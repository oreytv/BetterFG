using System;
using System.Reflection;
using BetterFG.Customization.Menu;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tabs
{
    public class UIBackgroundTab : UISubTab
    {
        public UIBackgroundTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "UI - Background";

        protected override void BuildContent(RectTransform contentRoot)
        {
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
        private string _scPattern = "";
        private Button _scEnabledBtn;

        // ── live clone preview (Backdrop + Circles) ────────────────────────────
        private const float SCREEN_PREVIEW_H = 100f;
        private GameObject _scPreviewGo;
        private Image _scPreviewBackdrop;
        private Image _scPreviewCircles;
        private Sprite _scPreviewDefaultBackdrop;
        private Color _scPreviewDefaultBackdropColor;
        private Texture _scPreviewDefaultPattern;
        private Texture2D _scPreviewGradTex;
        private Texture2D _scPreviewPatternTex;
        private string _scPreviewPatternPath;
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
                    if (lbl != null) lbl.text = HeaderLabel();
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

            // apply / remove row pinned at the bottom
            float by = y + bodyH + PAD;
            UGUIShip.CreatePanel(parent, new Rect(PAD, by, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            by += 1f + PAD;
            float btnw = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(parent, new Rect(PAD, by, btnw, BTN_H),
                "Apply", BTN_APPLY, WHITE, FS, new Action(() => { if (_creativeSel) OnCreativeApply(); else if (_fallingSel) OnFallingApply(); else OnScreenApply(); }));
            UGUIShip.CreateButton(parent, new Rect(PAD + btnw + PAD * 0.5f, by, btnw, BTN_H),
                "Remove", BTN_REMOVE, WHITE, FS, new Action(() => { if (_creativeSel) OnCreativeRemove(); else if (_fallingSel) OnFallingRemove(); else OnScreenRemove(); }));
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

        private void BuildScreenBody(RectTransform parent, float x, float y, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            BuildScreenPreview(scrollRect.transform.Find("Viewport"), x, w);

            float cy = SCREEN_PREVIEW_H + PAD;

            // single on/off — for FallForce this also flips the menu BG visibility so showing
            // custom colours always means showing the custom BG. (other screens have no separate
            // BG concept.)
            bool isFallForce = _screenSel == ScreenBackgroundService.Screen.FallForce;
            _scEnabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _scEnabled ? "Show custom bg: ON" : "Show custom bg: OFF", _scEnabled ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _scEnabled = !_scEnabled;
                    SettingsService.Set(ScreenBackgroundService.KeyEnabled(_screenSel), _scEnabled ? "true" : "false");
                    if (isFallForce)
                    {
                        SettingsService.Set(MenuCustomizationApplication.KEY_BG_ENABLED, _scEnabled ? "true" : "false");
                        MenuCustomizationApplication.Instance?.SetMenuBgEnabled(_scEnabled);
                    }
                    var lbl = _scEnabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _scEnabled ? "Show custom bg: ON" : "Show custom bg: OFF";
                    var img = _scEnabledBtn?.GetComponent<Image>();
                    if (img != null) img.color = _scEnabled ? BTN_ON : BTN_DARK;
                    ApplyScreenLive();
                    RefreshScreenPreview();
                }));
            cy += BTN_H + SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float slidersW = w;

            // top color
            UGUIShip.CreateLabel(content, new Rect(x, cy, slidersW, LH), "TOP COLOR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(content, x, ref cy, slidersW,
                () => _scTopR, () => _scTopG, () => _scTopB,
                v => _scTopR = v, v => _scTopG = v, v => _scTopB = v, () => RefreshScreenPreview(), out _, out _, out _,
                Color.black);

            // bottom color
            UGUIShip.CreateLabel(content, new Rect(x, cy, slidersW, LH), "BOTTOM COLOR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(content, x, ref cy, slidersW,
                () => _scBotR, () => _scBotG, () => _scBotB,
                v => _scBotR = v, v => _scBotG = v, v => _scBotB = v, () => RefreshScreenPreview(), out _, out _, out _,
                Color.white);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // shader (texture bake) params
            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "GRADIENT SHAPE", FS_SM, HINT);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Bias", _scBias, -1f, 1f, v => { _scBias = v; RefreshScreenPreview(); }, 0f);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Smooth", _scSmooth, 0.1f, 8f, v => { _scSmooth = v; RefreshScreenPreview(); }, 1f);
            cy += LH + PAD;

            // circles pattern — ShowSelector has a Circles image but no working pattern feature in-game
            if (_screenSel != ScreenBackgroundService.Screen.ShowSelector)
            {
                UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
                cy += 1f + PAD;

                UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "CIRCLES PATTERN", FS_SM, HINT);
                cy += LH + SH;
                float patBtnW = BTN_H * 2.5f, resetW = BTN_H * 2f;
                float patLblW = w - patBtnW - resetW - PAD * 2f;
                _scPatternLabel = UGUIShip.CreateLabel(content, new Rect(x, cy, patLblW, BTN_H),
                    string.IsNullOrEmpty(_scPattern) ? "none" : System.IO.Path.GetFileName(_scPattern),
                    FS_SM, HINT, TextAnchor.MiddleLeft);
                UGUIShip.CreateButton(content, new Rect(x + patLblW + PAD, cy, patBtnW, BTN_H),
                    "Browse", BTN_DARK, WHITE, FS_SM,
                    new Action(() => WinDialogs.PickPng("Select pattern PNG", path =>
                    {
                        if (string.IsNullOrEmpty(path)) return;
                        _scPattern = path;
                        SettingsService.Set(ScreenBackgroundService.KeyPattern(_screenSel), path);
                        if (_scPatternLabel != null) _scPatternLabel.text = System.IO.Path.GetFileName(path);
                        RefreshScreenPreview();
                    })));
                UGUIShip.CreateButton(content, new Rect(x + patLblW + PAD + patBtnW + PAD, cy, resetW, BTN_H),
                    "Reset", BTN_REMOVE, WHITE, FS_SM,
                    new Action(() =>
                    {
                        _scPattern = "";
                        SettingsService.Remove(ScreenBackgroundService.KeyPattern(_screenSel));
                        if (_scPatternLabel != null) _scPatternLabel.text = "none";
                        if (_screenSel == ScreenBackgroundService.Screen.FallForce)
                            MenuCustomizationApplication.Instance?.RestorePattern();
                        RefreshScreenPreview();
                    }));
                cy += BTN_H + PAD;
            }

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        // the container to clone the preview from. every screen except FallForce now has its own
        // reliable, always-findable source (pre-instantiated and inactive off the menu scene — none of
        // them need to actually be showing right now).
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
                default: // FallForce
                    t = null;
                    break;
            }
            ownGeometry = t != null || s == ScreenBackgroundService.Screen.FallForce;
            return t ?? MenuCustomizationApplication.FindBackgroundContainer()?.transform;
        }

        // clones the live Backdrop+Circles container once so the preview shows the real shader/pattern,
        // not an approximation — pinned to the viewport (not content) so it stays put while the sliders
        // below scroll, same trick the banner preview uses.
        private void BuildScreenPreview(Transform viewport, float x, float w)
        {
            if (_scPreviewGo != null) GameObject.Destroy(_scPreviewGo);
            _scPreviewGo = null; _scPreviewBackdrop = null; _scPreviewCircles = null; _scPreviewDefaultPattern = null;
            if (viewport == null) return;

            var source = ScreenPreviewSource(_screenSel, out bool ownGeometry);
            if (source == null) return; // not in the main menu right now, nothing live to clone

            // this screen's OWN container, found live — learn its real default right now, no need to
            // have actually lived through it this session (LoadingLevel/Explore/ShowSelector are all
            // pre-instantiated and inactive off the menu scene, same as ShowSelectorBg already relied on).
            if (_screenSel != ScreenBackgroundService.Screen.FallForce && ownGeometry)
                ScreenBackgroundService.CacheScreenDefault(_screenSel, source);

            var holderGo = new GameObject("ScreenPreviewHolder");
            holderGo.transform.SetParent(viewport, false);
            var holderRt = holderGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(holderRt, new Rect(x, 0f, w, SCREEN_PREVIEW_H));
            holderGo.AddComponent<RectMask2D>();

            _scPreviewGo = GameObject.Instantiate(source.gameObject, holderRt, false);
            _scPreviewGo.name = "ScreenPreviewClone";
            var cloneRt = _scPreviewGo.GetComponent<RectTransform>();
            cloneRt.anchorMin = Vector2.zero;
            cloneRt.anchorMax = Vector2.one;
            cloneRt.offsetMin = cloneRt.offsetMax = Vector2.zero;

            foreach (var anim in _scPreviewGo.GetComponentsInChildren<Animator>(true))
                if (anim != null) anim.enabled = false;
            foreach (var g in _scPreviewGo.GetComponentsInChildren<Graphic>(true))
                if (g != null) g.raycastTarget = false;

            _scPreviewBackdrop = _scPreviewGo.transform.Find("Backdrop")?.GetComponent<Image>();
            _scPreviewCircles = _scPreviewGo.transform.Find("Circles")?.GetComponent<Image>();

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

            // FallForce's Backdrop sprite/colour are never touched by the live game at all (the visible
            // menu bg is a separate quad BettrFG spawns on top), so reading it straight off the clone
            // is always the true default — no taint risk, unlike every other screen. those go through
            // ScreenBackgroundService's cache instead (populated above via CacheScreenDefault, which IS
            // taint-protected), see RefreshScreenPreview.
            if (_screenSel == ScreenBackgroundService.Screen.FallForce && _scPreviewBackdrop != null)
            {
                _scPreviewDefaultBackdrop = _scPreviewBackdrop.sprite;
                _scPreviewDefaultBackdropColor = _scPreviewBackdrop.color;
            }
            // Circles' pattern, unlike Backdrop, IS mutated live by ApplyPatternFromSettings — if a
            // custom pattern is already active this session, reading the clone's material straight
            // off would hand us back our OWN texture as the "default". route through the guarded
            // capture instead, which remembers the real original from before any custom was applied.
            _scPreviewDefaultPattern = MenuCustomizationApplication.Instance?.EnsureOriginalCirclesPatternCaptured();

            _scPreviewHintLabel = UGUIShip.CreateLabel(holderRt, new Rect(PAD, 0f, w - PAD * 2f, SCREEN_PREVIEW_H),
                "haven't seen this screen's real default yet this session — visit it once, or turn custom colours on above to preview those instead",
                FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshScreenPreview();
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

            // ShowSelector has a Circles image but no working pattern feature in-game — never touch it.
            if (_scPreviewCircles != null && _scPreviewCircles.material != null &&
                _screenSel != ScreenBackgroundService.Screen.ShowSelector)
            {
                // circles are a shared seasonal decoration, not per-screen themed art like Backdrop is —
                // fall back to FallForce's (always known) pattern rather than null when a screen's own
                // hasn't been cached, so the pattern doesn't just vanish for screens we haven't learned yet.
                Texture patternTex = _scPreviewDefaultPattern;
                if (!isFallForce && ScreenBackgroundService.TryGetScreenDefault(_screenSel, out _, out _, out var cachedPattern))
                    patternTex = cachedPattern;
                if (_scEnabled && !string.IsNullOrEmpty(_scPattern) && System.IO.File.Exists(_scPattern))
                {
                    if (_scPreviewPatternPath != _scPattern)
                    {
                        if (_scPreviewPatternTex != null) Destroy(_scPreviewPatternTex);
                        var ptex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        ptex.LoadImage(System.IO.File.ReadAllBytes(_scPattern));
                        ptex.Apply();
                        _scPreviewPatternTex = ptex;
                        _scPreviewPatternPath = _scPattern;
                    }
                    patternTex = _scPreviewPatternTex;
                }
                _scPreviewCircles.material.SetTexture("_Pattern", patternTex);
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
                    MenuCustomizationApplication.Instance?.ApplyGradient(Color.black, Color.white, 0f, 1f);
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
            })
                SettingsService.Remove(k);

            _scTopR = _scTopG = _scTopB = 0f;
            _scBotR = _scBotG = _scBotB = 1f;
            _scBias = 0f; _scSmooth = 1f; _scEnabled = false; _scPattern = "";

            if (s == ScreenBackgroundService.Screen.FallForce)
            {
                MenuCustomizationApplication.Instance?.ApplyGradient(Color.black, Color.white, 0f, 1f);
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
        }

        private void BuildFallingBody(RectTransform parent, float x, float y, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;

            _lbEnabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _lbEnabled ? "Custom colours: ON" : "Custom colours: OFF", _lbEnabled ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _lbEnabled = !_lbEnabled;
                    SettingsService.Set(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, _lbEnabled ? "true" : "false");
                    var lbl = _lbEnabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _lbEnabled ? "Custom colours: ON" : "Custom colours: OFF";
                    var img = _lbEnabledBtn?.GetComponent<Image>();
                    if (img != null) img.color = _lbEnabled ? BTN_ON : BTN_DARK;
                    ApplyFallingLive();
                }));
            cy += BTN_H + SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float swatchW = BTN_H;
            float lbSliderW = w - swatchW - PAD;

            // dark blue
            UGUIShip.CreateLabel(content, new Rect(x, cy, lbSliderW, LH), "DARK BLUE", FS_SM, HINT);
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
            UGUIShip.CreateLabel(content, new Rect(x, cy, lbSliderW, LH), "MED BLUE", FS_SM, HINT);
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
            UGUIShip.CreateLabel(content, new Rect(x, cy, lbSliderW, LH), "LIGHT BLUE", FS_SM, HINT);
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

        // ── Creative (level browser) body ─────────────────────────────────────
        // four named colour slots on Generic_UI_CreativeBackground_Prefab_Canvas. see
        // MenuCustomizationApplication.CreativeSlot.
        private static readonly string[] CreativeSlotLabels = { "BACKDROP", "GLOWS", "DRAWINGS", "VIGNETTE" };

        private void BuildCreativeBody(RectTransform parent, float x, float y, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            BuildCreativePreview(scrollRect.transform.Find("Viewport"), x, w);

            float cy = SCREEN_PREVIEW_H + PAD;

            _crEnabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _crEnabled ? "Custom colours: ON" : "Custom colours: OFF", _crEnabled ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _crEnabled = !_crEnabled;
                    SettingsService.Set(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, _crEnabled ? "true" : "false");
                    var lbl = _crEnabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _crEnabled ? "Custom colours: ON" : "Custom colours: OFF";
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
        // not swatches — same pinned-to-viewport trick as the Screen/Falling previews.
        private void BuildCreativePreview(Transform viewport, float x, float w)
        {
            if (_crPreviewGo != null) GameObject.Destroy(_crPreviewGo);
            _crPreviewGo = null;
            _crPreviewGraphics.Clear();
            _crPreviewDefaults.Clear();
            if (viewport == null) return;

            var source = MenuCustomizationApplication.FindCreativePreviewSource();
            if (source == null) return; // level editor view has never been up this session

            var holderGo = new GameObject("CreativePreviewHolder");
            holderGo.transform.SetParent(viewport, false);
            var holderRt = holderGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(holderRt, new Rect(x, 0f, w, SCREEN_PREVIEW_H));
            holderGo.AddComponent<RectMask2D>();

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

            var cloneRt = _crPreviewGo.GetComponent<RectTransform>();
            cloneRt.localScale = Vector3.one;
            cloneRt.localRotation = Quaternion.identity;
            cloneRt.anchorMin = Vector2.zero;
            cloneRt.anchorMax = Vector2.one;
            cloneRt.offsetMin = cloneRt.offsetMax = Vector2.zero;

            foreach (var g in _crPreviewGo.GetComponentsInChildren<Graphic>(true))
                if (g != null) g.raycastTarget = false;

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
