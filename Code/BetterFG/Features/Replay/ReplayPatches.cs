using System;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Utilities;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

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

        [HarmonyPostfix]
        public static void Postfix(object[] __args, EventInstanceReference __result)
        {
            if (FeatureReplay.Live == null || __args == null || __args.Length < 2) return;

            Vector3 pos;
            if (__args[1] is Vector3 at) pos = at;
            else if (__args[1] is Transform tf && tf != null) pos = tf.position;
            else return;

            FeatureReplay.CaptureHeldAudio(__result, __args[0] as string, pos);
        }
    }

    [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.StopAndReleaseAudioEvent))]
    internal static class ReplayStopAudioPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref EventInstanceReference instanceReference)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CloseHeldAudio(instanceReference);
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

    [HarmonyPatch(typeof(Levels.Obstacles.COMMON_PrefabSpawnerBase), nameof(Levels.Obstacles.COMMON_PrefabSpawnerBase.OnInstantiateObject))]
    internal static class ReplaySpawnerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GameObject go)
        {
            if (FeatureReplay.Live == null || go == null) return;
            ReplayWorldRecorder.OnLocalSpawn(go, FeatureReplay.GameplayTime);
        }
    }

    [HarmonyPatch(typeof(FG.Common.GameObjectPool), nameof(FG.Common.GameObjectPool.PrepareObjectForUse))]
    internal static class ReplayPoolGetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GameObject go)
        {
            if (FeatureReplay.Live == null || go == null) return;
            ReplayWorldRecorder.OnLocalSpawn(go, FeatureReplay.GameplayTime);
        }
    }

    [HarmonyPatch(typeof(FG.Common.GameObjectPool), nameof(FG.Common.GameObjectPool.PrepareObjectForStorage))]
    internal static class ReplayPoolReturnPatch
    {
        [HarmonyPrefix]
        public static void Prefix(GameObject go)
        {
            if (FeatureReplay.Live == null || go == null) return;
            ReplayWorldRecorder.OnLocalDespawn(go, FeatureReplay.GameplayTime);
        }
    }

    [HarmonyPatch(typeof(FG.Common.Character.FallGuyVFXController), nameof(FG.Common.Character.FallGuyVFXController.HandleOnDive))]
    internal static class ReplayDiveSlideVfxPatch
    {
        [HarmonyPrefix]
        public static void Prefix(FG.Common.Character.FallGuyVFXController __instance)
        {
            if (FeatureReplay.Live == null || __instance == null) return;
            FeatureReplay.CaptureDiveSlideVfx(__instance);
        }
    }

    [HarmonyPatch(typeof(Levels.Starlink.StarlinkNode), nameof(Levels.Starlink.StarlinkNode.OnButtonPress))]
    internal static class ReplayStarchartButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Levels.Starlink.StarlinkNode __instance)
        {
            if (FeatureReplay.Live == null) return;
            FeatureReplay.CaptureStarchartButtonPress(__instance);
        }
    }
}
