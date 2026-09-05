using System;
using System.Collections.Generic;
using BetterFG.Services;
using FGClient;
using UnityEngine;

namespace BetterFG.Nametag
{
    public static class NametagAllPlayersService
    {
        public const string KEY_HIDE_ARROW = "nametag.all.hide_arrow";
        public const string KEY_SCALE = "nametag.all.scale";

        private static bool _hideArrow;
        private static float _scale = 1f;
        private static int _readFrame = -1;

        private class Row
        {
            public Transform t;
            public Vector3 lastScale;
            public GameObject arrowGo;
            public bool lastArrowActive;
        }

        private class HudCache
        {
            public int count = -1;
            public readonly List<Row> rows = new List<Row>();
        }

        private static readonly Dictionary<IntPtr, HudCache> _huds = new Dictionary<IntPtr, HudCache>();

        public static void Invalidate() { _huds.Clear(); _readFrame = -1; }

        public static void Tick(PlayerInfoHUDBase hud)
        {
            var spawned = hud?._spawnedInfoObjects;
            if (spawned == null) return;

            int frame = Time.frameCount;
            if (_readFrame != frame)
            {
                _readFrame = frame;
                _hideArrow = SettingsService.Get(KEY_HIDE_ARROW, "false") == "true";
                _scale = float.TryParse(SettingsService.Get(KEY_SCALE, "1"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out float s) ? Mathf.Clamp(s, 0.05f, 10f) : 1f;
            }

            bool identity = !_hideArrow && Mathf.Abs(_scale - 1f) < 0.0005f;
            if (identity && !_huds.ContainsKey(hud.Pointer)) return;

            int count = spawned.Count;
            IntPtr id = hud.Pointer;
            if (!_huds.TryGetValue(id, out var cache))
            {
                cache = new HudCache();
                _huds[id] = cache;
            }
            if (cache.count != count)
            {
                cache.count = count;
                cache.rows.Clear();
                for (int i = 0; i < count; i++)
                {
                    var display = spawned[i]?.playerInfo;
                    if (display == null) continue;
                    GameObject arrow = null;
                    var go = display.TryCast<PlayerInfoDisplayGameObject>();
                    if (go != null && go._arrowRenderer != null) arrow = go._arrowRenderer.gameObject;
                    else
                    {
                        var canvas = display.TryCast<PlayerInfoDisplayCanvas>();
                        if (canvas != null && canvas._arrowImage != null) arrow = canvas._arrowImage.gameObject;
                    }
                    cache.rows.Add(new Row { t = display.transform, arrowGo = arrow, lastScale = Vector3.one, lastArrowActive = true });
                }
            }

            var wantScale = new Vector3(_scale, _scale, _scale);
            bool wantArrow = !_hideArrow;
            var list = cache.rows;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r.t == null || r.t.m_CachedPtr == IntPtr.Zero) { cache.count = -1; return; }
                if (r.lastScale != wantScale) { r.t.localScale = wantScale; r.lastScale = wantScale; }
                if (r.arrowGo != null && r.lastArrowActive != wantArrow)
                {
                    r.arrowGo.SetActive(wantArrow);
                    r.lastArrowActive = wantArrow;
                }
            }
        }
    }
}
