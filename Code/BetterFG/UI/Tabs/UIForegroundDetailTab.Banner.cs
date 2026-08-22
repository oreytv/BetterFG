using System;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tabs
{
    public partial class UIForegroundDetailTab
    {
        private struct CachedImgColor { public Image img; public Color orig; public bool isHighlight; }
        private struct CachedTmpColor { public TMPro.TMP_Text tmp; public Color origFill; public Color origOutline; public Color origUnderlay; public bool hasOutline; public bool hasUnderlay; }
        private System.Collections.Generic.List<CachedImgColor> _previewImgCache = new System.Collections.Generic.List<CachedImgColor>();
        private System.Collections.Generic.List<CachedTmpColor> _previewTmpCache = new System.Collections.Generic.List<CachedTmpColor>();
        private GameObject _bannerPreviewGo;

        private class BannerColourUI
        {
            public bool on;
            public float r, g, b;
            public Button toggleBtn;
            public Image swatch;
            public Image areaBg;
        }

        private class BannerSlotUI
        {
            public Customization.Menu.MenuCustomizationApplication.BannerBucket bucket;
            public string label;
            public string keyPrefix;
            public float dr, dg, db;
            public readonly BannerColourUI ui = new BannerColourUI();
        }

        private class BannerDef
        {
            public System.Collections.Generic.List<BannerSlotUI> slots;
            public BannerSlotUI highlight;
            public Transform viewport;
            public string enabledKey;
            public bool enabled;
            public Button enabledBtn;
        }

        private System.Collections.Generic.Dictionary<UIForegroundKind, BannerDef> _bannerDefs;

        private static void SetBannerToggle(BannerColourUI ch, bool on)
        {
            ch.on = on;
            var lbl = ch.toggleBtn?.GetComponentInChildren<Text>();
            if (lbl != null) lbl.text = on ? "ON" : "OFF";
            UGUIShip.SetButtonSelected(ch.toggleBtn, on, SEL_COLOR);
        }

        private BannerSlotUI MkSlot(Customization.Menu.MenuCustomizationApplication.BannerBucket bucket,
            string label, string keyPrefix, float dr, float dg, float db)
        {
            var s = new BannerSlotUI { bucket = bucket, label = label, keyPrefix = keyPrefix, dr = dr, dg = dg, db = db };
            s.ui.r = dr; s.ui.g = dg; s.ui.b = db;
            return s;
        }

        private BannerDef GetBannerDef(UIForegroundKind what)
        {
            if (_bannerDefs == null) BuildBannerDefs();
            return _bannerDefs.TryGetValue(what, out var d) ? d : null;
        }

        private void BuildBannerDefs()
        {
            var B = Customization.Menu.MenuCustomizationApplication.BannerBucket.Black;
            var W = Customization.Menu.MenuCustomizationApplication.BannerBucket.White;
            var C = Customization.Menu.MenuCustomizationApplication.BannerBucket.Cyan;
            var P = Customization.Menu.MenuCustomizationApplication.BannerBucket.Pink;
            var Y = Customization.Menu.MenuCustomizationApplication.BannerBucket.Yellow;
            var O = Customization.Menu.MenuCustomizationApplication.BannerBucket.Orange;
            var Bl = Customization.Menu.MenuCustomizationApplication.BannerBucket.Blue;
            var BG = Customization.Menu.MenuCustomizationApplication.BannerBucket.BlackGrey;

            _bannerDefs = new System.Collections.Generic.Dictionary<UIForegroundKind, BannerDef>
            {
                [UIForegroundKind.Qualified] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(C, "CYAN REPLACEMENT",  "menu.banner.qual.cyan",  0f,    0.78f, 1f),
                        MkSlot(P, "PINK REPLACEMENT",  "menu.banner.qual.pink",  1f,    0.2f,  0.5f),
                        MkSlot(B, "BLACK REPLACEMENT", "menu.banner.qual.black", 0.08f, 0.08f, 0.08f),
                        MkSlot(W, "WHITE REPLACEMENT", "menu.banner.qual.white", 1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.qual.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_QUAL_ENABLED,
                },
                [UIForegroundKind.Eliminated] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(C, "CYAN REPLACEMENT",  "menu.banner.elim.cyan",  0f,    0.78f, 1f),
                        MkSlot(P, "PINK REPLACEMENT",  "menu.banner.elim.pink",  1f,    0.2f,  0.5f),
                        MkSlot(B, "BLACK REPLACEMENT", "menu.banner.elim.black", 0.08f, 0.08f, 0.08f),
                        MkSlot(W, "WHITE REPLACEMENT", "menu.banner.elim.white", 1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.elim.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_ELIM_ENABLED,
                },
                [UIForegroundKind.Winner] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(Y, "YELLOW REPLACEMENT", "menu.banner.win.yellow", 1f,    0.85f, 0f),
                        MkSlot(O, "ORANGE REPLACEMENT", "menu.banner.win.orange", 1f,    0.55f, 0.1f),
                        MkSlot(W,  "WHITE REPLACEMENT", "menu.banner.win.white",  1f,    1f,    1f),
                        MkSlot(BG, "BLACK REPLACEMENT", "menu.banner.win.black",  0.08f, 0.08f, 0.08f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.win.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_WIN_ENABLED,
                },
                [UIForegroundKind.RoundOver] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(BG, "BLACK REPLACEMENT", "menu.banner.round.black", 0.08f, 0.08f, 0.08f),
                        MkSlot(P,  "PINK REPLACEMENT",  "menu.banner.round.pink",  1f,    0.2f,  0.5f),
                        MkSlot(C,  "CYAN REPLACEMENT",  "menu.banner.round.blue",  0f,    0.78f, 1f),
                        MkSlot(W,  "WHITE REPLACEMENT", "menu.banner.round.white", 1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.round.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_ROUND_ENABLED,
                },
                [UIForegroundKind.EliminatedSquad] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(O,  "ORANGE REPLACEMENT", "menu.banner.squad.orange", 1f,    0.55f, 0.1f),
                        MkSlot(BG, "BLACK REPLACEMENT",  "menu.banner.squad.black",  0.08f, 0.08f, 0.08f),
                        MkSlot(P,  "PINK REPLACEMENT",   "menu.banner.squad.pink",   1f,    0.2f,  0.5f),
                        MkSlot(C,  "CYAN REPLACEMENT",   "menu.banner.squad.blue",   0f,    0.78f, 1f),
                        MkSlot(Y,  "YELLOW REPLACEMENT", "menu.banner.squad.yellow", 1f,    0.85f, 0f),
                        MkSlot(W,  "WHITE REPLACEMENT",  "menu.banner.squad.white",  1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.squad.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_SQUAD_ENABLED,
                },
            };
        }

        private const float BANNER_PREVIEW_SPACE = 120f;

        private void BuildBannerPanel(RectTransform parent, float x, float y, float w, float h, UIForegroundKind what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            LoadBannerSettings(what);

            float sectionH = LH + SH + BTN_H + SH + (LH + SH) * 2f + LH;
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            def.viewport = scrollRect.transform.Find("Viewport");

            float cy = BANNER_PREVIEW_SPACE;
            float swatchW = BTN_H;
            float toggleW = BTN_H * 2.2f;
            float slidersW = w - swatchW - toggleW - PAD * 2f;
            float fullSliderW = slidersW + toggleW + swatchW + PAD;

            def.enabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                def.enabled ? "CUSTOM COLOURS: ON" : "CUSTOM COLOURS: OFF",
                def.enabled ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    def.enabled = !def.enabled;
                    SettingsService.Set(def.enabledKey, def.enabled ? "true" : "false");
                    var lbl = def.enabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = def.enabled ? "CUSTOM COLOURS: ON" : "CUSTOM COLOURS: OFF";
                    UGUIShip.SetButtonSelected(def.enabledBtn, def.enabled, SEL_COLOR);
                    UpdateBannerPreviewColours();
                }));
            cy += BTN_H + SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            foreach (var slot in def.slots)
                BuildBannerSection(content, x, ref cy, w, sectionH, fullSliderW, swatchW, toggleW, slot);
            BuildBannerSection(content, x, ref cy, w, sectionH, fullSliderW, swatchW, toggleW, def.highlight);

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildBannerSection(Transform content, float x, ref float cy, float w, float sectionH,
            float fullSliderW, float swatchW, float toggleW, BannerSlotUI slot)
        {
            string title = slot.label;
            var ch = slot.ui;
            float sectionStart = cy;
            var bgGo = new GameObject(title + "_AreaBg");
            bgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(bgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, sectionStart - 3f, w + 6f, sectionH + 6f));
            ch.areaBg = bgGo.AddComponent<Image>();
            ch.areaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            ch.areaBg.type = Image.Type.Simple;
            ch.areaBg.color = new Color(ch.r, ch.g, ch.b, 0.18f);
            ch.areaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), title, FS_SM, HINT);
            cy += LH + SH;

            ch.toggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                ch.on ? "ON" : "OFF", ch.on ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    ch.on = !ch.on;
                    var lbl = ch.toggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = ch.on ? "ON" : "OFF";
                    UGUIShip.SetButtonSelected(ch.toggleBtn, ch.on, SEL_COLOR);
                    UpdateBannerPreviewColours();
                }));

            var swatchGo = new GameObject(title + "_Swatch");
            swatchGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            ch.swatch = swatchGo.AddComponent<Image>();
            ch.swatch.color = new Color(ch.r, ch.g, ch.b);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => ch.r, () => ch.g, () => ch.b,
                v => ch.r = v, v => ch.g = v, v => ch.b = v, () => SyncBannerColour(ch), out _, out _, out _,
                new Color(slot.dr, slot.dg, slot.db));

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;
        }

        private void SyncBannerColour(BannerColourUI ch)
        {
            if (ch.swatch != null) ch.swatch.color = new Color(ch.r, ch.g, ch.b);
            if (ch.areaBg != null) ch.areaBg.color = new Color(ch.r, ch.g, ch.b, 0.18f);
            UpdateBannerPreviewColours();
        }

        private void LoadBannerSettings(UIForegroundKind what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float d) =>
                float.TryParse(SettingsService.Get(key, d.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : d;

            void Load(BannerSlotUI s)
            {
                s.ui.on = SettingsService.Get(s.keyPrefix + ".on", "false") == "true";
                s.ui.r = P(s.keyPrefix + ".r", s.dr);
                s.ui.g = P(s.keyPrefix + ".g", s.dg);
                s.ui.b = P(s.keyPrefix + ".b", s.db);
            }

            def.enabled = SettingsService.Get(def.enabledKey, "false") == "true";
            foreach (var s in def.slots) Load(s);
            Load(def.highlight);
        }

        private void OnBannerApply(UIForegroundKind what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void W(BannerSlotUI s)
            {
                SettingsService.Set(s.keyPrefix + ".on", s.ui.on ? "true" : "false");
                SettingsService.Set(s.keyPrefix + ".r", s.ui.r.ToString(ci));
                SettingsService.Set(s.keyPrefix + ".g", s.ui.g.ToString(ci));
                SettingsService.Set(s.keyPrefix + ".b", s.ui.b.ToString(ci));
            }

            foreach (var s in def.slots) W(s);
            W(def.highlight);
        }

        // ── Live preview clone ────────────────────────────────────────────────

        private void RefreshBannerPreview()
        {
            if (_bannerPreviewGo != null) { GameObject.Destroy(_bannerPreviewGo); _bannerPreviewGo = null; }
            _previewImgCache.Clear();
            _previewTmpCache.Clear();

            Transform viewport = GetBannerDef(What)?.viewport;
            if (viewport == null) return;

            GameObject source = FindBannerSource(What);
            if (source == null) return;

            _bannerPreviewGo = GameObject.Instantiate(source);
            _bannerPreviewGo.name = "BannerPreview";

            StartCoroutine(DisableBannerAnimatorsDelayed(_bannerPreviewGo).WrapToIl2Cpp());

            foreach (var t in _bannerPreviewGo.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == "Layout") t.gameObject.SetActive(false);

            _bannerPreviewGo.transform.SetParent(viewport, false);
            _bannerPreviewGo.transform.localPosition = new Vector3(205.4f, -44.6501f, 0f);
            _bannerPreviewGo.transform.localScale = new Vector3(0.8236f, 0.8236f, 0.6f);
            _bannerPreviewGo.SetActive(true);

            foreach (var g in _bannerPreviewGo.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (g != null) g.raycastTarget = false;

            if (What == UIForegroundKind.Winner)
            {
                foreach (var t in _bannerPreviewGo.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t.parent == null || t.parent.name != "Container") continue;
                    if (t.name == "background-starburst-top" || t.name == "UIParticleStars")
                        t.gameObject.SetActive(false);
                }
            }
            else if (What == UIForegroundKind.RoundOver)
            {
                foreach (var t in _bannerPreviewGo.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == "text-ROUND")
                    {
                        t.localPosition = new Vector3(-5f, 0.3327f, 0f);
                        t.localScale = new Vector3(0.2f, 0.2f, 0.2f);
                        break;
                    }
            }
            else if (What == UIForegroundKind.EliminatedSquad)
            {
                ApplySquadPreviewLayout(_bannerPreviewGo);
            }

            foreach (var img in _bannerPreviewGo.GetComponentsInChildren<Image>(true))
            {
                if (img != null)
                {
                    bool hl = Customization.Menu.MenuCustomizationApplication.BannerColours.IsHighlight(img);
                    _previewImgCache.Add(new CachedImgColor { img = img, orig = img.color, isHighlight = hl });
                }
            }

            foreach (var binding in _bannerPreviewGo.GetComponentsInChildren<Mediatonic.Tools.MVVM.TMPTextBinding>(true))
                if (binding != null) GameObject.Destroy(binding);

            StartCoroutine(SetBannerTextNextFrame().WrapToIl2Cpp());
        }

        private System.Collections.IEnumerator DisableBannerAnimatorsDelayed(GameObject go)
        {
            yield return new WaitForSeconds(1.7f);
            if (go == null) yield break;
            foreach (var anim in go.GetComponentsInChildren<Animator>(true))
                if (anim != null) anim.enabled = false;
        }

        private void ApplySquadPreviewLayout(GameObject go)
        {
            if (go == null) return;
            foreach (var anim in go.GetComponentsInChildren<Animator>(true))
                if (anim != null) anim.enabled = false;

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (t.parent != null && t.parent.name == "Container" && t.name == "Badge")
                    t.gameObject.SetActive(false);
                else if (t.name == "text-title" || t.name == "text-subtitle")
                {
                    var fitter = t.GetComponent<ContentSizeFitter>();
                    if (fitter != null) GameObject.Destroy(fitter);
                    var le = t.GetComponent<LayoutElement>();
                    if (le != null) GameObject.Destroy(le);

                    if (t.name == "text-title")
                    {
                        t.localPosition = new Vector3(-301.564f, -10.9704f, 0f);
                        t.localScale = new Vector3(3f, 3f, 3f);
                        var rt = t as RectTransform;
                        if (rt != null) rt.sizeDelta = new Vector2(320f, -194.8501f);
                    }
                    else
                    {
                        t.localScale = new Vector3(3f, 3f, 3f);
                        t.localPosition = new Vector3(63.6912f, -50.9455f, 0f);
                        var rt = t as RectTransform;
                        if (rt != null) rt.sizeDelta = new Vector2(520f, 0f);
                    }
                }
            }
        }

        private System.Collections.IEnumerator SetBannerTextNextFrame()
        {
            yield return null;
            if (_bannerPreviewGo == null) yield break;

            foreach (var tmp in _bannerPreviewGo.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (tmp == null) continue;
                if (tmp.gameObject.name.StartsWith("text-"))
                    tmp.SetText("BEAUTY");

                tmp.ForceMeshUpdate();
                tmp.enabled = false;
                var entry = new CachedTmpColor { tmp = tmp, origFill = tmp.color };
                if (tmp.fontSharedMaterial != null)
                {
                    var mat = tmp.fontMaterial;
                    entry.hasOutline = mat.HasProperty(TMPro.ShaderUtilities.ID_OutlineColor);
                    if (entry.hasOutline) entry.origOutline = mat.GetColor(TMPro.ShaderUtilities.ID_OutlineColor);
                    entry.hasUnderlay = mat.HasProperty(TMPro.ShaderUtilities.ID_UnderlayColor);
                    if (entry.hasUnderlay) entry.origUnderlay = mat.GetColor(TMPro.ShaderUtilities.ID_UnderlayColor);
                }
                _previewTmpCache.Add(entry);
            }

            yield return null;

            for (int i = 0; i < _previewTmpCache.Count; i++)
            {
                var c = _previewTmpCache[i];
                if (c.tmp != null) c.tmp.enabled = true;
            }
            UpdateBannerPreviewColours();
        }

        private void UpdateBannerPreviewColours()
        {
            if (_bannerPreviewGo == null) return;
            var set = PreviewBannerColours(What);

            UnityEngine.UI.Image winnerRoundOverWhiteImg = null;
            bool winnerOverrideOn = false;
            Color winnerOverrideColor = Color.white;
            if (What == UIForegroundKind.Winner)
            {
                var def = GetBannerDef(UIForegroundKind.Winner);
                if (def != null && def.enabled)
                {
                    var yellow = def.slots[0];
                    if (yellow.ui.on)
                    {
                        winnerOverrideOn = true;
                        winnerOverrideColor = new Color(yellow.ui.r, yellow.ui.g, yellow.ui.b);
                    }
                }
                foreach (var t in _bannerPreviewGo.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t.gameObject.name != "round-over-white") continue;
                    winnerRoundOverWhiteImg = t.GetComponent<UnityEngine.UI.Image>();
                    if (winnerRoundOverWhiteImg != null) break;
                }
            }

            for (int i = 0; i < _previewImgCache.Count; i++)
            {
                var c = _previewImgCache[i];
                if (c.img == null) continue;
                if (winnerRoundOverWhiteImg != null && c.img == winnerRoundOverWhiteImg)
                {
                    c.img.color = winnerOverrideOn
                        ? new Color(winnerOverrideColor.r, winnerOverrideColor.g, winnerOverrideColor.b, c.orig.a)
                        : c.orig;
                    continue;
                }
                if (c.isHighlight && set.highlightOn)
                    c.img.color = new Color(set.highlight.r, set.highlight.g, set.highlight.b, c.orig.a);
                else if (set.TryMatch(c.orig, out var t))
                    c.img.color = new Color(t.r, t.g, t.b, c.orig.a);
                else
                    c.img.color = c.orig;
            }

            for (int i = 0; i < _previewTmpCache.Count; i++)
            {
                var c = _previewTmpCache[i];
                if (c.tmp == null) continue;
                c.tmp.color = set.TryMatch(c.origFill, out var tFill)
                    ? new Color(tFill.r, tFill.g, tFill.b, c.origFill.a) : c.origFill;

                if (c.tmp.fontSharedMaterial == null) continue;
                var mat = c.tmp.fontMaterial;
                if (c.hasOutline)
                    mat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor,
                        set.TryMatch(c.origOutline, out var tOut)
                            ? new Color(tOut.r, tOut.g, tOut.b, c.origOutline.a) : c.origOutline);
                if (c.hasUnderlay)
                    mat.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor,
                        set.TryMatch(c.origUnderlay, out var tUn)
                            ? new Color(tUn.r, tUn.g, tUn.b, c.origUnderlay.a) : c.origUnderlay);
            }
        }

        private Customization.Menu.MenuCustomizationApplication.BannerColours PreviewBannerColours(UIForegroundKind what)
        {
            var def = GetBannerDef(what);
            var slots = new System.Collections.Generic.List<Customization.Menu.MenuCustomizationApplication.BannerSlot>();
            if (def == null || !def.enabled)
                return new Customization.Menu.MenuCustomizationApplication.BannerColours { slots = slots, highlightOn = false, highlight = Color.white };

            foreach (var s in def.slots)
                if (s.ui.on)
                    slots.Add(new Customization.Menu.MenuCustomizationApplication.BannerSlot
                    { bucket = s.bucket, target = new Color(s.ui.r, s.ui.g, s.ui.b) });

            var hl = def.highlight.ui;
            return new Customization.Menu.MenuCustomizationApplication.BannerColours
            {
                slots = slots,
                highlightOn = hl != null && hl.on,
                highlight = hl != null ? new Color(hl.r, hl.g, hl.b) : Color.white,
            };
        }

        private static bool IsBannerPreviewClone(UnityEngine.Object obj)
        {
            if (obj == null) return false;
            var t = (obj as Component)?.transform;
            while (t != null)
            {
                if (t.gameObject.name == "BannerPreview") return true;
                t = t.parent;
            }
            return false;
        }

        private GameObject FindBannerSource(UIForegroundKind what)
        {
            switch (what)
            {
                case UIForegroundKind.Qualified:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.UI.QualifiedScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
                case UIForegroundKind.Eliminated:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.EliminatedScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
                case UIForegroundKind.EliminatedSquad:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.EliminatedSquadScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
                case UIForegroundKind.Winner:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.UI.WinnerScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
                case UIForegroundKind.RoundOver:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.RoundEndedScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
            }
            return null;
        }
    }
}
