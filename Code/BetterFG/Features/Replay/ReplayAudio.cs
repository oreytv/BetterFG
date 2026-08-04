using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Utilities;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BetterFG.Features.Replay
{
    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.PlayCharacterAudio))]
    internal static class ReplayCharacterAudioPatch
    {
        [HarmonyPrefix]
        public static void Prefix(AudioEvent2D3DPairSO pair, FallGuysCharacterController characterController, Vector3 pos, AudioParamContainer paramContainer)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CaptureAudio(pair, characterController, pos, paramContainer);
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.PlaySpeechBubbleAudio))]
    internal static class ReplaySpeechBubbleAudioPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string audioEvent, FallGuysCharacterController characterController)
        {
            if (FeatureReplay.Live == null || characterController == null) return;
            FeatureReplay.CaptureAudio(audioEvent, characterController, characterController.transform.position);
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.PlayOneShot), new Type[] { typeof(string), typeof(Vector3) })]
    internal static class ReplayObjectAudioPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string __0, Vector3 __1)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CaptureAudio(__0, null, __1);
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.PlayOneShotAttached), new Type[] { typeof(string), typeof(GameObject) })]
    internal static class ReplayAttachedAudioPatch
    {
        [HarmonyPrefix]
        public static void Prefix(string __0, GameObject __1)
        {
            if (FeatureReplay.Live == null || __1 == null) return;
            FeatureReplay.CaptureAudio(__0, null, __1.transform.position);
        }
    }

    [HarmonyPatch]
    internal static class ReplayCreateAudioPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> Targets()
        {
            foreach (var m in typeof(AudioManager).GetMethods())
            {
                if (m.Name != nameof(AudioManager.CreateAudio)) continue;
                var ps = m.GetParameters();
                if (ps.Length > 1 && (ps[1].ParameterType == typeof(Vector3) || ps[1].ParameterType == typeof(Transform)))
                    yield return m;
            }
        }

        [HarmonyPrefix]
        public static void Prefix(object[] __args)
        {
            if (FeatureReplay.Live == null || __args == null || __args.Length < 2) return;

            Vector3 pos;
            if (__args[1] is Vector3 at) pos = at;
            else if (__args[1] is Transform tf && tf != null) pos = tf.position;
            else return;

            FeatureReplay.CaptureAudio(__args[0] as string, null, pos);
        }
    }

    [HarmonyPatch(typeof(PlayAudioStateBehaviour), nameof(PlayAudioStateBehaviour.OnStateEnter))]
    internal static class ReplayAnimatorAudioPatch
    {
        [HarmonyPrefix]
        public static void Prefix(PlayAudioStateBehaviour __instance, Animator animator)
        {
            if (FeatureReplay.Live == null || animator == null) return;

            var controller = FeatureReplay.BeanFor(animator);
            if (controller == null) return;

            var source = __instance.TransformOverride != null ? __instance.TransformOverride : controller.transform;
            FeatureReplay.CaptureAudio(__instance._audioToPlay, controller, source.position);
        }
    }

    internal static class ReplayBankFreeze
    {
        public static bool Held;
    }

    [HarmonyPatch(typeof(SoundBankLoader), nameof(SoundBankLoader.LoadBanks))]
    internal static class ReplayBankLoadPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !ReplayBankFreeze.Held;
    }

    [HarmonyPatch(typeof(SoundBankLoader), nameof(SoundBankLoader.UnloadBanks))]
    internal static class ReplayBankUnloadPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !ReplayBankFreeze.Held;
    }

    internal class ReplayAudioPlayer
    {
        const float MAX_STEP = 0.5f;
        const float FAR_WORLD = 300f;
        const float NEAR_FRACTION = 0.15f;
        const float FAR_FRACTION = 0.45f;

        readonly ReplayRecording _rec;
        readonly HashSet<int> _reported = new HashSet<int>();
        readonly List<Transform> _ears = new List<Transform>();
        readonly List<Pose> _earPoses = new List<Pose>();
        int _listeners = 1;
        readonly List<string> _banks = new List<string>();
        readonly List<AsyncOperationHandle<TextAsset>> _assets = new List<AsyncOperationHandle<TextAsset>>();
        readonly float[] _range;
        readonly EventDescription[] _desc;
        Transform _cam;
        Vector3 _earPos;
        float _pitch = 1f;
        bool _culledRemotes = true;
        bool _ready;
        int _cursor;
        int _fired;
        int _failed;

        public ReplayAudioPlayer(ReplayRecording rec)
        {
            _rec = rec;
            _range = new float[rec.audioKeys.Count];
            _desc = new EventDescription[rec.audioKeys.Count];
        }

        public IEnumerator Prepare(Transform cam)
        {
            _cam = cam;
            _earPos = cam.position;

            var banks = new List<string>();
            try
            {
                Claim();
                banks = CollectBanks();

                foreach (var name in banks)
                {
                    if (!Begin(name, out var handle)) continue;
                    while (!handle.IsDone) yield return null;
                    Install(name, handle);
                }

                RuntimeManager.WaitForAllLoads();
            }
            finally
            {
                ClearMix();
                MeasureRanges();
                _ready = true;
                Plugin.Log.LogInfo($"replay audio armed: {_rec.audioEvents.Count} sounds over {_rec.audioKeys.Count} events, {_banks.Count} of {banks.Count} banks pulled in by us");
            }
        }

        void Claim()
        {
            _ears.Clear();
            _earPoses.Clear();

            foreach (var listener in UnityEngine.Object.FindObjectsOfType<StudioListener>())
            {
                var tf = listener.transform;
                tf.GetPositionAndRotation(out var pos, out var rot);
                _ears.Add(tf);
                _earPoses.Add(new Pose { position = pos, rotation = rot });
            }

            RuntimeManager.StudioSystem.getNumListeners(out _listeners);
            RuntimeManager.StudioSystem.setNumListeners(1);
            if (_ears.Count == 0) Plugin.Log.LogWarning("level has no StudioListener, so fmod listener 0 is ours to drive by hand");
        }

        List<string> CollectBanks()
        {
            var banks = new List<string>();
            var fmod = AudioManager.FMODData;

            foreach (var key in _rec.audioKeys)
            {
                var needed = fmod.GetEventBanks(key);
                if (needed == null || needed.BankNames == null)
                {
                    Plugin.Log.LogWarning($"replay audio: nothing lists a bank for '{key}', it may not play");
                    continue;
                }
                foreach (var name in needed.BankNames)
                    if (!string.IsNullOrEmpty(name) && !banks.Contains(name))
                        banks.Add(name);
            }

            var companions = new List<string>();
            foreach (var entry in fmod.SoundBanksArray)
            {
                if (entry == null) continue;
                string name = entry.Name;
                if (string.IsNullOrEmpty(name)) continue;

                foreach (var wanted in banks)
                {
                    if (!name.StartsWith(wanted + ".")) continue;
                    if (!companions.Contains(name)) companions.Add(name);
                }
            }

            banks.AddRange(companions);
            return banks;
        }

        static bool Begin(string name, out AsyncOperationHandle<TextAsset> handle)
        {
            handle = default;
            if (RuntimeManager.HasBankLoaded(name)) return false;

            try
            {
                var reference = AudioManager.FMODData.GetSoundBankAssetReference(name);
                if (reference == null || reference.AssetReference == null || !reference.AssetReference.RuntimeKeyIsValid())
                {
                    Plugin.Log.LogWarning($"replay audio: {name} has no addressable behind it, can't load it");
                    return false;
                }

                handle = Addressables.LoadAssetAsync<TextAsset>(reference.AssetReference.RuntimeKey);
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"replay audio: {name} wouldn't even start loading, {ex.Message}");
                return false;
            }
        }

        void Install(string name, AsyncOperationHandle<TextAsset> handle)
        {
            try
            {
                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    Plugin.Log.LogWarning($"replay audio: addressable for {name} came back {handle.Status}");
                    return;
                }

                var asset = handle.Result;
                RuntimeManager.LoadBank(asset, true);
                _banks.Add(asset.name);
                _assets.Add(handle);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"replay audio: {name} wouldn't load, {ex.Message}"); }
        }

        public void Release()
        {
            SetSpeed(1f);
            _ready = false;

            foreach (var description in _desc)
            {
                if (!description.isValid()) continue;
                if (description.getInstanceList(out Il2CppStructArray<EventInstance> live) != RESULT.OK || live == null) continue;

                foreach (var instance in live)
                    if (instance.isValid()) instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            }

            for (int i = 0; i < _ears.Count; i++)
                if (_ears[i] != null) _ears[i].SetPositionAndRotation(_earPoses[i].position, _earPoses[i].rotation);
            RuntimeManager.StudioSystem.setNumListeners(_listeners);
            _ears.Clear();
            _earPoses.Clear();

            int given = _banks.Count;
            foreach (var name in _banks) RuntimeManager.UnloadBank(name);
            foreach (var handle in _assets) if (handle.IsValid()) Addressables.Release(handle);
            _banks.Clear();
            _assets.Clear();

            var manager = UnityEngine.Object.FindObjectOfType<AudioManager>();
            if (manager != null) manager.RemovePlayersAudioActive = _culledRemotes;

            Plugin.Log.LogInfo($"replay audio done, {_fired} played and {_failed} dropped, {given} banks handed back, {_listeners} listener(s) back to the game");
        }

        public void Tick()
        {
            if (!_ready) return;

            _earPos = _cam.position;
            foreach (var ear in _ears)
                if (ear != null) ear.SetPositionAndRotation(_cam.position, _cam.rotation);

            RuntimeManager.StudioSystem.setListenerAttributes(0, RuntimeUtils.To3DAttributes(_cam));
        }

        void MeasureRanges()
        {
            for (int i = 0; i < _rec.audioKeys.Count; i++)
            {
                string key = _rec.audioKeys[i];
                try
                {
                    var guid = AudioManager.GetGuidForKey(key);
                    if (guid.IsNull) { Plugin.Log.LogWarning($"replay audio: '{key}' has no guid, it won't play"); continue; }

                    var description = RuntimeManager.GetEventDescription(guid);
                    if (!description.isValid()) { Plugin.Log.LogWarning($"replay audio: '{key}' has no description, its bank is probably missing"); continue; }

                    _desc[i] = description;
                    description.loadSampleData();

                    description.is3D(out bool spatial);
                    if (!spatial) continue;

                    description.getMinMaxDistance(out float _, out float max);
                    _range[i] = max;
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"replay audio: couldn't measure '{key}': {ex.Message}"); }
            }

            RuntimeManager.WaitForAllSampleLoading();

            for (int i = 0; i < _rec.audioKeys.Count; i++)
            {
                if (!_desc[i].isValid()) continue;
                _desc[i].getSampleLoadingState(out LOADING_STATE samples);
                if (samples != LOADING_STATE.LOADED)
                    Plugin.Log.LogWarning($"{_rec.audioKeys[i]} samples are still {samples} after the wait. that one plays silence");
            }
        }

        Vector3 Place(Vector3 soundPos, float max)
        {
            if (max <= 0f) return soundPos;

            var toSound = soundPos - _earPos;
            float distance = toSound.magnitude;

            float near = max * NEAR_FRACTION;
            if (distance <= near) return soundPos;

            float heard = max * FAR_FRACTION;
            float scaled = near + (distance - near) * (heard - near) / (FAR_WORLD - near);
            return _earPos + toSound / distance * Mathf.Min(scaled, heard);
        }

        // pitching the core master group rather than each instance means sounds already ringing when
        // the speed ramps follow it too, instead of only the ones fired after the change
        public void SetSpeed(float speed)
        {
            speed = Mathf.Clamp(speed, 0.05f, 8f);
            if (Mathf.Approximately(speed, _pitch)) return;
            _pitch = speed;

            try
            {
                if (RuntimeManager.StudioSystem.getCoreSystem(out var core) != RESULT.OK) return;
                if (core.getMasterChannelGroup(out var master) != RESULT.OK) return;
                master.setPitch(speed);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't pitch the replay mix to {speed:0.##}x: {ex.Message}"); }
        }

        public void Seek(float time)
        {
            var events = _rec.audioEvents;
            int i = 0;
            while (i < events.Count && events[i].t <= time) i++;
            _cursor = i;
        }

        public void Advance(float from, float to)
        {
            var events = _rec.audioEvents;
            if (events.Count == 0) return;

            if (!_ready || to <= from || to - from > MAX_STEP)
            {
                Seek(to);
                return;
            }

            while (_cursor < events.Count && events[_cursor].t <= to)
            {
                var sound = events[_cursor];
                _cursor++;
                if (sound.t > from) Fire(sound);
            }
        }

        void Fire(ReplayAudioEvent sound)
        {
            string key = _rec.audioKeys[sound.key];
            try
            {
                var guid = AudioManager.GetGuidForKey(key);
                if (guid.IsNull) { Blame(sound.key, key, "no guid for that key"); return; }

                var instance = RuntimeManager.CreateInstance(guid);
                if (!instance.isValid()) { Blame(sound.key, key, "instance came back invalid"); return; }

                instance.set3DAttributes(RuntimeUtils.To3DAttributes(Place(sound.pos, _range[sound.key])));

                for (int i = 0; i < sound.paramCount; i++)
                {
                    var parameter = _rec.audioParams[sound.paramStart + i];
                    string name = _rec.audioParamNames[parameter.name];

                    if (instance.setParameterByName(name, parameter.value, true) != RESULT.OK)
                        RuntimeManager.StudioSystem.setParameterByName(name, parameter.value, true);
                }

                instance.start();
                instance.release();
                _fired++;
            }
            catch (Exception ex) { Blame(sound.key, key, ex.Message); }
        }

        void Blame(int index, string key, string why)
        {
            _failed++;
            if (!_reported.Add(index)) return;
            Plugin.Log.LogWarning($"replay audio: '{key}' won't play, {why} ({_fired} played, {_failed} dropped so far)");
        }

        void ClearMix()
        {
            var manager = UnityEngine.Object.FindObjectOfType<AudioManager>();
            if (manager != null)
            {
                _culledRemotes = manager.RemovePlayersAudioActive;
                manager.RemovePlayersAudioActive = false;
            }

            RuntimeManager.MuteAllEvents(false);
            RuntimeManager.PauseAllEvents(false);
        }
    }
}
