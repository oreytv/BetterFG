using System;
using BetterFG.Services;
using BetterFG.Nametag;
using BetterFG.Customization.Menu;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class NametagColourTab : NametagWizardTab
    {
        public NametagColourTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Nametag - Colour";
        protected override string TitleId => "ui.nametag_colour";
        protected override string BgResource => "BetterFG.assets.ui.nametag.bg.png";

        private const string KEY_COLOR_R = "nametag.color.r";
        private const string KEY_COLOR_G = "nametag.color.g";
        private const string KEY_COLOR_B = "nametag.color.b";
        private const string KEY_BOLD = "nametag.bold";
        private const string KEY_ITALIC = "nametag.italic";
        private const string KEY_ENABLED = "nametag.enabled";
        private const string KEY_NAME_STYLE = "nametag.namestyle";
        private const string KEY_GHOST_NAMETAG = "nametag.ghost.enabled";

        private static readonly Color WHITE2 = Color.white;
        private static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_DARK2 = new Color(0.2f, 0.2f, 0.2f, 1f);

        private enum NameStyle { None, Default, Gold, GoldColored }

        private float _r = 1f, _g = 1f, _b = 1f;
        private bool _bold, _italic;
        private NameStyle _nameStyle = NameStyle.Default;
        private bool _ghostNametag;

        private Slider _sliderR, _sliderG, _sliderB;
        private Text _boldBtnLabel, _italicBtnLabel, _ghostNametagBtnLabel;
        private Button _btnStyleNone, _btnStyleDefault, _btnStyleGold, _btnStyleGoldColored;

        protected override string[] StepTitles => new[] { "ui.text_colour", "ui.bold_italic", "ui.preset_style", "ui.ghost_nametag" };
        protected override bool HasRemove => true;
        protected override Tab MakeListTarget() => BetterFGTabRegistry.NewTab<NametagTab>();

        void Awake()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            _r = float.TryParse(SettingsService.Get(KEY_COLOR_R, "1"), System.Globalization.NumberStyles.Float, ci, out float r) ? r : 1f;
            _g = float.TryParse(SettingsService.Get(KEY_COLOR_G, "1"), System.Globalization.NumberStyles.Float, ci, out float g) ? g : 1f;
            _b = float.TryParse(SettingsService.Get(KEY_COLOR_B, "1"), System.Globalization.NumberStyles.Float, ci, out float b) ? b : 1f;
            _bold = SettingsService.Get(KEY_BOLD, "false") == "true";
            _italic = SettingsService.Get(KEY_ITALIC, "false") == "true";
            string styleStr = SettingsService.Get(KEY_NAME_STYLE, "default");
            _nameStyle = styleStr == "none" ? NameStyle.None
                       : styleStr == "gold" ? NameStyle.Gold
                       : styleStr == "goldcolored" ? NameStyle.GoldColored
                       : NameStyle.Default;
            _ghostNametag = SettingsService.Get(KEY_GHOST_NAMETAG, "false") == "true";
        }

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            var (_, c) = UGUIShip.CreateScrollView(root, new Rect(0f, 0f, TabWidth, bodyH));
            float cw = w - 26f;
            switch (step)
            {
                case 0: BuildColourStep(c, cw); break;
                case 1: BuildStyleStep(c, cw); break;
                case 2: BuildPresetStep(c, cw); break;
                case 3: BuildGhostStep(c, cw); break;
            }
        }

        private void BuildColourStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "ui.colour", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(c, x, ref cy, w,
                () => _r, () => _g, () => _b,
                v => _r = v, v => _g = v, v => _b = v, () => RefreshPreview(),
                out _sliderR, out _sliderG, out _sliderB, Color.white);
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildStyleStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "ui.style", FS_SM, HINT);
            cy += LH + SH;
            float togglew = (w - PAD * 0.5f) / 2f;
            var boldBtn = UGUIShip.CreateButton(c, new Rect(x, cy, togglew, BTN_H),
                _bold ? "ui.bold_on" : "ui.bold_off", BTN_DARK2, WHITE2, FS_SM, new Action(OnToggleBold));
            _boldBtnLabel = boldBtn.GetComponentInChildren<Text>();
            var italicBtn = UGUIShip.CreateButton(c, new Rect(x + togglew + PAD * 0.5f, cy, togglew, BTN_H),
                _italic ? "ui.italic_on" : "ui.italic_off", BTN_DARK2, WHITE2, FS_SM, new Action(OnToggleItalic));
            _italicBtnLabel = italicBtn.GetComponentInChildren<Text>();
            cy += BTN_H + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildPresetStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "ui.predefined_style", FS_SM, HINT);
            cy += LH + SH;
            float stylew = (w - PAD * 1.5f) / 4f;
            float stylestep = stylew + PAD * 0.5f;
            _btnStyleNone = UGUIShip.CreateButton(c, new Rect(x, cy, stylew, BTN_H), "ui.none_2",
                _nameStyle == NameStyle.None ? SEL_COLOR : BTN_DARK2, WHITE2, FS_SM, new Action(() => SetNameStyle(NameStyle.None)));
            _btnStyleDefault = UGUIShip.CreateButton(c, new Rect(x + stylestep, cy, stylew, BTN_H), "ui.default",
                _nameStyle == NameStyle.Default ? SEL_COLOR : BTN_DARK2, WHITE2, FS_SM, new Action(() => SetNameStyle(NameStyle.Default)));
            _btnStyleGold = UGUIShip.CreateButton(c, new Rect(x + stylestep * 2f, cy, stylew, BTN_H), "ui.gold",
                _nameStyle == NameStyle.Gold ? SEL_COLOR : BTN_DARK2, WHITE2, FS_SM, new Action(() => SetNameStyle(NameStyle.Gold)));
            _btnStyleGoldColored = UGUIShip.CreateButton(c, new Rect(x + stylestep * 3f, cy, stylew, BTN_H), "ui.gold_rgb",
                _nameStyle == NameStyle.GoldColored ? SEL_COLOR : BTN_DARK2, WHITE2, FS_SM, new Action(() => SetNameStyle(NameStyle.GoldColored)));
            cy += BTN_H + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildGhostStep(RectTransform c, float w)
        {
            float x = PAD, cy = PAD;
            UGUIShip.CreateLabel(c, new Rect(x, cy, w, LH), "ui.ghost", FS_SM, HINT);
            cy += LH + SH;
            var ghostBtn = UGUIShip.CreateButton(c, new Rect(x, cy, w, BTN_H),
                _ghostNametag ? "ui.show_ghost_nametag_on" : "ui.show_ghost_nametag_off",
                BTN_DARK2, WHITE2, FS_SM, new Action(OnToggleGhostNametag));
            _ghostNametagBtnLabel = ghostBtn.GetComponentInChildren<Text>();
            cy += BTN_H + PAD;
            c.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void OnToggleBold()
        {
            _bold = !_bold;
            if (_boldBtnLabel != null) UGUIShip.RelabelText(_boldBtnLabel, _bold ? "ui.bold_on" : "ui.bold_off");
            RefreshPreview();
        }

        private void OnToggleItalic()
        {
            _italic = !_italic;
            if (_italicBtnLabel != null) UGUIShip.RelabelText(_italicBtnLabel, _italic ? "ui.italic_on" : "ui.italic_off");
            RefreshPreview();
        }

        private void SetNameStyle(NameStyle style)
        {
            _nameStyle = style;
            UGUIShip.SetButtonSelected(_btnStyleNone, _nameStyle == NameStyle.None, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnStyleDefault, _nameStyle == NameStyle.Default, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnStyleGold, _nameStyle == NameStyle.Gold, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnStyleGoldColored, _nameStyle == NameStyle.GoldColored, SEL_COLOR);
            RefreshPreview();
        }

        private void OnToggleGhostNametag()
        {
            _ghostNametag = !_ghostNametag;
            SettingsService.Set(KEY_GHOST_NAMETAG, _ghostNametag ? "true" : "false");
            if (_ghostNametagBtnLabel != null) UGUIShip.RelabelText(_ghostNametagBtnLabel, _ghostNametag ? "ui.show_ghost_nametag_on" : "ui.show_ghost_nametag_off");
        }

        private string StyleString() =>
            _nameStyle == NameStyle.None ? "none"
            : _nameStyle == NameStyle.Gold ? "gold"
            : _nameStyle == NameStyle.GoldColored ? "goldcolored"
            : "default";

        public override void RefreshPreview()
        {
            var cfg = NametagIconApplicator.CfgFromSettings();
            cfg.enabled = true;
            cfg.r = _r; cfg.g = _g; cfg.b = _b;
            cfg.bold = _bold; cfg.italic = _italic;
            cfg.style = StyleString();
            var crownCfg = CrownRankService.CfgFromSettings();
            ApplyPreview(cfg, crownCfg, NametagIconApplicator.PlatformHideFromSettings(), NametagIconApplicator.PlatformCustomFromSettings());
        }

        protected override bool Save()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(KEY_COLOR_R, _r.ToString(ci));
            SettingsService.Set(KEY_COLOR_G, _g.ToString(ci));
            SettingsService.Set(KEY_COLOR_B, _b.ToString(ci));
            SettingsService.Set(KEY_BOLD, _bold ? "true" : "false");
            SettingsService.Set(KEY_ITALIC, _italic ? "true" : "false");
            SettingsService.Set(KEY_ENABLED, "true");
            SettingsService.Set(KEY_NAME_STYLE, StyleString());

            NametagIconApplicator.ApplyNametag();
            NametagFinder.ReapplyAllNameplates();
            MenuCustomizationApplication.Instance?.ReapplySpecialForeground(
                MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList);
            return true;
        }

        protected override void OnRemoveClicked()
        {
            _r = _g = _b = 1f;
            _bold = _italic = false;
            _nameStyle = NameStyle.Default;

            SettingsService.Set(KEY_COLOR_R, "1");
            SettingsService.Set(KEY_COLOR_G, "1");
            SettingsService.Set(KEY_COLOR_B, "1");
            SettingsService.Set(KEY_BOLD, "false");
            SettingsService.Set(KEY_ITALIC, "false");
            SettingsService.Set(KEY_NAME_STYLE, "default");

            NametagIconApplicator.ApplyNametag();
            NametagFinder.ReapplyAllNameplates();
        }
    }
}
