using System;
using BetterFG.Services;
using UnityEngine;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class UISubTab : SwitchTab
    {
        public UISubTab(IntPtr ptr) : base(ptr) { }

        protected override string BgResource => "BetterFG.assets.ui.tab.ui.png";
        protected override string SwitchLabel => "‹ back";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<UITab>();

        protected static float dropdownRowH => BTN_H * 0.8f;

        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        protected static readonly Color WHITE = UGUIShip.WHITE;
        protected static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        protected static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        protected static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        protected static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        protected static readonly Color BTN_ON = new Color(0.25f, 0.5f, 0.25f, 1f);
    }
}
