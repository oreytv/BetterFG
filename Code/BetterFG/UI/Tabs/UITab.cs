using System;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tabs
{
    public class UITab : TexturedTab
    {
        public UITab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "User Interface";
        protected override string BgResource => "BetterFG.assets.ui.tab.ui.png";

        internal static float PAD => UIScale.PAD;
        internal static float VPAD => UIScale.VPAD;
        internal static float SH => UIScale.SH;
        internal static float BTN_H => UIScale.BTN_H;
        internal static int FS_SM => UIScale.FS_SM;
        internal static float subTabH => BTN_H * 0.9f;

        internal static readonly Color WHITE = Color.white;
        internal static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
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

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float cy = BuildSectionBar(this, contentRoot, PAD, VPAD, w, "Foreground");

            for (int i = 0; i < 6; i++)
            {
                var kind = (UIForegroundKind)i;
                UGUIShip.CreateButton(contentRoot, new Rect(PAD, cy, w, BTN_H), UIForegroundDetailTab.Label(kind),
                    BTN_DARK, WHITE, FS_SM, new Action(() => OpenForeground(kind)));
                cy += BTN_H + SH;
            }
        }

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
