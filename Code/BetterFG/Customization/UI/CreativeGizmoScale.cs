using System;
using System.Collections.Generic;
using System.Globalization;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Customization.UI
{
    internal static class CreativeGizmoScale
    {
        private const string Key = "creative.gizmo.scale";
        internal const float Min = 0.4f, Max = 3f;

        private static float _factor = 1f;
        private static float? _stockLineWidth;
        private static readonly Dictionary<IntPtr, float> _written = new Dictionary<IntPtr, float>();

        internal static float Scale
        {
            get => float.TryParse(SettingsService.Get(Key, "1"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float v) && v > 0f ? v : 1f;
            set => SettingsService.Set(Key, value.ToString(CultureInfo.InvariantCulture));
        }

        internal static void Apply()
        {
            _factor = Scale;
            _written.Clear();

            var mgr = LevelEditorWorldSpaceUIManager.Instance;
            var live = mgr != null ? mgr._activeWorldSpaceUIElements : null;
            if (live == null) return;
            for (int i = 0; i < live.Count; i++) Rescale(live[i]);
        }

        internal static void Rescale(LevelEditorWorldSpaceUI marker)
        {
            if (_factor == 1f || marker == null) return;

            var t = marker.transform;
            var s = t.localScale;
            if (!_written.TryGetValue(marker.Pointer, out float ours) || !Mathf.Approximately(s.x, ours))
            {
                s *= _factor;
                t.localScale = s;
                _written[marker.Pointer] = s.x;
            }

            var line = marker._lineRenderer;
            if (line == null) return;
            if (_stockLineWidth == null) _stockLineWidth = line.widthMultiplier;
            float want = _stockLineWidth.Value * _factor;
            if (!Mathf.Approximately(line.widthMultiplier, want)) line.widthMultiplier = want;
        }
    }
}
