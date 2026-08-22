using System;
using System.Collections.Generic;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    // base for tabs that just want the standard embedded-PNG backdrop + the hover-in shine. subclass
    // overrides BgResource with its own png; the transform magic numbers and the BG_Hover wiring live
    // here once instead of being copied into every tab.
    public class TexturedTab : Tab
    {
        public TexturedTab(IntPtr ptr) : base(ptr) { }

        protected virtual string BgResource => null;

        private static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();
        private GameObject _bgHoverGo;

        private static Texture2D LoadTex(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return null;
            if (_texCache.TryGetValue(resource, out var t) && t != null) return t;
            t = EmbeddedResourceandUnity.LoadTexture(resource);
            _texCache[resource] = t;
            return t;
        }

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex(BgResource);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0f);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = LoadTex("BetterFG.assets.ui.bg_hover.png");
            if (hoverTex == null) return;

            var hoverGo = new GameObject("BG_Hover");
            hoverGo.transform.SetParent(bgGo.transform, false);
            var hoverRt = hoverGo.AddComponent<RectTransform>();
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
            hoverGo.AddComponent<RawImage>().texture = hoverTex;
            hoverGo.SetActive(false);
            _bgHoverGo = hoverGo;
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }
    }
}
