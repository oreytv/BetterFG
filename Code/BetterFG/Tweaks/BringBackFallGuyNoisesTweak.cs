using System;
using BetterFG.Utilities;
using FG.Common;
using FG.Common.Audio;
using FGClient;
using FMOD;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class BringBackFallGuyNoisesTweak : BfgTweak
    {
        public BringBackFallGuyNoisesTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "bring_back_fallguy_noises";
        public override string TweakLabel => "Bring Back Fall Guy noises";
        public override bool DefaultEnabled => true;

        public static BringBackFallGuyNoisesTweak Instance { get; private set; }

        internal static bool Active;

        void Awake() => Instance = this;

        public override void EnableTweak() => Active = true;
        public override void DisableTweak() => Active = false;

        public override void OnRoundStart() => ApplyFix();

        private static readonly AudioParamContainer _params = new AudioParamContainer();
        private const float RewooCooldown = 1.25f;
        private const float FallingWoo = 0.8f;
        private const float HardLandingThreshold = 4f;
        private const float HitThreshold = 3f;
        private const float HitVoiceCooldown = 0.3f;

        private enum Voe { Falling, Dive, Hit }

        private static readonly System.Collections.Generic.Dictionary<long, bool> _was = new System.Collections.Generic.Dictionary<long, bool>();
        private static readonly System.Collections.Generic.Dictionary<long, float> _last = new System.Collections.Generic.Dictionary<long, float>();
        private static readonly System.Collections.Generic.Dictionary<int, bool> _wasGrounded = new System.Collections.Generic.Dictionary<int, bool>();
        private static readonly System.Collections.Generic.Dictionary<int, float> _peakFall = new System.Collections.Generic.Dictionary<int, float>();
        private static readonly System.Collections.Generic.Dictionary<int, float> _fallTime = new System.Collections.Generic.Dictionary<int, float>();

        internal static void TickRemote(FallGuysCharacterController c)
        {
            if (c == null) return;
            try
            {
                var master = AudioManager.EventMasterData;
                if (master == null) return;

                int id = c.GetInstanceID();
                bool grounded = c.IsTouchingGround;
                float yvel = c.AnimatedPlayerVelocity.y;

                bool descending = !grounded && yvel < 0f;
                _fallTime.TryGetValue(id, out float dropping);
                dropping = descending ? dropping + Time.deltaTime : 0f;
                _fallTime[id] = dropping;

                Fire(c, Voe.Falling, dropping >= FallingWoo, master.VO_Falling);
                Fire(c, Voe.Dive, c.IsDiving, master.Dive);

                if (!grounded)
                {
                    _peakFall.TryGetValue(id, out float peak);
                    if (yvel < peak) _peakFall[id] = yvel;
                }

                _wasGrounded.TryGetValue(id, out bool wasGrounded);
                _wasGrounded[id] = grounded;

                if (grounded && !wasGrounded)
                {
                    _peakFall.TryGetValue(id, out float peak);
                    _peakFall[id] = 0f;
                    float landSpeed = -peak;
                    if (landSpeed >= HardLandingThreshold) Yelp(c, landSpeed);
                }
            }
            catch { }
        }

        internal static void HandleRemoteCollision(FallGuysCharacterController c, UnityEngine.Collision collision)
        {
            if (c == null || collision == null) return;
            try
            {
                float rel = collision.relativeVelocity.magnitude;
                if (rel < HitThreshold) return;

                Yelp(c, rel);

                var rb = collision.rigidbody;
                var otherBean = rb != null ? rb.GetComponent<FallGuysCharacterController>() : null;
                if (otherBean == null) otherBean = collision.gameObject.GetComponentInParent<FallGuysCharacterController>();
                if (otherBean == null || otherBean.IsLocalPlayer) return;

                Yelp(otherBean, rel);
            }
            catch { }
        }

        private static bool _probedHit;

        private static void ProbeHitEvent()
        {
            _probedHit = true;
            var master = AudioManager.EventMasterData;

            foreach (string key in new[] { master.VO_Hit.audioEvent2D, master.VO_Hit.audioEvent3D, master.VO_Effort.audioEvent3D })
            {
                var desc = RuntimeManager.GetEventDescription(AudioManager.GetGuidForKey(key));
                if (!desc.isValid()) { Plugin.Log.LogWarning($"{key}: no description, bank not loaded"); continue; }

                desc.getLength(out int len);
                desc.is3D(out bool spatial);

                string ps = "";
                if (desc.getParameterDescriptionCount(out int n) == RESULT.OK)
                    for (int i = 0; i < n; i++)
                        if (desc.getParameterDescriptionByIndex(i, out var pd) == RESULT.OK)
                            ps += $" {(string)pd.name}[{pd.minimum}..{pd.maximum} default {pd.defaultvalue}]";

                Plugin.Log.LogInfo($"{key}: {len}ms, 3d={spatial}, params:{(ps.Length == 0 ? " none" : ps)}");
            }
        }

        private static void Yelp(FallGuysCharacterController c, float strength)
        {
            long key = Key(c, Voe.Hit);
            _last.TryGetValue(key, out float last);
            if (Time.time - last < HitVoiceCooldown) return;
            _last[key] = Time.time;

            if (!_probedHit) ProbeHitEvent();

            var master = AudioManager.EventMasterData;
            var p = new AudioParamContainer(2);
            p.AddBool(true);
            p.AddFloat(master.ImpactStrengthParam, Mathf.Clamp(strength * 25f, 60f, 500f));
            AudioManager.PlayCharacterAudio(master.VO_Hit, c, c.transform.position, p);
        }

        private static void Fire(FallGuysCharacterController c, Voe ev, bool condition, AudioEvent2D3DPairSO pair)
        {
            long key = Key(c, ev);

            _was.TryGetValue(key, out bool was);
            _was[key] = condition;
            if (!condition || was) return;

            if (IsOnCooldown(c, ev)) return;
            StampCooldown(c, ev);
            PlayPair(c, pair);
        }

        private static long Key(FallGuysCharacterController c, Voe ev) => ((long)c.GetInstanceID() << 4) | (long)ev;

        private static bool IsOnCooldown(FallGuysCharacterController c, Voe ev)
        {
            _last.TryGetValue(Key(c, ev), out float last);
            return Time.time - last < RewooCooldown;
        }

        private static void StampCooldown(FallGuysCharacterController c, Voe ev) => _last[Key(c, ev)] = Time.time;

        private static void PlayPair(FallGuysCharacterController c, AudioEvent2D3DPairSO pair)
        {
            if (pair == null) return;
            AudioManager.PlayCharacterAudio(pair, c, c.transform.position, _params);
        }

        internal static void ApplyFix()
        {
            try
            {
                var am = UnityEngine.Object.FindObjectOfType<AudioManager>();
                if (am != null) am.RemovePlayersAudioActive = false;
            }
            catch { }

            FmodUtil.UnmuteBus("bus:/MASTER/BUS_VO");
            FmodUtil.UnmuteBus("bus:/MASTER/BUS_VO/BUS_VO_3D");
            FmodUtil.UnmuteBus("bus:/MASTER/BUS_VO/BUS_VO_2D");
        }
    }

    [HarmonyPatch(typeof(FallGuysCharacterController), nameof(FallGuysCharacterController.OnManagedUpdate_Remote))]
    internal static class BringBackFallGuyNoises_RemoteTick
    {
        [HarmonyPostfix]
        public static void Postfix(FallGuysCharacterController __instance)
        {
            if (!BringBackFallGuyNoisesTweak.Active) return;
            BringBackFallGuyNoisesTweak.TickRemote(__instance);
        }
    }

    [HarmonyPatch(typeof(FallGuysCharacterController), nameof(FallGuysCharacterController.OnCollisionEnter))]
    internal static class BringBackFallGuyNoises_Collision
    {
        [HarmonyPostfix]
        public static void Postfix(FallGuysCharacterController __instance, UnityEngine.Collision collision)
        {
            if (!BringBackFallGuyNoisesTweak.Active || __instance == null) return;
            if (__instance.IsLocalPlayer) return;
            BringBackFallGuyNoisesTweak.HandleRemoteCollision(__instance, collision);
        }
    }
}
