using System;
using UnityEngine;
using UnityEngine.UI;
using Rewired;
using FGClient;
using FG.Common.UI;
using BetterFG.UI;
using BetterFG.UI.Tabs;

namespace BetterFG.Features.QualificationTime
{
    // the PB top-bar tab. NOT a modal popup and NOT a real SwitchableView view (the game's view
    // machinery is a native index->enum map we can't extend). our clone tab sits at the end of the
    // Rewired nav's _menuTabs; landing on it positionally lands on view 6 (Settings), which the nav
    // patch catches. we then navigate to the Customiser view for the scene backdrop (same as the old
    // popup did) and drop our content on top. lives on the game canvas so it ignores the UI scale.
    internal static class PBTabView
    {
        public static bool IsOpen => _root != null || _opening;
        static GameObject _root;
        // Show() navigates to the Customiser backdrop first, which pushes a presence update before our
        // root exists — without this the RPC flashes "Customising their bean" for a beat.
        static bool _opening;
        static int _svIdxAtHide = -1;

        // position of our clone in the nav's _menuTabs (the last slot).
        public static int MenuTabIndex = -1;
        public static Toggle TabToggle;
        // the SwitchableView index we consider "still on our tab" (== MenuTabIndex). a switch to any
        // OTHER index means the user navigated off us, so drop the overlay.
        public static int BackdropIndex = -1;
        static FGClient.Customiser.CustomiserScreenViewModel _custVm;
        static MenuInputHandler _custHandler;
        static GameObject _custUiGo;
        // the SwitchableView + the REAL index its Customiser view sits at. we lie that current is our
        // slot for nav, so SetView never tears Customiser down — we deactivate it ourselves on leave.
        static SwitchableView _sv;
        static int _custViewIdx = -1;

        // the Rewired nav moved to a tab. landing on our slot shows the PB tab. leaving is handled by
        // the clone toggle going off (see the onValueChanged wiring), so we don't hide from here.
        public static void OnNavIndex(SwitchableView sv, int index)
        {
            if (MenuTabIndex < 0) return;
            if (index == MenuTabIndex) Show();
        }

        public static void Show()
        {
            if (_root != null) return;
            _opening = true;

            // Customiser view for its 3D scene backdrop, like the old popup. that leaves CurrentViewIndex
            // pointing at Customiser though, so the pad would navigate left/right around Customiser and
            // the toggle handler would light Customiser's tab. write the index back to OUR slot (the
            // last one) so nav treats us as the last tab and the handler lights no stock toggle (there's
            // none at our index) — leaving it ENABLED so a mouse click on another tab still fires SetView.
            var mmm = GameObject.Find("MainMenuManager")?.GetComponent<MainMenuManager>();
            if (mmm != null) mmm.NavigateToView(MainMenuViews.Customiser);
            _sv = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)")?.GetComponent<SwitchableView>();
            if (_sv != null)
            {
                _custViewIdx = _sv.CurrentViewIndex; // real Customiser view index, before we overwrite it
                if (MenuTabIndex >= 0) _sv._currentViewIndex = MenuTabIndex;
            }
            BackdropIndex = MenuTabIndex;

            // the Customiser backdrop screen VM keeps focus, and its MenuInputHandler reads the stick
            // (scrolling cosmetics under our overlay). just calling OnLoseFocus doesn't hold — alt-tab
            // and screen changes re-assert focus. instead pull the handler off the VM's input list so
            // even a re-focused VM has nothing to drive; we hand it back on Hide.
            var custScreen = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Customiser");
            var custUi = custScreen?.transform.Find("Prime_UI_Customizer_Prefab_Canvas(Clone)");
            _custVm = custUi?.GetComponent<FGClient.Customiser.CustomiserScreenViewModel>();
            _custHandler = custScreen?.GetComponent<MenuInputHandler>();
            if (_custVm != null && _custHandler != null) _custVm.RemoveInputHandler(_custHandler);
            // the Customiser view is only here for its 3D scene backdrop — its cosmetics UI panels
            // shouldn't be seen, navigated, or entered under our tab. deactivate the whole UI canvas;
            // the bean/camera live in the scene, not under it, so the backdrop stays.
            _custUiGo = custUi?.gameObject;
            if (_custUiGo != null) _custUiGo.SetActive(false);

            // our tab now owns the screen — drop the custom menu background image too.
            BetterFG.Customization.Menu.MenuCustomizationApplication.Instance?.RefreshImageBgVisibility();

            // our OWN screen-space canvas. scaling text crisply means driving the canvas scale, not a
            // transform localScale (which just magnifies the baked glyph atlas -> pixelated). a smaller
            // reference resolution renders everything ~1.9x bigger and re-bakes the font sharp. it's a
            // standalone canvas we don't register with UIScaleService, so the UI-scale slider leaves it
            // alone. no full-screen graphic, so the top bar underneath still takes clicks/nav.
            const float scale = 1.9f;
            _root = new GameObject("PB_TabView");
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f) / scale;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var hostGo = new GameObject("Host");
            hostGo.transform.SetParent(_root.transform, false);
            var hostRt = hostGo.AddComponent<RectTransform>();
            hostRt.anchorMin = hostRt.anchorMax = hostRt.pivot = new Vector2(0.5f, 0.5f);
            hostRt.sizeDelta = new Vector2(UIScale.TAB_W, UIScale.TAB_CONTENT_H);
            hostRt.localPosition = new Vector3(137.7327f, -64.0136f, 0f);

            var tab = BetterFGTabRegistry.NewTab<PersonalBestTab>();
            tab.transform.SetParent(hostRt, false);
            var tabRt = tab.gameObject.GetComponent<RectTransform>() ?? tab.gameObject.AddComponent<RectTransform>();
            tabRt.anchorMin = Vector2.zero;
            tabRt.anchorMax = Vector2.one;
            tabRt.offsetMin = tabRt.offsetMax = Vector2.zero;

            tab.TabWidth = UIScale.TAB_W;
            tab.TabHeight = UIScale.TAB_CONTENT_H;
            tab.Initialize(tabRt);
            tab.EnsureBuilt();
            tab.IsOpen = true;
            tab.SetContentActive(true);
            tab.OnOpened();

            var titleBar = tab.transform.Find("Content/TitleBar");
            if (titleBar != null) titleBar.gameObject.SetActive(false);
            var contentRt = tab.transform.Find("Content/ContentArea")?.GetComponent<RectTransform>();
            if (contentRt != null) contentRt.offsetMax = Vector2.zero;

            var bg = tab.transform.Find("BG");
            if (bg != null) bg.localPosition = new Vector3(54.1514f, 71.8456f, 0f);

            // our clone is in the tab ToggleGroup; turning it on makes it the sole lit tab. the handler
            // keeps the stock toggles off since none sits at our index.
            if (TabToggle != null) TabToggle.isOn = true;
            _opening = false;
            Plugin.Log.LogInfo("navprobe PB show done");
            BetterFG.Services.DiscordPresenceService.Push();
        }

        public static void Hide()
        {
            if (_root == null) return;
            _svIdxAtHide = _sv != null ? _sv.CurrentViewIndex : -1;
            UnityEngine.Object.Destroy(_root);
            _root = null;
            _opening = false;

            // BetterFGUIMan force-frees the cursor every frame while we're open; once that stops the
            // cursor just freezes wherever it was. if we left on a pad, hide+lock it (nobody else does);
            // for kbm leave it visible. skip when our full UI is up and already owns the cursor.
            bool leftOnPad = ReInput.isReady
                && ReInput.controllers.GetLastActiveController()?.type == ControllerType.Joystick;
            if (leftOnPad && (BetterFGUIMan.Instance == null || !BetterFGUIMan.Instance.IsVisible))
                BetterFGUIMan.Instance?.SetCursorFree(false);

            // put the customiser UI + its input handler back so it works normally next time.
            if (_custUiGo != null) _custUiGo.SetActive(true);
            if (_custVm != null && _custHandler != null) _custVm.AddInputHandler(_custHandler);

            // we lied that current was our slot, so SetView tore down _views[our slot] (nothing) and
            // left the Customiser view active. deactivate it ourselves — unless the user actually
            // navigated TO Customiser, in which case it's the real destination and should stay.
            if (_sv != null && _custViewIdx >= 0 && _sv.CurrentViewIndex != _custViewIdx
                && _custViewIdx < _sv.Views.Length && _sv.Views[_custViewIdx] != null)
                _sv.Views[_custViewIdx].SetActive(false);

            _custUiGo = null;
            _custVm = null;
            _custHandler = null;
            _sv = null;
            _custViewIdx = -1;
            BackdropIndex = -1;

            // restore the custom menu image to whatever the current view calls for.
            BetterFG.Customization.Menu.MenuCustomizationApplication.Instance?.RefreshImageBgVisibility();

            if (TabToggle != null) TabToggle.SetIsOnWithoutNotify(false);
            Plugin.Log.LogInfo($"navprobe PB hide, sv idx {(_svIdxAtHide)}");
            BetterFG.Services.DiscordPresenceService.Push();
        }
    }
}
