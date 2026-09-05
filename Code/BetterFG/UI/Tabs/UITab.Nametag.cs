using System;
using BetterFG.Nametag;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public partial class UITab
    {
        private bool _ntHideArrow;
        private bool _ntHidePlatform;
        private float _ntScale = 1f;
        private Button _ntArrowBtn;
        private Button _ntHidePlatformBtn;
        private Slider _ntScaleSlider;
        private Text _ntScaleValue;
        private readonly NametagPreview _ntPreview = new NametagPreview();

        private void SetAllNametagEnabled(bool on)
        {
            _ntHideArrow = on;
            _ntHidePlatform = on;
            _ntScale = 1f;
            SetToggle(_ntArrowBtn, on);
            SetToggle(_ntHidePlatformBtn, on);
            if (_ntScaleSlider != null) _ntScaleSlider.value = 1f;
            if (_ntScaleValue != null) _ntScaleValue.text = "1.00x";
            NametagIconApplicator.SetPlatformHideEveryone(on);
        }

        private void BuildNametagPanel(RectTransform parent, float x, float y, float w, float h)
        {
            float cy = y + PAD;
            float previewH = h - PAD * 5f - BTN_H * 3f - LH * 3f - SH * 3f;
            if (previewH < 120f) previewH = Mathf.Max(80f, h * 0.5f);

            _ntPreview.Build(parent, new Rect(x, cy, w, previewH), RefreshNametagPreview);
            cy += previewH + PAD;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, w, LH), "nametag_all.remove_arrow", FS_SM, HINT);
            cy += LH + SH;

            float toggleW = BTN_H * 2.2f;
            _ntArrowBtn = UGUIShip.CreateButton(parent, new Rect(x, cy, toggleW, BTN_H),
                _ntHideArrow ? "ui.on" : "ui.off", _ntHideArrow ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _ntHideArrow = !_ntHideArrow;
                    var lbl = _ntArrowBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _ntHideArrow ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_ntArrowBtn, _ntHideArrow, SEL_COLOR);
                    OnNametagApply();
                    RefreshNametagPreview();
                }));
            cy += BTN_H + PAD;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, w, LH), "nametag_all.hide_platform_icons", FS_SM, HINT);
            cy += LH + SH;

            _ntHidePlatformBtn = UGUIShip.CreateButton(parent, new Rect(x, cy, toggleW, BTN_H),
                _ntHidePlatform ? "ui.on" : "ui.off", _ntHidePlatform ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _ntHidePlatform = !_ntHidePlatform;
                    var lbl = _ntHidePlatformBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) UGUIShip.RelabelText(lbl, _ntHidePlatform ? "ui.on" : "ui.off");
                    UGUIShip.SetButtonSelected(_ntHidePlatformBtn, _ntHidePlatform, SEL_COLOR);
                    NametagIconApplicator.SetPlatformHideEveryone(_ntHidePlatform);
                    RefreshNametagPreview();
                }));
            cy += BTN_H + PAD;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, w, LH), "nametag_all.scale", FS_SM, HINT);
            cy += LH + SH;

            float valW = BTN_H * 2f;
            _ntScaleValue = UGUIShip.CreateLabel(parent, new Rect(x + w - valW, cy, valW, BTN_H),
                _ntScale.ToString("0.00") + "x", FS_SM, WHITE, TextAnchor.MiddleRight);
            _ntScaleSlider = UGUIShip.CreateSlider(parent, x, cy, w - valW - PAD, "", _ntScale, BTN_H, PAD, FS_SM,
                v =>
                {
                    _ntScale = v;
                    if (_ntScaleValue != null) _ntScaleValue.text = v.ToString("0.00") + "x";
                    OnNametagApply();
                },
                reserveLabel: false, resetTo: 1f);
            _ntScaleSlider.minValue = 0.25f;
            _ntScaleSlider.maxValue = 3f;
            _ntScaleSlider.value = _ntScale;
        }

        private void RefreshNametagPreview()
        {
            var nameCfg = NametagIconApplicator.CfgFromSettings();
            nameCfg.enabled = true;
            var crownCfg = CrownRankService.CfgFromSettings();
            crownCfg.enabled = true;
            _ntPreview.Apply(nameCfg, crownCfg,
                NametagIconApplicator.PlatformHideFromSettings(),
                NametagIconApplicator.PlatformCustomFromSettings());
            var canvas = _ntPreview.Canvas;
            if (canvas != null)
            {
                canvas.SetCrownRankByCrownsEarned(100000);
                if (canvas._arrowImage != null) canvas._arrowImage.gameObject.SetActive(!_ntHideArrow);
            }
        }

        private void LoadNametagSettings()
        {
            _ntHideArrow = SettingsService.Get(NametagAllPlayersService.KEY_HIDE_ARROW, "false") == "true";
            _ntHidePlatform = NametagIconApplicator.PlatformHideEveryoneFromSettings();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            _ntScale = float.TryParse(SettingsService.Get(NametagAllPlayersService.KEY_SCALE, "1"),
                System.Globalization.NumberStyles.Float, ci, out float s) ? s : 1f;
        }

        private void OnNametagApply()
        {
            SettingsService.Set(NametagAllPlayersService.KEY_HIDE_ARROW, _ntHideArrow ? "true" : "false");
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(NametagAllPlayersService.KEY_SCALE, _ntScale.ToString(ci));
            NametagAllPlayersService.Invalidate();
        }
    }
}
