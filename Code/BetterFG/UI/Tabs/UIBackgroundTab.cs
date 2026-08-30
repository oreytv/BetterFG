using System;
using System.Reflection;
using BetterFG.Customization.Menu;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class UIBackgroundTab : UISubTab
    {
        public UIBackgroundTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "UI - Background";
        protected override string TitleId => "ui.ui_background";

        // set by UIPatternPickerTab's back link so returning from a pattern pick lands back on
        // whichever screen you were customising, instead of resetting to FallForce.
        public ScreenBackgroundService.Screen? InitialScreen { get; set; }
        public int? InitialSubtab { get; set; }

        protected override void BuildContent(RectTransform contentRoot)
        {
            if (InitialScreen.HasValue) _screenSel = InitialScreen.Value;
            if (InitialSubtab.HasValue) _screenSubtab = InitialSubtab.Value;
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            var panelGo = new GameObject("BackgroundPanel");
            panelGo.transform.SetParent(contentRoot, false);
            var panelRt = panelGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(panelRt, new Rect(0f, y, TabWidth, TabHeight - y));

            float btnRowH = BTN_H + PAD * 2f + 1f;
            BuildScreenPanel(panelRt, PAD, 0f, w, TabHeight - y - btnRowH, btnRowH);

            PositionSwitchLink();
        }

        private ScreenBackgroundService.Screen _screenSel = ScreenBackgroundService.Screen.FallForce;
        // the falling screen (lobby bg) isn't a ScreenBackgroundService.Screen — it recolours the
        // named blue-slot images in Menu_Screen_Lobby, not a gradient backdrop. it's a fifth dropdown
        // entry with its own body. this flag says the dropdown currently has it selected.
        private bool _fallingSel;
        private float _scTopR, _scTopG, _scTopB;
        private float _scBotR = 1f, _scBotG = 1f, _scBotB = 1f;
        private float _scBias, _scSmooth = 1f;
        private bool _scEnabled;
        // fixed carousel above the scroll view — Gradient (top/bottom colour, shape) vs Pattern
        // (browse + tint). ShowSelector has no pattern feature so it's forced to Gradient.
        private static readonly string[] ScreenSubtabs = { "Gradient", "Pattern" };
        private int _screenSubtab;
        private string _scPattern = "";
        private float _scPatternR = 1f, _scPatternG = 1f, _scPatternB = 1f, _scPatternA = 1f;
        private Image _scPatternSwatch;
        private Image _scTopSwatch, _scBotSwatch;

        // ── live clone preview (Backdrop + Circles) ────────────────────────────
        private const float SCREEN_PREVIEW_H = 100f;
        // gap between the preview frame and the scroll frame below it
        private const float PREVIEW_GAP = 6f;

        // rounded, masked frame for a live clone preview. sized to (w, SCREEN_PREVIEW_H) at (x, y);
        // returns the inner slot to instantiate the clone into.
        private static Transform BuildPreviewFrame(Transform parent, float x, float y, float w)
        {
            var frameGo = new GameObject("PreviewFrame");
            frameGo.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(frameGo.AddComponent<RectTransform>(), new Rect(x, y, w, SCREEN_PREVIEW_H));
            var (_, slot) = UGUIShip.CreateFramedImage(frameGo.transform);
            slot.gameObject.AddComponent<RectMask2D>();
            return slot;
        }

        // scroll view wrapped in its own slightly-inner outline frame, sitting below the preview.
        // returns the same (scrollRect, content) pair CreateScrollView gives.
        private (ScrollRect scrollRect, RectTransform content) BuildFramedScroll(RectTransform parent, float x, float y, float w, float h)
        {
            var frameGo = new GameObject("ScrollFrame");
            frameGo.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(frameGo.AddComponent<RectTransform>(), new Rect(x, y, w, h));
            var outGo = new GameObject("Outline");
            outGo.transform.SetParent(frameGo.transform, false);
            var oRt = outGo.AddComponent<RectTransform>();
            oRt.anchorMin = Vector2.zero; oRt.anchorMax = Vector2.one; oRt.offsetMin = oRt.offsetMax = Vector2.zero;
            var oImg = outGo.AddComponent<Image>();
            UGUIShip.ApplyDeluxPanelOutline(oImg);
            oImg.raycastTarget = false;
            return UGUIShip.CreateScrollView(frameGo.transform, new Rect(3f, 3f, w - 6f, h - 6f));
        }
        private GameObject _scPreviewGo;
        private Image _scPreviewBackdrop;
        private Image _scPreviewCircles;
        private Sprite _scPreviewDefaultBackdrop;
        private Color _scPreviewDefaultBackdropColor;
        private Texture _scPreviewDefaultPattern;
        private Color _scPreviewDefaultCirclesColor = Color.white;
        private Texture2D _scPreviewGradTex;
        private Texture2D _scPreviewPatternTex;
        private string _scPreviewPatternPath;
        private Sprite _scPreviewDefaultPatternSprite;
        private Image.Type _scPreviewDefaultPatternType;
        private Text _scPreviewHintLabel;
        private Material _scPreviewCirclesMat;
        private Text _scPatternLabel;
        private GameObject _screenBodyGo;
        private RectTransform _screenBodyParent;
        private float _screenBodyW, _screenBodyH;

        // ── Creative (level browser) slot colours ─────────────────────────────
        private bool _creativeSel;
        private bool _crEnabled;
        // r/g/b per slot, indexed by CreativeSlot (Backdrop, Glows, Drawings, Vignette)
        private readonly float[] _crR = new float[4], _crG = new float[4], _crB = new float[4];
        private readonly Image[] _crSwatch = new Image[4];
        private Button _crEnabledBtn;

        // ── Creative live clone preview ─────────────────────────────────────────
        private GameObject _crPreviewGo;
        private readonly System.Collections.Generic.List<(Graphic g, MenuCustomizationApplication.CreativeSlot slot)> _crPreviewGraphics =
            new System.Collections.Generic.List<(Graphic, MenuCustomizationApplication.CreativeSlot)>();
        private readonly System.Collections.Generic.List<Color> _crPreviewDefaults = new System.Collections.Generic.List<Color>();

        // ── Falling screen (lobby bg) slot colours ────────────────────────────
        private bool _lbEnabled;
        private float _lbSlot0R, _lbSlot0G, _lbSlot0B;
        private float _lbSlot1R, _lbSlot1G, _lbSlot1B;
        private float _lbSlot2R, _lbSlot2G, _lbSlot2B;
        private Image _lbSwatch0, _lbSwatch1, _lbSwatch2;
        private Button _lbEnabledBtn;

        private GameObject _lbPreviewGo;
        private readonly System.Collections.Generic.List<(Image img, int slot)> _lbPreviewSlots =
            new System.Collections.Generic.List<(Image, int)>();
        private readonly System.Collections.Generic.List<Color> _lbPreviewDefaults = new System.Collections.Generic.List<Color>();

        // screen overlay images (fallforce shared by fallforce + loading level)
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _screenSprites =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        private static Sprite ScreenSprite(ScreenBackgroundService.Screen s)
        {
            string file;
            switch (s)
            {
                case ScreenBackgroundService.Screen.FinalRound: file = "final"; break;
                case ScreenBackgroundService.Screen.Explore: file = "explore"; break;
                default: file = "fallforce"; break; // FallForce + LoadingLevel
            }
            return ScreenSpriteByName(file);
        }

        // load an assets/ui/uiscreen/<file>.png sprite by stem — the falling screen isn't a
        // ScreenBackgroundService.Screen so it grabs its overlay through here directly.
        private static Sprite ScreenSpriteByName(string file)
        {
            if (_screenSprites.TryGetValue(file, out var cached) && cached != null) return cached;
            Sprite sp = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.uiscreen." + file + ".png");
                if (stream != null)
                {
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.LoadImage(bytes);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch (Exception ex) { Plugin.Log.LogError("UITab: screen sprite load failed: " + ex.Message); }
            _screenSprites[file] = sp;
            return sp;
        }

        private void BuildScreenPanel(RectTransform parent, float x, float y, float w, float bodyH, float btnRowH)
        {
            LoadScreenSettings(_screenSel);

            float cy = PAD;

            // screen selector dropdown — each option has its screen image as an overlay, text right-aligned
            var screens = new System.Collections.Generic.List<ScreenBackgroundService.Screen>
            {
                ScreenBackgroundService.Screen.FallForce,
                ScreenBackgroundService.Screen.LoadingLevel,
                ScreenBackgroundService.Screen.FinalRound,
                ScreenBackgroundService.Screen.Explore,
                ScreenBackgroundService.Screen.ShowSelector,
            };
            var opts = new System.Collections.Generic.List<string>();
            var initial = new System.Collections.Generic.List<bool>();
            var sprites = new System.Collections.Generic.List<Sprite>();
            bool special = _fallingSel || _creativeSel;
            foreach (var s in screens) { opts.Add(ScreenBackgroundService.Label(s)); initial.Add(!special && s == _screenSel); sprites.Add(ScreenSprite(s)); }
            // special entries — not gradient screens, each has its own recolour body
            opts.Add("Falling Screen"); initial.Add(_fallingSel); sprites.Add(ScreenSpriteByName("fallingscreen"));
            int fallingIdx = screens.Count;
            opts.Add("Creative Background"); initial.Add(_creativeSel); sprites.Add(ScreenSpriteByName("creative"));
            int creativeIdx = fallingIdx + 1;

            string HeaderLabel() => _creativeSel ? "Creative Background" : _fallingSel ? "Falling Screen" : ScreenBackgroundService.Label(_screenSel);
            Sprite HeaderSprite() => _creativeSel ? ScreenSpriteByName("creative") : _fallingSel ? ScreenSpriteByName("fallingscreen") : ScreenSprite(_screenSel);

            Button screenDd = null;
            screenDd = UGUIShip.CreateMultiSelectDropdown(parent, new Rect(x, cy, w, BTN_H),
                HeaderLabel(), opts, initial,
                new Action<int, bool>((idx, _) =>
                {
                    if (idx < 0 || idx > creativeIdx) return;
                    _fallingSel = idx == fallingIdx;
                    _creativeSel = idx == creativeIdx;
                    if (!_fallingSel && !_creativeSel) _screenSel = screens[idx];
                    var lbl = screenDd?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, HeaderLabel());
                    var headImg = screenDd?.transform.Find("HeaderImg")?.GetComponent<Image>();
                    if (headImg != null) headImg.sprite = HeaderSprite();
                    RebuildScreenBody();
                }), FS_SM, w, dropdownRowH, true, true, false, sprites, true);
            cy += BTN_H + SH;

            UGUIShip.CreatePanel(parent, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + SH;

            // body host — rebuilt whenever the selected screen changes
            _screenBodyParent = parent;
            _screenBodyW = w;
            _screenBodyH = bodyH - cy - PAD;
            float bodyY = cy;

            _screenBodyGo = new GameObject("ScreenBody");
            _screenBodyGo.transform.SetParent(parent, false);
            var bodyRt = _screenBodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(bodyRt, new Rect(0f, bodyY, TabWidth, _screenBodyH));
            if (_creativeSel) { LoadCreativeSettings(); BuildCreativeBody(bodyRt, x, 0f, w, _screenBodyH); }
            else if (_fallingSel) { LoadFallingSettings(); BuildFallingBody(bodyRt, x, 0f, w, _screenBodyH); }
            else BuildScreenBody(bodyRt, x, 0f, w, _screenBodyH);

            float by = y + bodyH + PAD;
            UGUIShip.CreatePanel(parent, new Rect(PAD, by, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            by += 1f + PAD;
            float btnw = (w - PAD * 1.5f) / 4f;
            UGUIShip.CreateButton(parent, new Rect(PAD, by, btnw, BTN_H),
                "ui.apply", BTN_APPLY, WHITE, FS_SM, new Action(() => { if (_creativeSel) OnCreativeApply(); else if (_fallingSel) OnFallingApply(); else OnScreenApply(); }));
            UGUIShip.CreateButton(parent, new Rect(PAD + (btnw + PAD * 0.5f), by, btnw, BTN_H),
                "ui.remove_2", BTN_REMOVE, WHITE, FS_SM, new Action(() => { if (_creativeSel) OnCreativeRemove(); else if (_fallingSel) OnFallingRemove(); else OnScreenRemove(); }));
            UGUIShip.CreateButton(parent, new Rect(PAD + (btnw + PAD * 0.5f) * 2f, by, btnw, BTN_H),
                "ui.enable_all", BTN_ON, WHITE, FS_SM, new Action(() => SetScreenEnabled(true)));
            UGUIShip.CreateButton(parent, new Rect(PAD + (btnw + PAD * 0.5f) * 3f, by, btnw, BTN_H),
                "ui.disable_all", BTN_REMOVE, WHITE, FS_SM, new Action(() => SetScreenEnabled(false)));
        }

        private void SetScreenEnabled(bool on)
        {
            if (_creativeSel)
            {
                _crEnabled = on;
                SettingsService.Set(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, on ? "true" : "false");
                ApplyCreativeLive();
            }
            else if (_fallingSel)
            {
                _lbEnabled = on;
                SettingsService.Set(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, on ? "true" : "false");
                ApplyFallingLive();
            }
            else
            {
                _scEnabled = on;
                SettingsService.Set(ScreenBackgroundService.KeyEnabled(_screenSel), on ? "true" : "false");
                if (_screenSel == ScreenBackgroundService.Screen.FallForce)
                {
                    SettingsService.Set(MenuCustomizationApplication.KEY_BG_ENABLED, on ? "true" : "false");
                    MenuCustomizationApplication.Instance?.SetMenuBgEnabled(on);
                }
                ApplyScreenLive();
            }
            RebuildScreenBody();
        }

        private void RebuildScreenBody()
        {
            if (_screenBodyGo == null) return;
            if (_creativeSel) LoadCreativeSettings();
            else if (_fallingSel) LoadFallingSettings();
            else LoadScreenSettings(_screenSel);
            for (int i = _screenBodyGo.transform.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_screenBodyGo.transform.GetChild(i).gameObject);
            var rt = _screenBodyGo.GetComponent<RectTransform>();
            if (_creativeSel) BuildCreativeBody(rt, PAD, 0f, _screenBodyW, _screenBodyH);
            else if (_fallingSel) BuildFallingBody(rt, PAD, 0f, _screenBodyW, _screenBodyH);
            else BuildScreenBody(rt, PAD, 0f, _screenBodyW, _screenBodyH);
        }

        private void BuildScreenSubtabCarousel(RectTransform parent, float x, float y, float w)
        {
            bool showSelector = _screenSel == ScreenBackgroundService.Screen.ShowSelector;
            var labels = (string[])ScreenSubtabs.Clone();
            if (showSelector) labels[1] = "Circles";
            UGUIShip.CreateCarousel(parent, new Rect(x, y, w, BTN_H), labels, _screenSubtab,
                d => { _screenSubtab = (_screenSubtab + d + labels.Length) % labels.Length; RebuildScreenBody(); },
                BTN_DARK, FS_SM);
        }

        private void BuildScreenBody(RectTransform parent, float x, float y, float w, float h)
        {
            float headerH = BTN_H + SH;
            BuildScreenSubtabCarousel(parent, x, y, w);
            UGUIShip.CreatePanel(parent, new Rect(x, y + headerH - SH, w, 1f), new Color(1f, 1f, 1f, 0.06f));

            BuildScreenPreview(BuildPreviewFrame(parent, x, y + headerH, w), x, w);

            float scrollY = y + headerH + SCREEN_PREVIEW_H + PREVIEW_GAP;
            var (scrollRect, content) = BuildFramedScroll(parent, x, scrollY, w, h - headerH - SCREEN_PREVIEW_H - PREVIEW_GAP);
            w -= UGUIShip.SCROLLBAR_INSET * 2f;

            float cy = PAD;

            if (_screenSubtab == 0)
            {
                // top color
                UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.top_color", FS_SM, HINT);
                cy += LH + SH;
                float topSwatchW = BTN_H, topSliderW = w - topSwatchW - PAD;
                var topSwGo = new GameObject("TopSwatch");
                topSwGo.transform.SetParent(content, false);
                UGUIShip.SetPixelRect(topSwGo.AddComponent<RectTransform>(), new Rect(x + topSliderW + PAD, cy, topSwatchW, (LH + SH) * 3f - SH));
                _scTopSwatch = topSwGo.AddComponent<Image>();
                _scTopSwatch.color = new Color(_scTopR, _scTopG, _scTopB);
                UGUIShip.CreateColorControls(content, x, ref cy, topSliderW,
                    () => _scTopR, () => _scTopG, () => _scTopB,
                    v => _scTopR = v, v => _scTopG = v, v => _scTopB = v,
                    () => { if (_scTopSwatch != null) _scTopSwatch.color = new Color(_scTopR, _scTopG, _scTopB); RefreshScreenPreview(); },
                    out _, out _, out _, Color.black);

                // bottom color
                UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.bottom_color", FS_SM, HINT);
                cy += LH + SH;
                float botSwatchW = BTN_H, botSliderW = w - botSwatchW - PAD;
                var botSwGo = new GameObject("BotSwatch");
                botSwGo.transform.SetParent(content, false);
                UGUIShip.SetPixelRect(botSwGo.AddComponent<RectTransform>(), new Rect(x + botSliderW + PAD, cy, botSwatchW, (LH + SH) * 3f - SH));
                _scBotSwatch = botSwGo.AddComponent<Image>();
                _scBotSwatch.color = new Color(_scBotR, _scBotG, _scBotB);
                UGUIShip.CreateColorControls(content, x, ref cy, botSliderW,
                    () => _scBotR, () => _scBotG, () => _scBotB,
                    v => _scBotR = v, v => _scBotG = v, v => _scBotB = v,
                    () => { if (_scBotSwatch != null) _scBotSwatch.color = new Color(_scBotR, _scBotG, _scBotB); RefreshScreenPreview(); },
                    out _, out _, out _, Color.white);

                UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
                cy += 1f + PAD;

                // shader (texture bake) params
                UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.gradient_shape", FS_SM, HINT);
                cy += LH + SH;
                BuildSliderRaw(content, x, cy, w, "Bias", _scBias, -1f, 1f, v => { _scBias = v; RefreshScreenPreview(); }, 0f);
                cy += LH + SH;
                BuildSliderRaw(content, x, cy, w, "Smooth", _scSmooth, 0.1f, 8f, v => { _scSmooth = v; RefreshScreenPreview(); }, 1f);
                cy += LH + PAD;
            }
            else
            {
                bool hasPattern = _screenSel != ScreenBackgroundService.Screen.ShowSelector;
                if (hasPattern)
                {
                    UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.pattern", FS_SM, HINT);
                    cy += LH + SH;
                    float patBtnW = BTN_H * 2.5f;
                    float patLblW = w - patBtnW - PAD;
                    _scPatternLabel = UGUIShip.CreateLabel(content, new Rect(x, cy, patLblW, BTN_H),
                        ScreenBackgroundService.PatternDisplayName(_scPattern),
                        FS_SM, HINT, TextAnchor.MiddleLeft);
                    UGUIShip.CreateButton(content, new Rect(x + patLblW + PAD, cy, patBtnW, BTN_H),
                        "ui.browse", BTN_DARK, WHITE, FS_SM, new Action(() =>
                        {
                            var t = BetterFGTabRegistry.NewTab<UIPatternPickerTab>();
                            t.Screen = _screenSel;
                            BetterFGUIMan.Instance?.SwitchSlotTab(this, t);
                        }));
                    cy += BTN_H + PAD;
                }

                UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), hasPattern ? "ui.pattern_color" : "ui.circles_color", FS_SM, HINT);
                cy += LH + SH;
                float patSwatchW = BTN_H, patSliderW = w - patSwatchW - PAD;
                var swGo = new GameObject("PatternSwatch");
                swGo.transform.SetParent(content, false);
                UGUIShip.SetPixelRect(swGo.AddComponent<RectTransform>(), new Rect(x + patSliderW + PAD, cy, patSwatchW, (LH + SH) * 4f - SH));
                _scPatternSwatch = swGo.AddComponent<Image>();
                _scPatternSwatch.color = new Color(_scPatternR, _scPatternG, _scPatternB, _scPatternA);
                void SyncPatternSwatch()
                {
                    if (_scPatternSwatch != null) _scPatternSwatch.color = new Color(_scPatternR, _scPatternG, _scPatternB, _scPatternA);
                    RefreshScreenPreview();
                }
                UGUIShip.CreateColorControls(content, x, ref cy, patSliderW,
                    () => _scPatternR, () => _scPatternG, () => _scPatternB,
                    v => _scPatternR = v, v => _scPatternG = v, v => _scPatternB = v, SyncPatternSwatch, out _, out _, out _,
                    Color.white);
                BuildSliderRaw(content, x, cy, patSliderW, "Alpha", _scPatternA, 0f, 1f, v => { _scPatternA = v; SyncPatternSwatch(); }, 1f);
                cy += LH + PAD;
            }

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private Transform ScreenPreviewSource(ScreenBackgroundService.Screen s, out bool ownGeometry)
        {
            Transform t;
            switch (s)
            {
                case ScreenBackgroundService.Screen.LoadingLevel:
                case ScreenBackgroundService.Screen.Explore:
                case ScreenBackgroundService.Screen.FinalRound:
                    t = ScreenBackgroundService.FindPreviewSource(s);
                    break;
                case ScreenBackgroundService.Screen.ShowSelector:
                    t = BetterFG.Patches.ShowSelectorBg.FindLiveMask();
                    break;
                default:
                    t = ScreenBackgroundService.FindPreviewSource(ScreenBackgroundService.Screen.LoadingLevel);
                    break;
            }
            ownGeometry = t != null && s != ScreenBackgroundService.Screen.FallForce;
            return t;
        }

        // clones the live Backdrop+Circles container once so the preview shows the real shader/pattern,
        // not an approximation. lives in its own framed slot above the scroll area.
        private void BuildScreenPreview(Transform previewSlot, float x, float w)
        {
            if (_scPreviewGo != null) GameObject.Destroy(_scPreviewGo);
            _scPreviewGo = null; _scPreviewBackdrop = null; _scPreviewCircles = null; _scPreviewDefaultPattern = null;
            if (previewSlot == null) return;

            var source = ScreenPreviewSource(_screenSel, out bool ownGeometry);
            if (source == null) return; // not in the main menu right now, nothing live to clone

            // this screen's OWN container, found live — learn its real default right now, no need to
            // have actually lived through it this session (LoadingLevel/Explore/ShowSelector are all
            // pre-instantiated and inactive off the menu scene, same as ShowSelectorBg already relied on).
            if (_screenSel != ScreenBackgroundService.Screen.FallForce && ownGeometry)
                ScreenBackgroundService.CacheScreenDefault(_screenSel, source);

            var holderRt = (RectTransform)previewSlot;
            _scPreviewGo = GameObject.Instantiate(source.gameObject, holderRt, false);
            _scPreviewGo.name = "ScreenPreviewClone";
            FitCloneToPreview(_scPreviewGo, source, w, SCREEN_PREVIEW_H);

            foreach (var anim in _scPreviewGo.GetComponentsInChildren<Animator>(true))
                if (anim != null) anim.enabled = false;
            // the framed preview slot sits under a real UI Mask now (rounded corners), not just a
            // RectMask2D — Mask makes MaskableGraphic swap in a cached stencil COPY of the material,
            // taken once and never refreshed, so every SetTexture/color we do below in Refresh*Preview
            // kept painting a material nobody was actually rendering. maskable=false skips that copy;
            // the RectMask2D still clips the box, just square-cornered instead of rounded.
            foreach (var g in _scPreviewGo.GetComponentsInChildren<Graphic>(true))
                if (g != null) { g.raycastTarget = false; if (g is MaskableGraphic mg) mg.maskable = false; }

            _scPreviewBackdrop = _scPreviewGo.transform.Find("Backdrop")?.GetComponent<Image>();
            _scPreviewCircles = ScreenBackgroundService.FindCirclesChild(_scPreviewGo.transform)?.GetComponent<Image>();
            _scPreviewDefaultPatternSprite = _scPreviewCircles != null ? _scPreviewCircles.sprite : null;
            _scPreviewDefaultPatternType = _scPreviewCircles != null ? _scPreviewCircles.type : Image.Type.Simple;

            // Image.material does NOT auto-instance like Renderer.material does — Instantiate leaves the
            // clone pointing at the SAME shared Material asset the live menu's real Circles uses. every
            // SetTexture below would otherwise paint the live game's actual background, not just this
            // preview. give the clone its own copy before touching it.
            if (_scPreviewCircles != null && _scPreviewCircles.material != null)
            {
                if (_scPreviewCirclesMat != null) Destroy(_scPreviewCirclesMat);
                _scPreviewCirclesMat = new Material(_scPreviewCircles.material);
                _scPreviewCircles.material = _scPreviewCirclesMat;
            }

            // FallForce's Backdrop, like Circles, IS mutated live by ApplyGradientFromSettings — if a
            // custom gradient is already active this session, reading the clone's sprite straight off
            // would hand us back our OWN texture as the "default". route through the guarded capture
            if (_screenSel == ScreenBackgroundService.Screen.FallForce && _scPreviewBackdrop != null)
            {
                _scPreviewDefaultBackdrop = MenuCustomizationApplication.Instance?.EnsureOriginalBackdropSpriteCaptured() ?? _scPreviewBackdrop.sprite;
                _scPreviewDefaultBackdropColor = Color.white;
            }
            _scPreviewDefaultPattern = MenuCustomizationApplication.Instance?.EnsureOriginalCirclesPatternCaptured();
            if (_screenSel == ScreenBackgroundService.Screen.FallForce)
            {
                _scPreviewDefaultCirclesColor = MenuCustomizationApplication.Instance?.EnsureOriginalCirclesColorCaptured() ?? Color.white;
                if (_scPreviewDefaultPattern == null && _scPreviewCirclesMat != null)
                    _scPreviewDefaultPattern = _scPreviewCirclesMat.GetTexture("_Pattern");
            }
            else if (ScreenBackgroundService.TryGetScreenDefaultCirclesColor(_screenSel, out var defCol))
                _scPreviewDefaultCirclesColor = defCol;
            else if (_scPreviewCircles != null)
                _scPreviewDefaultCirclesColor = _scPreviewCircles.color;

            _scPreviewHintLabel = UGUIShip.CreateLabel(holderRt, new Rect(PAD, 0f, w - PAD * 2f, SCREEN_PREVIEW_H),
                "ui.haven_t_seen_this_screen_s_real_default_yet_this",
                FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshScreenPreview();
        }

        private static void FitCloneToPreview(GameObject clone, Transform source, float w, float h)
        {
            var cloneRt = clone != null ? clone.GetComponent<RectTransform>() : null;
            if (cloneRt == null) return;
            var srcRt = source != null ? source.GetComponent<RectTransform>() : null;
            Vector2 natSize = srcRt != null ? srcRt.sizeDelta : Vector2.zero;
            if (natSize.x <= 1f || natSize.y <= 1f) natSize = new Vector2(1920f, 1080f);
            float fit = Mathf.Min(w / natSize.x, h / natSize.y);
            cloneRt.localRotation = Quaternion.identity;
            cloneRt.anchorMin = cloneRt.anchorMax = new Vector2(0.5f, 0.5f);
            cloneRt.pivot = new Vector2(0.5f, 0.5f);
            cloneRt.sizeDelta = natSize;
            cloneRt.anchoredPosition = Vector2.zero;
            cloneRt.localScale = new Vector3(fit, fit, 1f);
        }

        private void RefreshScreenPreview()
        {
            bool isFallForce = _screenSel == ScreenBackgroundService.Screen.FallForce;

            if (_scPreviewBackdrop != null)
            {
                if (_scEnabled)
                {
                    if (_scPreviewGradTex != null) Destroy(_scPreviewGradTex);
                    _scPreviewGradTex = ScreenBackgroundService.BuildGradientTex(
                        new Color(_scTopR, _scTopG, _scTopB), new Color(_scBotR, _scBotG, _scBotB), _scBias, _scSmooth);
                    _scPreviewBackdrop.sprite = Sprite.Create(_scPreviewGradTex,
                        new Rect(0, 0, _scPreviewGradTex.width, _scPreviewGradTex.height), new Vector2(0.5f, 0.5f));
                    _scPreviewBackdrop.color = Color.white;
                }
                else if (isFallForce)
                {
                    _scPreviewBackdrop.sprite = _scPreviewDefaultBackdrop;
                    _scPreviewBackdrop.color = _scPreviewDefaultBackdropColor;
                }
                else if (ScreenBackgroundService.TryGetScreenDefault(_screenSel, out var cachedSprite, out var cachedColor, out _))
                {
                    // learned this screen's real default the first time it was actually seen live this
                    // session (ScreenBackgroundService caches it whenever the loading screen shows, on
                    // or off) — accurate, not borrowed from FallForce.
                    _scPreviewBackdrop.sprite = cachedSprite;
                    _scPreviewBackdrop.color = cachedColor;
                }
                else
                {
                    // never seen this screen live yet this session — no default to show, say so instead
                    // of guessing. it'll self-correct once you've actually hit that loading screen once.
                    _scPreviewBackdrop.sprite = null;
                    _scPreviewBackdrop.color = new Color(0f, 0f, 0f, 0.35f);
                }
            }

            bool knownDefault = isFallForce || ScreenBackgroundService.TryGetScreenDefault(_screenSel, out _, out _, out _);
            if (_scPreviewHintLabel != null)
                _scPreviewHintLabel.gameObject.SetActive(!_scEnabled && !knownDefault);

            if (_scPreviewCircles != null && _scPreviewCircles.material != null)
            {
                if (_scEnabled && !string.IsNullOrEmpty(_scPattern) && _scPreviewPatternPath != _scPattern)
                {
                    if (_scPreviewPatternTex != null) Destroy(_scPreviewPatternTex);
                    _scPreviewPatternTex = ScreenBackgroundService.LoadPatternTexture(_scPattern);
                    if (_scPreviewPatternTex != null && _scPreviewDefaultPattern != null)
                        _scPreviewPatternTex = ScreenBackgroundService.MatchPatternSize(_scPreviewPatternTex, _scPreviewDefaultPattern);
                    _scPreviewPatternPath = _scPattern;
                }

                // FinalRoundBackground's "Pattern" node has no _Pattern property on its material at all
                // (UI/ScrollingSprite2.0 paints from Image.sprite instead) — same split as ApplyToContainer.
                if (_scPreviewCircles.material.HasProperty("_Pattern"))
                {
                    // circles are a shared seasonal decoration, not per-screen themed art like Backdrop is —
                    // fall back to FallForce's (always known) pattern rather than null when a screen's own
                    // hasn't been cached, so the pattern doesn't just vanish for screens we haven't learned yet.
                    Texture patternTex = _scPreviewDefaultPattern;
                    if (!isFallForce && ScreenBackgroundService.TryGetScreenDefault(_screenSel, out _, out _, out var cachedPattern))
                        patternTex = cachedPattern;
                    if (_scEnabled && !string.IsNullOrEmpty(_scPattern) && _scPreviewPatternTex != null) patternTex = _scPreviewPatternTex;
                    _scPreviewCircles.material.SetTexture("_Pattern", patternTex);
                }
                else if (_scEnabled && !string.IsNullOrEmpty(_scPattern) && _scPreviewPatternTex != null)
                {
                    _scPreviewCircles.sprite = Sprite.Create(_scPreviewPatternTex,
                        new Rect(0, 0, _scPreviewPatternTex.width, _scPreviewPatternTex.height), new Vector2(0.5f, 0.5f));
                    _scPreviewCircles.type = Image.Type.Tiled;
                    _scPreviewCircles.pixelsPerUnitMultiplier = ScreenBackgroundService.FinalRoundPatternTileScale;
                }
                else
                {
                    _scPreviewCircles.sprite = _scPreviewDefaultPatternSprite;
                    _scPreviewCircles.type = _scPreviewDefaultPatternType;
                }
                _scPreviewCircles.color = _scEnabled ? new Color(_scPatternR, _scPatternG, _scPatternB, _scPatternA) : _scPreviewDefaultCirclesColor;
            }
        }

        private void LoadScreenSettings(ScreenBackgroundService.Screen s)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            _scTopR = P(ScreenBackgroundService.KeyTopR(s), 0f);
            _scTopG = P(ScreenBackgroundService.KeyTopG(s), 0f);
            _scTopB = P(ScreenBackgroundService.KeyTopB(s), 0f);
            _scBotR = P(ScreenBackgroundService.KeyBotR(s), 1f);
            _scBotG = P(ScreenBackgroundService.KeyBotG(s), 1f);
            _scBotB = P(ScreenBackgroundService.KeyBotB(s), 1f);
            _scBias = P(ScreenBackgroundService.KeyBias(s), 0f);
            _scSmooth = P(ScreenBackgroundService.KeySmooth(s), 1f);
            _scEnabled = SettingsService.Get(ScreenBackgroundService.KeyEnabled(s), "false") == "true";
            _scPattern = SettingsService.Get(ScreenBackgroundService.KeyPattern(s), "");
            _scPatternR = P(ScreenBackgroundService.KeyPatternR(s), 1f);
            _scPatternG = P(ScreenBackgroundService.KeyPatternG(s), 1f);
            _scPatternB = P(ScreenBackgroundService.KeyPatternB(s), 1f);
            _scPatternA = P(ScreenBackgroundService.KeyPatternA(s), 1f);
        }

        private void OnScreenApply()
        {
            var s = _screenSel;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void S(string k, float v) => SettingsService.Set(k, v.ToString(ci));
            S(ScreenBackgroundService.KeyTopR(s), _scTopR);
            S(ScreenBackgroundService.KeyTopG(s), _scTopG);
            S(ScreenBackgroundService.KeyTopB(s), _scTopB);
            S(ScreenBackgroundService.KeyBotR(s), _scBotR);
            S(ScreenBackgroundService.KeyBotG(s), _scBotG);
            S(ScreenBackgroundService.KeyBotB(s), _scBotB);
            S(ScreenBackgroundService.KeyBias(s), _scBias);
            S(ScreenBackgroundService.KeySmooth(s), _scSmooth);
            S(ScreenBackgroundService.KeyPatternR(s), _scPatternR);
            S(ScreenBackgroundService.KeyPatternG(s), _scPatternG);
            S(ScreenBackgroundService.KeyPatternB(s), _scPatternB);
            S(ScreenBackgroundService.KeyPatternA(s), _scPatternA);
            SettingsService.Set(ScreenBackgroundService.KeyEnabled(s), _scEnabled ? "true" : "false");

            ApplyScreenLive();
        }

        // push the selected screen's current state to whatever is showing right now (live preview).
        // FallForce = the menu/title; loading screens = the active loading screen if one is up.
        private void ApplyScreenLive()
        {
            var s = _screenSel;
            if (s == ScreenBackgroundService.Screen.FallForce)
            {
                // bg.enabled is independent from custom colours — show/hide the BG GO from that.
                // _scEnabled (colour customisation) only decides whether to push our gradient onto
                // the BG mat or revert to defaults.
                bool bgOn = SettingsService.Get(MenuCustomizationApplication.KEY_BG_ENABLED, "false") == "true";
                MenuCustomizationApplication.Instance?.SetMenuBgEnabled(bgOn);
                if (_scEnabled)
                {
                    MenuCustomizationApplication.Instance?.ApplyGradient(
                        new Color(_scTopR, _scTopG, _scTopB), new Color(_scBotR, _scBotG, _scBotB), _scBias, _scSmooth);
                    MenuCustomizationApplication.Instance?.ApplyPatternFromSettings();
                }
                else
                {
                    // revert menu gradient + pattern to default
                    MenuCustomizationApplication.Instance?.RestoreBackdrop();
                    MenuCustomizationApplication.Instance?.RestorePattern();
                }
            }
            else if (s == ScreenBackgroundService.Screen.ShowSelector)
            {
                // its own live path — the selector isn't a loading screen
                BetterFG.Patches.ShowSelectorBg.ReapplyLive();
            }
            else
            {
                // ReapplyActive runs ApplyUnder, which applies when enabled and reverts when not
                BetterFG.Patches.LoadingScreenBg.ReapplyActive();
            }
        }

        private void OnScreenRemove()
        {
            var s = _screenSel;
            foreach (var k in new[]
            {
                ScreenBackgroundService.KeyTopR(s), ScreenBackgroundService.KeyTopG(s), ScreenBackgroundService.KeyTopB(s),
                ScreenBackgroundService.KeyBotR(s), ScreenBackgroundService.KeyBotG(s), ScreenBackgroundService.KeyBotB(s),
                ScreenBackgroundService.KeyBias(s), ScreenBackgroundService.KeySmooth(s),
                ScreenBackgroundService.KeyEnabled(s), ScreenBackgroundService.KeyPattern(s),
                ScreenBackgroundService.KeyPatternR(s), ScreenBackgroundService.KeyPatternG(s),
                ScreenBackgroundService.KeyPatternB(s), ScreenBackgroundService.KeyPatternA(s),
            })
                SettingsService.Remove(k);

            _scTopR = _scTopG = _scTopB = 0f;
            _scBotR = _scBotG = _scBotB = 1f;
            _scBias = 0f; _scSmooth = 1f; _scEnabled = false; _scPattern = "";
            _scPatternR = _scPatternG = _scPatternB = _scPatternA = 1f;

            if (s == ScreenBackgroundService.Screen.FallForce)
            {
                MenuCustomizationApplication.Instance?.RestoreBackdrop();
                MenuCustomizationApplication.Instance?.RestorePattern();
            }
            RebuildScreenBody();
        }

        // ── Falling screen (lobby bg) body ────────────────────────────────────
        // recolours the named DarkBlue/MedBlue/LightBlue images in Menu_Screen_Lobby. moved here out
        // of the Main Menu tab so the falling-screen colours live with the other screens.

        private void SyncLbSwatches()
        {
            if (_lbSwatch0 != null) _lbSwatch0.color = new Color(_lbSlot0R, _lbSlot0G, _lbSlot0B);
            if (_lbSwatch1 != null) _lbSwatch1.color = new Color(_lbSlot1R, _lbSlot1G, _lbSlot1B);
            if (_lbSwatch2 != null) _lbSwatch2.color = new Color(_lbSlot2R, _lbSlot2G, _lbSlot2B);
            RefreshFallingPreview();
        }

        private void BuildFallingBody(RectTransform parent, float x, float y, float w, float h)
        {
            BuildFallingPreview(BuildPreviewFrame(parent, x, y, w), x, w);

            var (scrollRect, content) = BuildFramedScroll(parent, x, y + SCREEN_PREVIEW_H + PREVIEW_GAP, w, h - SCREEN_PREVIEW_H - PREVIEW_GAP);
            w -= UGUIShip.SCROLLBAR_INSET * 2f;
            float cy = PAD;

            _lbEnabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _lbEnabled ? "ui.custom_colours_on" : "ui.custom_colours_off", _lbEnabled ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _lbEnabled = !_lbEnabled;
                    SettingsService.Set(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, _lbEnabled ? "true" : "false");
                    var lbl = _lbEnabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _lbEnabled ? "ui.custom_colours_on" : "ui.custom_colours_off");
                    var img = _lbEnabledBtn?.GetComponent<Image>();
                    if (img != null) img.color = _lbEnabled ? BTN_ON : BTN_DARK;
                    ApplyFallingLive();
                    RefreshFallingPreview();
                }));
            cy += BTN_H + SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float swatchW = BTN_H;
            float lbSliderW = w - swatchW - PAD;

            // dark blue
            UGUIShip.CreateLabel(content, new Rect(x, cy, lbSliderW, LH), "ui.dark_blue", FS_SM, HINT);
            cy += LH + SH;
            var s0go = new GameObject("LbSwatch0");
            s0go.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(s0go.AddComponent<RectTransform>(), new Rect(x + lbSliderW + PAD, cy, swatchW, (LH + SH) * 3f - SH));
            _lbSwatch0 = s0go.AddComponent<Image>();
            _lbSwatch0.color = new Color(_lbSlot0R, _lbSlot0G, _lbSlot0B);
            UGUIShip.CreateColorControls(content, x, ref cy, lbSliderW,
                () => _lbSlot0R, () => _lbSlot0G, () => _lbSlot0B,
                v => _lbSlot0R = v, v => _lbSlot0G = v, v => _lbSlot0B = v, () => SyncLbSwatches(), out _, out _, out _,
                new Color(0f, 0f, 1f));

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // med blue
            UGUIShip.CreateLabel(content, new Rect(x, cy, lbSliderW, LH), "ui.med_blue", FS_SM, HINT);
            cy += LH + SH;
            var s1go = new GameObject("LbSwatch1");
            s1go.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(s1go.AddComponent<RectTransform>(), new Rect(x + lbSliderW + PAD, cy, swatchW, (LH + SH) * 3f - SH));
            _lbSwatch1 = s1go.AddComponent<Image>();
            _lbSwatch1.color = new Color(_lbSlot1R, _lbSlot1G, _lbSlot1B);
            UGUIShip.CreateColorControls(content, x, ref cy, lbSliderW,
                () => _lbSlot1R, () => _lbSlot1G, () => _lbSlot1B,
                v => _lbSlot1R = v, v => _lbSlot1G = v, v => _lbSlot1B = v, () => SyncLbSwatches(), out _, out _, out _,
                new Color(0f, 0.5f, 1f));

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // light blue
            UGUIShip.CreateLabel(content, new Rect(x, cy, lbSliderW, LH), "ui.light_blue", FS_SM, HINT);
            cy += LH + SH;
            var s2go = new GameObject("LbSwatch2");
            s2go.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(s2go.AddComponent<RectTransform>(), new Rect(x + lbSliderW + PAD, cy, swatchW, (LH + SH) * 3f - SH));
            _lbSwatch2 = s2go.AddComponent<Image>();
            _lbSwatch2.color = new Color(_lbSlot2R, _lbSlot2G, _lbSlot2B);
            UGUIShip.CreateColorControls(content, x, ref cy, lbSliderW,
                () => _lbSlot2R, () => _lbSlot2G, () => _lbSlot2B,
                v => _lbSlot2R = v, v => _lbSlot2G = v, v => _lbSlot2B = v, () => SyncLbSwatches(), out _, out _, out _,
                new Color(0.8f, 0.8f, 1f));

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void LoadFallingSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            _lbEnabled = SettingsService.Get(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, "false") == "true";
            _lbSlot0R = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_R, 0f);
            _lbSlot0G = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_G, 0f);
            _lbSlot0B = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_B, 1f);
            _lbSlot1R = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_R, 0f);
            _lbSlot1G = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_G, 0.5f);
            _lbSlot1B = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_B, 1f);
            _lbSlot2R = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_R, 0.8f);
            _lbSlot2G = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_G, 0.8f);
            _lbSlot2B = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_B, 1f);
        }

        private void OnFallingApply()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void S(string k, float v) => SettingsService.Set(k, v.ToString(ci));
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_R, _lbSlot0R);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_G, _lbSlot0G);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_B, _lbSlot0B);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_R, _lbSlot1R);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_G, _lbSlot1G);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_B, _lbSlot1B);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_R, _lbSlot2R);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_G, _lbSlot2G);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_B, _lbSlot2B);
            SettingsService.Set(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, _lbEnabled ? "true" : "false");
            ApplyFallingLive();
        }

        private void ApplyFallingLive()
        {
            if (_lbEnabled)
                MenuCustomizationApplication.Instance?.ApplyLobbyBgCustomColors(
                    new Color(_lbSlot0R, _lbSlot0G, _lbSlot0B),
                    new Color(_lbSlot1R, _lbSlot1G, _lbSlot1B),
                    new Color(_lbSlot2R, _lbSlot2G, _lbSlot2B));
            else
                MenuCustomizationApplication.Instance?.RevertLobbyBGForeground();
        }

        private void OnFallingRemove()
        {
            foreach (var k in new[]
            {
                MenuCustomizationApplication.KEY_LOBBYBG_ENABLED,
                MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_R, MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_G, MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_B,
                MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_R, MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_G, MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_B,
                MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_R, MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_G, MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_B,
            })
                SettingsService.Remove(k);

            _lbEnabled = false;
            _lbSlot0R = 0f; _lbSlot0G = 0f; _lbSlot0B = 1f;
            _lbSlot1R = 0f; _lbSlot1G = 0.5f; _lbSlot1B = 1f;
            _lbSlot2R = 0.8f; _lbSlot2G = 0.8f; _lbSlot2B = 1f;
            MenuCustomizationApplication.Instance?.RevertLobbyBGForeground();
            RebuildScreenBody();
        }

        private void BuildFallingPreview(Transform previewSlot, float x, float w)
        {
            if (_lbPreviewGo != null) GameObject.Destroy(_lbPreviewGo);
            _lbPreviewGo = null;
            _lbPreviewSlots.Clear();
            _lbPreviewDefaults.Clear();
            if (previewSlot == null) return;

            var source = MenuCustomizationApplication.FindLobbyBgPreviewSource();
            if (source == null) return;

            var holderRt = (RectTransform)previewSlot;
            _lbPreviewGo = GameObject.Instantiate(source.gameObject, holderRt, false);
            _lbPreviewGo.name = "FallingPreviewClone";
            _lbPreviewGo.SetActive(true);

            foreach (var canvas in _lbPreviewGo.GetComponentsInChildren<Canvas>(true))
                if (canvas != null) Destroy(canvas);
            var scaler = _lbPreviewGo.GetComponent<CanvasScaler>();
            if (scaler != null) Destroy(scaler);
            var raycaster = _lbPreviewGo.GetComponent<GraphicRaycaster>();
            if (raycaster != null) Destroy(raycaster);

            FitCloneToPreview(_lbPreviewGo, source, w, SCREEN_PREVIEW_H);

            // see BuildScreenPreview: skip the Mask's stencil-material caching so live colour/sprite
            // updates below actually show up.
            foreach (var g in _lbPreviewGo.GetComponentsInChildren<Graphic>(true))
                if (g != null) { g.raycastTarget = false; if (g is MaskableGraphic mg) mg.maskable = false; }

            var app = MenuCustomizationApplication.Instance;
            var srcImages = source.GetComponentsInChildren<Image>(true);
            var cloneImages = _lbPreviewGo.GetComponentsInChildren<Image>(true);
            int n = Mathf.Min(srcImages.Length, cloneImages.Length);
            for (int i = 0; i < n; i++)
            {
                var srcImg = srcImages[i];
                var cloneImg = cloneImages[i];
                if (cloneImg == null || srcImg == null) continue;
                int slot = MenuCustomizationApplication.LobbyBgSlotIndex(cloneImg.gameObject.name);
                var trueColor = app != null ? app.TrueLobbyBgColor(srcImg) : srcImg.color;
                var trueSprite = app != null ? app.TrueLobbyBgSprite(srcImg) : srcImg.sprite;
                cloneImg.sprite = trueSprite;
                cloneImg.color = trueColor;
                if (slot >= 0)
                {
                    _lbPreviewSlots.Add((cloneImg, slot));
                    _lbPreviewDefaults.Add(trueColor);
                }
            }

            RefreshFallingPreview();
        }

        private void RefreshFallingPreview()
        {
            for (int i = 0; i < _lbPreviewSlots.Count && i < _lbPreviewDefaults.Count; i++)
            {
                var (img, slot) = _lbPreviewSlots[i];
                if (img == null) continue;
                var def = _lbPreviewDefaults[i];
                if (_lbEnabled)
                {
                    Color c = slot == 0 ? new Color(_lbSlot0R, _lbSlot0G, _lbSlot0B)
                            : slot == 1 ? new Color(_lbSlot1R, _lbSlot1G, _lbSlot1B)
                            : new Color(_lbSlot2R, _lbSlot2G, _lbSlot2B);
                    if (def.a < 0.05f) img.color = def;
                    else img.color = new Color(c.r, c.g, c.b, def.a);
                }
                else img.color = def;
            }
        }

        // ── Creative (level browser) body ─────────────────────────────────────
        // four named colour slots on Generic_UI_CreativeBackground_Prefab_Canvas. see
        // MenuCustomizationApplication.CreativeSlot.
        private static readonly string[] CreativeSlotLabels = { "BACKDROP", "GLOWS", "DRAWINGS", "VIGNETTE" };

        private void BuildCreativeBody(RectTransform parent, float x, float y, float w, float h)
        {
            BuildCreativePreview(BuildPreviewFrame(parent, x, y, w), x, w);

            var (scrollRect, content) = BuildFramedScroll(parent, x, y + SCREEN_PREVIEW_H + PREVIEW_GAP, w, h - SCREEN_PREVIEW_H - PREVIEW_GAP);
            w -= UGUIShip.SCROLLBAR_INSET * 2f;
            float cy = PAD;

            _crEnabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _crEnabled ? "ui.custom_colours_on" : "ui.custom_colours_off", _crEnabled ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _crEnabled = !_crEnabled;
                    SettingsService.Set(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, _crEnabled ? "true" : "false");
                    var lbl = _crEnabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _crEnabled ? "ui.custom_colours_on" : "ui.custom_colours_off");
                    var img = _crEnabledBtn?.GetComponent<Image>();
                    if (img != null) img.color = _crEnabled ? BTN_ON : BTN_DARK;
                    ApplyCreativeLive();
                    RefreshCreativePreview();
                }));
            cy += BTN_H + SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float swatchW = BTN_H;
            float sliderW = w - swatchW - PAD;

            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                UGUIShip.CreateLabel(content, new Rect(x, cy, sliderW, LH), CreativeSlotLabels[slot], FS_SM, HINT);
                cy += LH + SH;
                var sw = new GameObject("CrSwatch" + slot);
                sw.transform.SetParent(content, false);
                UGUIShip.SetPixelRect(sw.AddComponent<RectTransform>(), new Rect(x + sliderW + PAD, cy, swatchW, (LH + SH) * 3f - SH));
                _crSwatch[slot] = sw.AddComponent<Image>();
                _crSwatch[slot].color = new Color(_crR[slot], _crG[slot], _crB[slot]);
                UGUIShip.CreateColorControls(content, x, ref cy, sliderW,
                    () => _crR[slot], () => _crG[slot], () => _crB[slot],
                    v => _crR[slot] = v, v => _crG[slot] = v, v => _crB[slot] = v,
                    () =>
                    {
                        if (_crSwatch[slot] != null) _crSwatch[slot].color = new Color(_crR[slot], _crG[slot], _crB[slot]);
                        RefreshCreativePreview();
                    },
                    out _, out _, out _,
                    MenuCustomizationApplication.CreativeSlotDefault((MenuCustomizationApplication.CreativeSlot)slot));

                if (slot < 3) { UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f)); cy += 1f + PAD; }
            }

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        // clones the live level-editor creative canvas so the preview shows the real paper-craft art,
        // not swatches. lives in its own framed slot above the scroll area.
        private void BuildCreativePreview(Transform previewSlot, float x, float w)
        {
            if (_crPreviewGo != null) GameObject.Destroy(_crPreviewGo);
            _crPreviewGo = null;
            _crPreviewGraphics.Clear();
            _crPreviewDefaults.Clear();
            if (previewSlot == null) return;

            var source = MenuCustomizationApplication.FindCreativePreviewSource();
            if (source == null) return; // level editor view has never been up this session

            var holderRt = (RectTransform)previewSlot;
            _crPreviewGo = GameObject.Instantiate(source.gameObject, holderRt, false);
            _crPreviewGo.name = "CreativePreviewClone";

            // it's a world-space Canvas prefab (lives on a 3D CameraRig), not a plain UI group like the
            // Screen preview's Mask — its own Canvas component and baked-in world-space localScale both
            // fight our anchor-stretch, which is what made it render tiny. strip the Canvas so it just
            // renders as normal children of our own UI's canvas, and reset the scale explicitly.
            foreach (var canvas in _crPreviewGo.GetComponentsInChildren<Canvas>(true))
                if (canvas != null) Destroy(canvas);
            var scaler = _crPreviewGo.GetComponent<CanvasScaler>();
            if (scaler != null) Destroy(scaler);
            var raycaster = _crPreviewGo.GetComponent<GraphicRaycaster>();
            if (raycaster != null) Destroy(raycaster);

            FitCloneToPreview(_crPreviewGo, source, w, SCREEN_PREVIEW_H);

            // see BuildScreenPreview: skip the Mask's stencil-material caching so live colour/sprite
            // updates below actually show up.
            foreach (var g in _crPreviewGo.GetComponentsInChildren<Graphic>(true))
                if (g != null) { g.raycastTarget = false; if (g is MaskableGraphic mg) mg.maskable = false; }

            // pair up clone graphics with the SOURCE's true (untainted) colour, slot by slot — the
            // clone's own graphics may already be sitting mid-custom-colour if the source canvas was
            // recoloured earlier this session, same taint risk the Screen preview's pattern had.
            MenuCustomizationApplication.ForEachCreativeSlotGraphic(source,
                (g, slot) => _crPreviewDefaults.Add(MenuCustomizationApplication.Instance?.TrueCreativeColor(g) ?? g.color));
            MenuCustomizationApplication.ForEachCreativeSlotGraphic(_crPreviewGo.transform,
                (g, slot) => _crPreviewGraphics.Add((g, slot)));

            RefreshCreativePreview();
        }

        private void RefreshCreativePreview()
        {
            for (int i = 0; i < _crPreviewGraphics.Count && i < _crPreviewDefaults.Count; i++)
            {
                var (g, slot) = _crPreviewGraphics[i];
                if (g == null) continue;
                Color c = _crEnabled
                    ? new Color(_crR[(int)slot], _crG[(int)slot], _crB[(int)slot])
                    : _crPreviewDefaults[i];
                g.color = new Color(c.r, c.g, c.b, g.color.a);
            }
        }

        private void LoadCreativeSettings()
        {
            _crEnabled = SettingsService.Get(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, "false") == "true";
            for (int i = 0; i < 4; i++)
            {
                var c = MenuCustomizationApplication.CreativeSlotColor((MenuCustomizationApplication.CreativeSlot)i);
                _crR[i] = c.r; _crG[i] = c.g; _crB[i] = c.b;
            }
        }

        private void SaveCreativeSlots()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < 4; i++)
            {
                string k = "screen.creative." + (MenuCustomizationApplication.CreativeSlot)i;
                SettingsService.Set(k + ".r", _crR[i].ToString(ci));
                SettingsService.Set(k + ".g", _crG[i].ToString(ci));
                SettingsService.Set(k + ".b", _crB[i].ToString(ci));
            }
        }

        private void OnCreativeApply()
        {
            SaveCreativeSlots();
            SettingsService.Set(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, _crEnabled ? "true" : "false");
            ApplyCreativeLive();
        }

        // push current sliders to settings first so the live bg reflects them (recolour reads settings)
        private void ApplyCreativeLive()
        {
            SaveCreativeSlots();
            MenuCustomizationApplication.Instance?.ReapplyCreativeBgLive();
        }

        private void OnCreativeRemove()
        {
            for (int i = 0; i < 4; i++)
            {
                string k = "screen.creative." + (MenuCustomizationApplication.CreativeSlot)i;
                SettingsService.Remove(k + ".r"); SettingsService.Remove(k + ".g"); SettingsService.Remove(k + ".b");
            }
            SettingsService.Remove(MenuCustomizationApplication.KEY_CREATIVE_ENABLED);
            MenuCustomizationApplication.Instance?.ReapplyCreativeBgLive();
            _crEnabled = false;
            LoadCreativeSettings();
            RebuildScreenBody();
        }

        // ── Slider helpers ────────────────────────────────────────────────────

        private Slider BuildSliderRaw(Transform parent, float x, float y, float w,
            string lbl, float init, float min, float max, Action<float> onChange, float? resetTo = null)
            => UGUIShip.CreateSlider(parent, x, y, w, lbl, Mathf.InverseLerp(min, max, init),
                LH, PAD, FS_SM, t => onChange(Mathf.Lerp(min, max, t)), null, null, true,
                resetTo.HasValue ? Mathf.InverseLerp(min, max, resetTo.Value) : (float?)null);
    }
}
