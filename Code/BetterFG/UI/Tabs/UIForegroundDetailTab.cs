using System;
using BetterFG.Customization.Menu;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public enum UIForegroundKind { CustomUI, Qualified, Eliminated, EliminatedSquad, Winner, RoundOver }

    public partial class UIForegroundDetailTab : UISubTab
    {
        public UIForegroundDetailTab(IntPtr ptr) : base(ptr) { }

        public UIForegroundKind What;

        public override string TabTitle => "UI - " + Label(What) + Label(What);

        public static string Label(UIForegroundKind k)
        {
            switch (k)
            {
                case UIForegroundKind.Qualified: return "Qualified banner";
                case UIForegroundKind.Eliminated: return "Eliminated banner";
                case UIForegroundKind.EliminatedSquad: return "Squad eliminated banner";
                case UIForegroundKind.Winner: return "Winner banner";
                case UIForegroundKind.RoundOver: return "Round over banner";
                default: return "Custom UI colours";
            }
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = UITab.BuildSectionBar(this, contentRoot, PAD, VPAD, w, "Foreground");

            float btnRowH = BTN_H + PAD * 2f + 1f;
            float bodyH = TabHeight - y - VPAD - btnRowH;

            var bodyGo = new GameObject("FgBody");
            bodyGo.transform.SetParent(contentRoot, false);
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(bodyRt, new Rect(0f, y, TabWidth, bodyH));

            if (What == UIForegroundKind.CustomUI)
            {
                LoadSettings();
                BuildFgPanel(bodyRt, PAD, 0f, w, bodyH);
            }
            else
            {
                BuildBannerPanel(bodyRt, PAD, 0f, w, bodyH, What);
                RefreshBannerPreview();
            }

            float by = y + bodyH + PAD;
            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, by, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            by += 1f + PAD;
            float btnw = (w - PAD) / 3f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, by, btnw, BTN_H),
                "ui.apply", BTN_APPLY, WHITE, FS, new Action(OnApplyClicked));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + btnw + PAD * 0.5f, by, btnw, BTN_H),
                "ui.enable_all_2", BTN_ON, WHITE, FS_SM, new Action(() => SetAllEnabled(true)));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + (btnw + PAD * 0.5f) * 2f, by, btnw, BTN_H),
                "ui.disable_all_2", BTN_REMOVE, WHITE, FS_SM, new Action(() => SetAllEnabled(false)));

            PositionSwitchLink();
        }

        private void OnApplyClicked()
        {
            if (What == UIForegroundKind.CustomUI) OnApply();
            else OnBannerApply(What);
        }

        private void SetAllEnabled(bool on)
        {
            if (What == UIForegroundKind.CustomUI) { SetAllCustomEnabled(on); OnApply(); return; }

            var def = GetBannerDef(What);
            if (def == null) return;
            def.enabled = on;
            SettingsService.Set(def.enabledKey, on ? "true" : "false");
            var elbl = def.enabledBtn?.GetComponentInChildren<Text>();
            if (elbl != null) UGUIShip.RelabelText(elbl, on ? "ui.custom_colours_on_2" : "ui.custom_colours_off_2");
            UGUIShip.SetButtonSelected(def.enabledBtn, on, SEL_COLOR);
            foreach (var s in def.slots) SetBannerToggle(s.ui, on);
            SetBannerToggle(def.highlight.ui, on);
            UpdateBannerPreviewColours();
            OnBannerApply(What);
        }

        private static void SetToggle(Button btn, bool on)
        {
            var lbl = btn?.GetComponentInChildren<Text>();
            if (lbl != null) UGUIShip.RelabelText(lbl, on ? "ui.on" : "ui.off");
            UGUIShip.SetButtonSelected(btn, on, SEL_COLOR);
        }
    }
}
