using System;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class UITab : Tab
    {
        public UITab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "User Interface";
        protected override string TitleId => "ui.user_interface_2";
        protected override string BgResource => "BetterFG.assets.ui.tab.ui.png";

        internal static float subTabH => BTN_H * 0.9f;

        internal static readonly Color WHITE = UGUIShip.WHITE;
        internal static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        internal static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);

        static readonly (string label, Func<Tab> make)[] Sections =
        {
            ("Foreground", () => BetterFGTabRegistry.NewTab<UITab>()),
            ("Font", () => BetterFGTabRegistry.NewTab<UIFontTab>()),
            ("Background", () => BetterFGTabRegistry.NewTab<UIBackgroundTab>()),
            ("Scaling", () => BetterFGTabRegistry.NewTab<UIScalingTab>()),
        };

        // shared subtab bar (Foreground/Font/Background/Scaling) drawn at the top of every UI section
        // tab; each button switches the slot to that section's own tab. `current` is highlighted.
        internal static float BuildSectionBar(Tab from, RectTransform parent, float x, float y, float w, string current)
        {
            float quarterTab = (w - PAD * 0.5f * 3f) / 4f;
            float qGap = PAD * 0.5f;
            for (int i = 0; i < Sections.Length; i++)
            {
                var (label, make) = Sections[i];
                UGUIShip.CreateButton(parent, new Rect(x + (quarterTab + qGap) * i, y, quarterTab, subTabH), label,
                    label == current ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                    new Action(() => BetterFGUIMan.Instance?.SwitchSlotTab(from, make())));
            }
            y += subTabH + SH;
            UGUIShip.CreatePanel(parent, new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            return y + 1f + SH;
        }

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private const float FG_PREVIEW_H = 120f;

        private readonly BannerPreviewClone _fgPreview = new BannerPreviewClone();
        private Transform _fgViewport;
        private Text _fgCarouselLabel;
        private Text _fgNoPreviewLabel;
        private int _fgIndex;

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float cy = BuildSectionBar(this, contentRoot, PAD, VPAD, w, "Foreground");

            // ── carousel header: ‹  Qualified banner  › ──
            float arrow = subTabH;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, cy, arrow, BTN_H),
                "<", BTN_DARK, WHITE, FS_SM, new Action(() => CycleForeground(-1)));
            _fgCarouselLabel = UGUIShip.CreateLabel(contentRoot, new Rect(PAD + arrow, cy, w - arrow * 2f, BTN_H),
                UIForegroundDetailTab.Label((UIForegroundKind)_fgIndex), FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - arrow, cy, arrow, BTN_H),
                ">", BTN_DARK, WHITE, FS_SM, new Action(() => CycleForeground(1)));
            cy += BTN_H + SH;

            var (scrollRect, _) = UGUIShip.CreateScrollView(contentRoot, new Rect(0f, cy, TabWidth, FG_PREVIEW_H));
            scrollRect.vertical = false;
            _fgViewport = scrollRect.transform.Find("Viewport");

            _fgNoPreviewLabel = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, cy, w, FG_PREVIEW_H),
                "ui.no_live_preview_for_custom_ui_colours", FS_SM, HINT, TextAnchor.MiddleCenter);
            cy += FG_PREVIEW_H + SH;

            UGUIShip.CreateButton(contentRoot, new Rect(PAD, cy, w, BTN_H),
                "ui.edit", BTN_DARK, WHITE, FS_SM, new Action(() => OpenForeground((UIForegroundKind)_fgIndex)));

            RefreshForegroundPreview();
        }

        void CycleForeground(int d)
        {
            _fgIndex = (_fgIndex + d + 6) % 6;
            if (_fgCarouselLabel != null) UGUIShip.RelabelText(_fgCarouselLabel, UIForegroundDetailTab.Label((UIForegroundKind)_fgIndex));
            RefreshForegroundPreview();
        }

        void RefreshForegroundPreview()
        {
            var kind = (UIForegroundKind)_fgIndex;
            _fgPreview.Refresh(this, kind, _fgViewport,
                new Vector3(205.4f, -44.6501f, 0f), new Vector3(0.8236f, 0.8236f, 0.6f),
                () => Customization.Menu.MenuCustomizationApplication.Instance.GetBannerColoursFromSettings(ToBannerScreen(kind)));
            if (_fgNoPreviewLabel != null) _fgNoPreviewLabel.gameObject.SetActive(kind == UIForegroundKind.CustomUI);
        }

        static Customization.Menu.MenuCustomizationApplication.BannerScreen ToBannerScreen(UIForegroundKind k) => k switch
        {
            UIForegroundKind.Qualified => Customization.Menu.MenuCustomizationApplication.BannerScreen.Qualified,
            UIForegroundKind.Eliminated => Customization.Menu.MenuCustomizationApplication.BannerScreen.Eliminated,
            UIForegroundKind.EliminatedSquad => Customization.Menu.MenuCustomizationApplication.BannerScreen.Squad,
            UIForegroundKind.Winner => Customization.Menu.MenuCustomizationApplication.BannerScreen.Winner,
            UIForegroundKind.RoundOver => Customization.Menu.MenuCustomizationApplication.BannerScreen.RoundOver,
            _ => Customization.Menu.MenuCustomizationApplication.BannerScreen.Qualified,
        };

        void OpenForeground(UIForegroundKind kind)
        {
            var t = BetterFGTabRegistry.NewTab<UIForegroundDetailTab>();
            t.What = kind;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, t);
        }

        // MenuTab's "take me there" link jumps straight to the Background tab
        public void OpenBackground() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.NewTab<UIBackgroundTab>());
    }
}
