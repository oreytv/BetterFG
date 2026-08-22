using System;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class HideZoneArchEffectsTweak : BfgTweak
    {
        public HideZoneArchEffectsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "hide_zone_arch_effects";
        public override string TweakLabel => "Hide Zone/Speed VFX";
        public override bool DefaultEnabled => false;

        public static HideZoneArchEffectsTweak Instance { get; private set; }
        void Awake() => Instance = this;

        internal static void StripSpeedArchCameraLines()
        {
            var player = UnityEngine.Object.FindObjectOfType<FG.Common.SpeedBoostCameraScreenVFXPlayer>();
            if (player == null) return;
            StripByName(player.transform, "Prefab_VFX_SpeedArch_Camera_Lines(Clone)");
            StripByName(player.transform, "Prefab_VFX_Speedarch_Stacked_Camera_Lines(Clone)");
        }

        private static void StripByName(Transform root, string name)
        {
            var t = root.Find(name);
            if (t == null) return;
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }
    }
}
