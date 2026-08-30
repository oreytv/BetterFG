using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    public static partial class UGUIShip
    {
        // ── one knob for the whole mod UI font ───────────────────────────────
        // this game's IL2CPP build strips Font.CreateDynamicFontFromOSFont, so a legacy UI.Text can't
        // take a ttf at runtime. instead every mod label keeps its (invisible) legacy Text as the
        // layout/measure driver and gets a TextMeshProUGUI child that mirrors it, rendered in a
        // TMP_FontAsset built from the embedded ttf. swap the font = drop a ttf in assets/ui/general/
        // and repoint UIFontResource.
        public static string UIFontResource = "BetterFG.assets.ui.general.CharterBold.ttf";

        private static Font _arial;
        private static Font Arial => _arial != null ? _arial : (_arial = Resources.GetBuiltinResource<Font>("Arial.ttf"));

        private static bool _uiFontTried;
        private static TMPro.TMP_FontAsset _uiFont;
        public static TMPro.TMP_FontAsset UIFont
        {
            get
            {
                if (_uiFont != null || _uiFontTried) return _uiFont;
                _uiFontTried = true;
                try
                {
                    using var s = ResourceAssembly.GetManifestResourceStream(UIFontResource);
                    if (s == null) { Log.LogWarning("UI font: no embedded resource " + UIFontResource + ", mod UI stays on Arial"); return null; }
                    var bytes = new byte[s.Length];
                    s.Read(bytes, 0, bytes.Length);
                    var path = Path.Combine(Application.temporaryCachePath, "bfg_" + Path.GetFileName(UIFontResource));
                    File.WriteAllBytes(path, bytes);

                    var fa = TMPro.TMP_FontAsset.CreateFontAsset(path, 0, 90, 9,
                        UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                        TMPro.AtlasPopulationMode.Dynamic, false);
                    if (fa == null) { Log.LogWarning("UI font: CreateFontAsset came back null"); return null; }
                    fa.hideFlags = HideFlags.HideAndDontSave;
                    fa.name = "BFG_UIFont";
                    var warm = new System.Text.StringBuilder();
                    for (int c = 0x20; c <= 0x7E; c++) warm.Append((char)c);
                    try { fa.TryAddCharacters(warm.ToString(), false); } catch { }
                    _uiFont = fa;
                    Log.LogInfo("UI font ready: BFG_UIFont from " + Path.GetFileName(UIFontResource));
                }
                catch (Exception ex) { Log.LogWarning("UI font build failed, mod UI stays on Arial: " + ex.Message); }
                return _uiFont;
            }
        }

        private sealed class TmpMirror
        {
            public Text src;
            public TMPro.TextMeshProUGUI dst;
            public string lastText;
            public Color lastColor = new Color(-1f, -1f, -1f, -1f);
            public int lastSize = -1;
            public TextAnchor lastAnchor = (TextAnchor)(-1);
            public FontStyle lastStyle = (FontStyle)(-1);
            public int lastRich = -1;
            public bool lastEnabled = true;
        }
        private static readonly System.Collections.Generic.List<TmpMirror> _mirrors = new System.Collections.Generic.List<TmpMirror>();

        // give a freshly built legacy Text the mod font: keep it as the (invisible) measure/layout
        // driver, overlay a TMP child that tracks its text/colour/size/alignment every frame.
        public static void Stylize(Text t)
        {
            if (t == null) return;
            var font = UIFont;
            if (font == null) return; // no ttf -> leave it as plain Arial
            if (t.transform.Find("TmpMirror") != null) return;

            t.font = Arial;
            t.canvasRenderer.SetColor(new Color(1f, 1f, 1f, 0f));
            // a SetActive(false)->true cycle (tab/window open-close, list rebuilds) resets the
            // CanvasRenderer colour back to opaque, so the Arial glyphs reappear offset behind the
            // TMP mirror ("ghost text"). re-assert the invisible multiplier on every re-enable.
            if (t.gameObject.GetComponent<StylizeGuard>() == null) t.gameObject.AddComponent<StylizeGuard>();

            var go = new GameObject("TmpMirror");
            go.SetActive(false);
            go.transform.SetParent(t.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var d = go.AddComponent<TMPro.TextMeshProUGUI>();
            d.font = font;
            d.raycastTarget = false;
            d.richText = t.supportRichText;
            d.enableWordWrapping = t.horizontalOverflow == HorizontalWrapMode.Wrap;
            d.overflowMode = TMPro.TextOverflowModes.Overflow;
            go.SetActive(true);

            _mirrors.Add(new TmpMirror { src = t, dst = d });
        }

        // opt a label back out of the mod font — drop its TMP mirror and let the legacy Arial Text
        // render again. for the few surfaces that should stay Arial (tab titles).
        public static void Unstylize(Text t)
        {
            if (t == null) return;
            for (int i = _mirrors.Count - 1; i >= 0; i--)
            {
                if (_mirrors[i].src != t) continue;
                if (_mirrors[i].dst != null) UnityEngine.Object.Destroy(_mirrors[i].dst.gameObject);
                _mirrors.RemoveAt(i);
            }
            var guard = t.gameObject.GetComponent<StylizeGuard>();
            if (guard != null) UnityEngine.Object.Destroy(guard);
            t.canvasRenderer.SetColor(Color.white);
        }

        // pumped once per frame from BetterFGUIMan.Update
        public static void PumpTextMirrors()
        {
            for (int i = _mirrors.Count - 1; i >= 0; i--)
            {
                var m = _mirrors[i];
                if (m.src == null || m.dst == null) { _mirrors.RemoveAt(i); continue; }
                var s = m.src;

                // InputField hides its placeholder by disabling the Text component, not the
                // GameObject (SetActive) — mirror that or the TMP overlay keeps showing stale
                // placeholder text on top of what's actually typed.
                if (s.enabled != m.lastEnabled) { m.dst.enabled = s.enabled; m.lastEnabled = s.enabled; }
                if (!string.Equals(s.text, m.lastText, StringComparison.Ordinal)) { m.dst.text = s.text; m.lastText = s.text; }
                if (s.color != m.lastColor) { m.dst.color = s.color; m.lastColor = s.color; }
                if (s.fontSize != m.lastSize) { m.dst.fontSize = s.fontSize; m.lastSize = s.fontSize; }
                if (s.alignment != m.lastAnchor) { m.dst.alignment = MapAlign(s.alignment); m.lastAnchor = s.alignment; }
                if (s.fontStyle != m.lastStyle) { m.dst.fontStyle = MapStyle(s.fontStyle); m.lastStyle = s.fontStyle; }
                int rich = s.supportRichText ? 1 : 0;
                if (rich != m.lastRich) { m.dst.richText = s.supportRichText; m.lastRich = rich; }
            }
        }

        private static TMPro.TextAlignmentOptions MapAlign(TextAnchor a) => a switch
        {
            TextAnchor.UpperLeft => TMPro.TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TMPro.TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TMPro.TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TMPro.TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TMPro.TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TMPro.TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TMPro.TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TMPro.TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TMPro.TextAlignmentOptions.BottomRight,
            _ => TMPro.TextAlignmentOptions.Left,
        };

        private static TMPro.FontStyles MapStyle(FontStyle s) => s switch
        {
            FontStyle.Bold => TMPro.FontStyles.Bold,
            FontStyle.Italic => TMPro.FontStyles.Italic,
            FontStyle.BoldAndItalic => TMPro.FontStyles.Bold | TMPro.FontStyles.Italic,
            _ => TMPro.FontStyles.Normal,
        };
    }
}
