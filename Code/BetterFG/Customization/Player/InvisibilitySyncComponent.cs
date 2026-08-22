using System;
using System.Collections.Generic;
using HarmonyLib;
using Levels.Invisibeans;
using UnityEngine;

namespace BetterFG.Customization.Player
{
    // keeps a UGC skin/accessory/item clone's own renderers in lockstep with the wearer's
    // hide-and-seek/invisibility powerup state, since our clones sit outside the renderer
    // lists InvisibilityVisualsController manages on the base body/costume.
    //
    // driven entirely off the game's own SetVisuals / ResetCharacterShadersAndColor calls (see the
    // patches below) — the controller only makes those while an invisibility effect is actually
    // live, so a normal round costs nothing. clones are indexed by their wearer's controller
    // pointer, so a bean with no clone on it is rejected by one dictionary miss.
    public class InvisibilitySyncComponent : MonoBehaviour
    {
        public GameObject playerObject;

        private InvisibilityVisualsController invisController;
        private readonly List<Renderer> ownRenderers = new List<Renderer>();
        private bool? lastHidden;

        // the patch pair is held out of the game until a clone actually exists to sync
        internal const string GateKey = "bfg.invis.clones";
        private static readonly Dictionary<IntPtr, List<InvisibilitySyncComponent>> _byController =
            new Dictionary<IntPtr, List<InvisibilitySyncComponent>>();
        private static bool _gateOn;

        private IntPtr _registeredUnder = IntPtr.Zero;

        void Start() => Setup();

        public void Setup()
        {
            Unregister();
            invisController = playerObject != null ? playerObject.GetComponentInChildren<InvisibilityVisualsController>() : null;
            ownRenderers.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != null) ownRenderers.Add(r);

            if (invisController is null || invisController.m_CachedPtr == IntPtr.Zero) return;

            _registeredUnder = invisController.m_CachedPtr;
            if (!_byController.TryGetValue(_registeredUnder, out var list))
            {
                list = new List<InvisibilitySyncComponent>();
                _byController[_registeredUnder] = list;
            }
            list.Add(this);
            SyncGate();
        }

        void OnDestroy() => Unregister();

        private void Unregister()
        {
            if (_registeredUnder == IntPtr.Zero) return;
            if (_byController.TryGetValue(_registeredUnder, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) _byController.Remove(_registeredUnder);
            }
            _registeredUnder = IntPtr.Zero;
            SyncGate();
        }

        private static void SyncGate()
        {
            bool want = _byController.Count > 0;
            if (want == _gateOn) return;
            _gateOn = want;
            Utilities.PatchGate.SetActive(GateKey, want);
        }

        // the dictionary miss is the fast reject: a bean with no clone on it costs one hash lookup
        // and never touches the controller's fields at all.
        internal static void PushFrom(InvisibilityVisualsController controller)
        {
            if (!_byController.TryGetValue(controller.m_CachedPtr, out var list)) return;
            float ratio = controller._visibilityRatio;
            bool hidden = controller._ready && ratio < 0.5f;
            for (int i = 0; i < list.Count; i++)
                list[i].Apply(hidden, ratio);
        }

        internal static void PushVisible(InvisibilityVisualsController controller)
        {
            if (!_byController.TryGetValue(controller.m_CachedPtr, out var list)) return;
            for (int i = 0; i < list.Count; i++)
                list[i].Apply(false, 1f);
        }

        private void Apply(bool hidden, float ratio)
        {
            if (lastHidden == hidden) return;
            lastHidden = hidden;
            Plugin.Log.LogInfo($"skin clone on {playerObject.name} going {(hidden ? "invisible" : "visible")}, ratio={ratio:0.00}");

            for (int i = 0; i < ownRenderers.Count; i++)
            {
                var r = ownRenderers[i];
                if (r is null || r.m_CachedPtr == IntPtr.Zero) continue;
                r.enabled = !hidden;
            }
        }
    }

    // the game's per-effect visual push. carries the live fade ratio, so the clone crosses at the
    // same halfway point the body does instead of popping at the start of the fade.
    [Utilities.BfgPatchGate(InvisibilitySyncComponent.GateKey)]
    [HarmonyPatch(typeof(InvisibilityVisualsController), "SetVisuals")]
    internal static class InvisibilitySetVisualsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InvisibilityVisualsController __instance)
            => InvisibilitySyncComponent.PushFrom(__instance);
    }

    // the canonical "put this character back to normal" call — powerup end, controller disable and
    // round teardown all land here. without it a clone that stopped getting SetVisuals mid-fade
    // would stay hidden for good.
    [Utilities.BfgPatchGate(InvisibilitySyncComponent.GateKey)]
    [HarmonyPatch(typeof(InvisibilityVisualsController), nameof(InvisibilityVisualsController.ResetCharacterShadersAndColor), new Type[] { })]
    internal static class InvisibilityResetShadersPatch
    {
        [HarmonyPostfix]
        public static void Postfix(InvisibilityVisualsController __instance)
            => InvisibilitySyncComponent.PushVisible(__instance);
    }
}
