using System;
using BetterFG.Customization.Menu;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public partial class UIForegroundDetailTab
    {
        private bool _fgCyanOn;
        private float _fgCyanR = 0f, _fgCyanG = 0.3f, _fgCyanB = 1f;
        private bool _fgBlackOn;
        private float _fgBlackR = 0.75f, _fgBlackG = 0.75f, _fgBlackB = 0.75f;
        private bool _fgYellowOn;
        private float _fgYellowR = 1f, _fgYellowG = 0.5f, _fgYellowB = 0f;
        private bool _fgBlueOn;
        private float _fgBlueR = 0.1f, _fgBlueG = 0.25f, _fgBlueB = 0.85f;
        private bool _fgPinkOn;
        private float _fgPinkR = 1f, _fgPinkG = 0.2f, _fgPinkB = 0.5f;
        private bool _fgOrangeOn;
        private float _fgOrangeR = 1f, _fgOrangeG = 0.55f, _fgOrangeB = 0.1f;

        private Button _btnCyanOn, _btnBlackOn, _btnYellowOn, _btnBlueOn, _btnPinkOn, _btnOrangeOn;
        private Image _swatchCyan, _swatchBlack, _swatchYellow, _swatchBlue, _swatchPink, _swatchOrange;
        private Image _fgCyanAreaBg, _fgBlackAreaBg, _fgYellowAreaBg, _fgBlueAreaBg, _fgPinkAreaBg, _fgOrangeAreaBg;

        private void SetAllCustomEnabled(bool on)
        {
            _fgCyanOn = _fgBlackOn = _fgYellowOn = _fgBlueOn = _fgPinkOn = _fgOrangeOn = on;
            SetToggle(_btnCyanOn, on);
            SetToggle(_btnBlackOn, on);
            SetToggle(_btnYellowOn, on);
            SetToggle(_btnBlueOn, on);
            SetToggle(_btnPinkOn, on);
            SetToggle(_btnOrangeOn, on);
        }

        private void BuildFgPanel(RectTransform parent, float x, float y, float w, float h)
        {
            float sectionH = LH + SH + BTN_H + SH + (LH + SH) * 2f + LH;

            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;
            float swatchW = BTN_H;
            float toggleW = BTN_H * 2.2f;
            float slidersW = w - swatchW - toggleW - PAD * 2f;
            float fullSliderW = slidersW + toggleW + swatchW + PAD;

            float cyanStart = cy;
            var cyanBgGo = new GameObject("CyanAreaBg");
            cyanBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(cyanBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, cyanStart - 3f, w + 6f, sectionH + 6f));
            _fgCyanAreaBg = cyanBgGo.AddComponent<Image>();
            _fgCyanAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgCyanAreaBg.type = Image.Type.Simple;
            _fgCyanAreaBg.color = new Color(_fgCyanR, _fgCyanG, _fgCyanB, 0.18f);
            _fgCyanAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.cyan_replacement", FS_SM, HINT);
            cy += LH + SH;

            _btnCyanOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgCyanOn ? "ui.on" : "ui.off", _fgCyanOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgCyanOn = !_fgCyanOn;
                    var lbl = _btnCyanOn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _fgCyanOn ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_btnCyanOn, _fgCyanOn, SEL_COLOR);
                }));

            var swatchCyanGo = new GameObject("SwatchCyan");
            swatchCyanGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchCyanGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchCyan = swatchCyanGo.AddComponent<Image>();
            _swatchCyan.color = new Color(_fgCyanR, _fgCyanG, _fgCyanB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgCyanR, () => _fgCyanG, () => _fgCyanB,
                v => _fgCyanR = v, v => _fgCyanG = v, v => _fgCyanB = v, () => SyncCyan(), out _, out _, out _,
                new Color(0f, 0.3f, 1f));

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float blackStart = cy;
            var blackBgGo = new GameObject("BlackAreaBg");
            blackBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(blackBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, blackStart - 3f, w + 6f, sectionH + 6f));
            _fgBlackAreaBg = blackBgGo.AddComponent<Image>();
            _fgBlackAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgBlackAreaBg.type = Image.Type.Simple;
            _fgBlackAreaBg.color = new Color(_fgBlackR, _fgBlackG, _fgBlackB, 0.18f);
            _fgBlackAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.black_replacement", FS_SM, HINT);
            cy += LH + SH;

            _btnBlackOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgBlackOn ? "ui.on" : "ui.off", _fgBlackOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgBlackOn = !_fgBlackOn;
                    var lbl = _btnBlackOn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _fgBlackOn ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_btnBlackOn, _fgBlackOn, SEL_COLOR);
                }));

            var swatchBlackGo = new GameObject("SwatchBlack");
            swatchBlackGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchBlackGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchBlack = swatchBlackGo.AddComponent<Image>();
            _swatchBlack.color = new Color(_fgBlackR, _fgBlackG, _fgBlackB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgBlackR, () => _fgBlackG, () => _fgBlackB,
                v => _fgBlackR = v, v => _fgBlackG = v, v => _fgBlackB = v, () => SyncBlack(), out _, out _, out _,
                new Color(0.75f, 0.75f, 0.75f));

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float yellowStart = cy;
            var yellowBgGo = new GameObject("YellowAreaBg");
            yellowBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(yellowBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, yellowStart - 3f, w + 6f, sectionH + 6f));
            _fgYellowAreaBg = yellowBgGo.AddComponent<Image>();
            _fgYellowAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgYellowAreaBg.type = Image.Type.Simple;
            _fgYellowAreaBg.color = new Color(_fgYellowR, _fgYellowG, _fgYellowB, 0.18f);
            _fgYellowAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.yellow_replacement", FS_SM, HINT);
            cy += LH + SH;

            _btnYellowOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgYellowOn ? "ui.on" : "ui.off", _fgYellowOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgYellowOn = !_fgYellowOn;
                    var lbl = _btnYellowOn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _fgYellowOn ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_btnYellowOn, _fgYellowOn, SEL_COLOR);
                }));

            var swatchYellowGo = new GameObject("SwatchYellow");
            swatchYellowGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchYellowGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchYellow = swatchYellowGo.AddComponent<Image>();
            _swatchYellow.color = new Color(_fgYellowR, _fgYellowG, _fgYellowB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgYellowR, () => _fgYellowG, () => _fgYellowB,
                v => _fgYellowR = v, v => _fgYellowG = v, v => _fgYellowB = v, () => SyncYellow(), out _, out _, out _,
                new Color(1f, 0.5f, 0f));

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float blueStart = cy;
            var blueBgGo = new GameObject("BlueAreaBg");
            blueBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(blueBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, blueStart - 3f, w + 6f, sectionH + 6f));
            _fgBlueAreaBg = blueBgGo.AddComponent<Image>();
            _fgBlueAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgBlueAreaBg.type = Image.Type.Simple;
            _fgBlueAreaBg.color = new Color(_fgBlueR, _fgBlueG, _fgBlueB, 0.18f);
            _fgBlueAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.blue_replacement", FS_SM, HINT);
            cy += LH + SH;

            _btnBlueOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgBlueOn ? "ui.on" : "ui.off", _fgBlueOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgBlueOn = !_fgBlueOn;
                    var lbl = _btnBlueOn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _fgBlueOn ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_btnBlueOn, _fgBlueOn, SEL_COLOR);
                }));

            var swatchBlueGo = new GameObject("SwatchBlue");
            swatchBlueGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchBlueGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchBlue = swatchBlueGo.AddComponent<Image>();
            _swatchBlue.color = new Color(_fgBlueR, _fgBlueG, _fgBlueB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgBlueR, () => _fgBlueG, () => _fgBlueB,
                v => _fgBlueR = v, v => _fgBlueG = v, v => _fgBlueB = v, () => SyncBlue(), out _, out _, out _,
                new Color(0.1f, 0.25f, 0.85f));

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float pinkStart = cy;
            var pinkBgGo = new GameObject("PinkAreaBg");
            pinkBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(pinkBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, pinkStart - 3f, w + 6f, sectionH + 6f));
            _fgPinkAreaBg = pinkBgGo.AddComponent<Image>();
            _fgPinkAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgPinkAreaBg.type = Image.Type.Simple;
            _fgPinkAreaBg.color = new Color(_fgPinkR, _fgPinkG, _fgPinkB, 0.18f);
            _fgPinkAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.pink_replacement", FS_SM, HINT);
            cy += LH + SH;

            _btnPinkOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgPinkOn ? "ui.on" : "ui.off", _fgPinkOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgPinkOn = !_fgPinkOn;
                    var lbl = _btnPinkOn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _fgPinkOn ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_btnPinkOn, _fgPinkOn, SEL_COLOR);
                }));

            var swatchPinkGo = new GameObject("SwatchPink");
            swatchPinkGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchPinkGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchPink = swatchPinkGo.AddComponent<Image>();
            _swatchPink.color = new Color(_fgPinkR, _fgPinkG, _fgPinkB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgPinkR, () => _fgPinkG, () => _fgPinkB,
                v => _fgPinkR = v, v => _fgPinkG = v, v => _fgPinkB = v, () => SyncPink(), out _, out _, out _,
                new Color(1f, 0.2f, 0.5f));

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float orangeStart = cy;
            var orangeBgGo = new GameObject("OrangeAreaBg");
            orangeBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(orangeBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, orangeStart - 3f, w + 6f, sectionH + 6f));
            _fgOrangeAreaBg = orangeBgGo.AddComponent<Image>();
            _fgOrangeAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgOrangeAreaBg.type = Image.Type.Simple;
            _fgOrangeAreaBg.color = new Color(_fgOrangeR, _fgOrangeG, _fgOrangeB, 0.18f);
            _fgOrangeAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ui.orange_replacement", FS_SM, HINT);
            cy += LH + SH;

            _btnOrangeOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgOrangeOn ? "ui.on" : "ui.off", _fgOrangeOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgOrangeOn = !_fgOrangeOn;
                    var lbl = _btnOrangeOn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _fgOrangeOn ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_btnOrangeOn, _fgOrangeOn, SEL_COLOR);
                }));

            var swatchOrangeGo = new GameObject("SwatchOrange");
            swatchOrangeGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchOrangeGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchOrange = swatchOrangeGo.AddComponent<Image>();
            _swatchOrange.color = new Color(_fgOrangeR, _fgOrangeG, _fgOrangeB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgOrangeR, () => _fgOrangeG, () => _fgOrangeB,
                v => _fgOrangeR = v, v => _fgOrangeG = v, v => _fgOrangeB = v, () => SyncOrange(), out _, out _, out _,
                new Color(1f, 0.55f, 0.1f));

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void SyncOrange()
        {
            if (_swatchOrange != null) _swatchOrange.color = new Color(_fgOrangeR, _fgOrangeG, _fgOrangeB);
            if (_fgOrangeAreaBg != null) _fgOrangeAreaBg.color = new Color(_fgOrangeR, _fgOrangeG, _fgOrangeB, 0.18f);
        }

        private void SyncPink()
        {
            if (_swatchPink != null) _swatchPink.color = new Color(_fgPinkR, _fgPinkG, _fgPinkB);
            if (_fgPinkAreaBg != null) _fgPinkAreaBg.color = new Color(_fgPinkR, _fgPinkG, _fgPinkB, 0.18f);
        }

        private void SyncCyan()
        {
            if (_swatchCyan != null) _swatchCyan.color = new Color(_fgCyanR, _fgCyanG, _fgCyanB);
            if (_fgCyanAreaBg != null) _fgCyanAreaBg.color = new Color(_fgCyanR, _fgCyanG, _fgCyanB, 0.18f);
        }

        private void SyncBlack()
        {
            if (_swatchBlack != null) _swatchBlack.color = new Color(_fgBlackR, _fgBlackG, _fgBlackB);
            if (_fgBlackAreaBg != null) _fgBlackAreaBg.color = new Color(_fgBlackR, _fgBlackG, _fgBlackB, 0.18f);
        }

        private void SyncYellow()
        {
            if (_swatchYellow != null) _swatchYellow.color = new Color(_fgYellowR, _fgYellowG, _fgYellowB);
            if (_fgYellowAreaBg != null) _fgYellowAreaBg.color = new Color(_fgYellowR, _fgYellowG, _fgYellowB, 0.18f);
        }

        private void SyncBlue()
        {
            if (_swatchBlue != null) _swatchBlue.color = new Color(_fgBlueR, _fgBlueG, _fgBlueB);
            if (_fgBlueAreaBg != null) _fgBlueAreaBg.color = new Color(_fgBlueR, _fgBlueG, _fgBlueB, 0.18f);
        }

        private void OnApply()
        {
            SaveSettings();
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings();
            MenuCustomizationApplication.Instance?.ReapplyBakedPinkGreyTextures();
            MenuCustomizationApplication.Instance?.ReapplyCreativeBgLive();
            MenuCustomizationApplication.Instance?.ReapplyLevelBrowserTilesLive();
            BetterFG.Features.QualificationTime.FeatureQualificationTime.ReapplyTimerColors();
        }

        private void LoadSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)),
                    System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            _fgCyanOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_CYAN_ON, "false") == "true";
            _fgCyanR = P(MenuCustomizationApplication.KEY_FG_CYAN_R, 0f);
            _fgCyanG = P(MenuCustomizationApplication.KEY_FG_CYAN_G, 0.3f);
            _fgCyanB = P(MenuCustomizationApplication.KEY_FG_CYAN_B, 1f);
            _fgBlackOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_BLACK_ON, "false") == "true";
            _fgBlackR = P(MenuCustomizationApplication.KEY_FG_BLACK_R, 0.75f);
            _fgBlackG = P(MenuCustomizationApplication.KEY_FG_BLACK_G, 0.75f);
            _fgBlackB = P(MenuCustomizationApplication.KEY_FG_BLACK_B, 0.75f);
            _fgYellowOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_YELLOW_ON, "false") == "true";
            _fgYellowR = P(MenuCustomizationApplication.KEY_FG_YELLOW_R, 1f);
            _fgYellowG = P(MenuCustomizationApplication.KEY_FG_YELLOW_G, 0.5f);
            _fgYellowB = P(MenuCustomizationApplication.KEY_FG_YELLOW_B, 0f);
            _fgBlueOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_BLUE_ON, "false") == "true";
            _fgBlueR = P(MenuCustomizationApplication.KEY_FG_BLUE_R, 0.1f);
            _fgBlueG = P(MenuCustomizationApplication.KEY_FG_BLUE_G, 0.25f);
            _fgBlueB = P(MenuCustomizationApplication.KEY_FG_BLUE_B, 0.85f);
            _fgPinkOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_PINK_ON, "false") == "true";
            _fgPinkR = P(MenuCustomizationApplication.KEY_FG_PINK_R, 1f);
            _fgPinkG = P(MenuCustomizationApplication.KEY_FG_PINK_G, 0.2f);
            _fgPinkB = P(MenuCustomizationApplication.KEY_FG_PINK_B, 0.5f);
            _fgOrangeOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_ORANGE_ON, "false") == "true";
            _fgOrangeR = P(MenuCustomizationApplication.KEY_FG_ORANGE_R, 1f);
            _fgOrangeG = P(MenuCustomizationApplication.KEY_FG_ORANGE_G, 0.55f);
            _fgOrangeB = P(MenuCustomizationApplication.KEY_FG_ORANGE_B, 0.1f);
        }

        private void SaveSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void S(string k, float v) => SettingsService.Set(k, v.ToString(ci));

            SettingsService.Set(MenuCustomizationApplication.KEY_FG_CYAN_ON, _fgCyanOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_CYAN_R, _fgCyanR);
            S(MenuCustomizationApplication.KEY_FG_CYAN_G, _fgCyanG);
            S(MenuCustomizationApplication.KEY_FG_CYAN_B, _fgCyanB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_BLACK_ON, _fgBlackOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_BLACK_R, _fgBlackR);
            S(MenuCustomizationApplication.KEY_FG_BLACK_G, _fgBlackG);
            S(MenuCustomizationApplication.KEY_FG_BLACK_B, _fgBlackB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_YELLOW_ON, _fgYellowOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_YELLOW_R, _fgYellowR);
            S(MenuCustomizationApplication.KEY_FG_YELLOW_G, _fgYellowG);
            S(MenuCustomizationApplication.KEY_FG_YELLOW_B, _fgYellowB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_BLUE_ON, _fgBlueOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_BLUE_R, _fgBlueR);
            S(MenuCustomizationApplication.KEY_FG_BLUE_G, _fgBlueG);
            S(MenuCustomizationApplication.KEY_FG_BLUE_B, _fgBlueB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_PINK_ON, _fgPinkOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_PINK_R, _fgPinkR);
            S(MenuCustomizationApplication.KEY_FG_PINK_G, _fgPinkG);
            S(MenuCustomizationApplication.KEY_FG_PINK_B, _fgPinkB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_ORANGE_ON, _fgOrangeOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_ORANGE_R, _fgOrangeR);
            S(MenuCustomizationApplication.KEY_FG_ORANGE_G, _fgOrangeG);
            S(MenuCustomizationApplication.KEY_FG_ORANGE_B, _fgOrangeB);
        }
    }
}
