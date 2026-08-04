using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FGClient;
using FGClient.Customiser;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class LobbyCustomiserTweak : BfgTweak
    {
        public LobbyCustomiserTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "lobby_customiser";
        public override string TweakLabel => "Customise In Private lobby";
        public override string TweakTooltip => "Shows the customiser options in the top right of a custom lobby, without the top bar.";
        public override bool DefaultEnabled => true;

        public static LobbyCustomiserTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const string UiRootPath = "UICanvas_Client_V2(Clone)/Default";
        const string LobbyCanvasSub = "Prime_UI_PrivateLobby_Canvas(Clone)";
        const string TopBarSub = "Topbar_Prime(Clone)";

        const string SafeAreaSub = "Prime_UI_Customizer_Prefab_Canvas(Clone)/Generic_UI_CustomizerLandingPage_Prefab/SafeArea";

        static readonly Vector2 TopRight = new Vector2(1f, 1f);
        const float RowScale = 0.68f;
        const float MarginX = 4f;
        const float MarginY = 52f;
        const float PixelsPerUnit = 0.76f;

        static readonly Vector3 PartyHome = Vector3.zero;
        static readonly Vector3 PartyShifted = new Vector3(-3.9f, 0f, 0f);
        const float SlideTime = 0.35f;

        private RectTransform _row;
        private Vector2 _origAnchorMin, _origAnchorMax, _origPivot, _origAnchoredPos, _origSizeDelta;
        private Vector3 _origScale;
        private CustomiserScreenViewModel _vm;
        private bool _selectorUp;
        private int _previousView = -1;
        private bool _open;
        private bool _inLobby;

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            bool lobby = newState != null && newState.TryCast<StatePrivateLobby>() != null;
            if (lobby == _inLobby) return;
            _inLobby = lobby;
            if (lobby) StartCoroutine(Open().WrapToIl2Cpp());
            else Close();
        }

        public override void EnableTweak()
        {
            if (_inLobby && !_open) StartCoroutine(Open().WrapToIl2Cpp());
        }

        public override void DisableTweak() => Close();

        void Update()
        {
            if (!IsEnabled || !_open) return;

            var rootGo = GameObject.Find(UiRootPath);
            if (rootGo == null) { Close(); return; }
            var root = rootGo.transform;

            var bar = root.Find(TopBarSub);
            if (bar != null && bar.gameObject.activeSelf) bar.gameObject.SetActive(false);

            if (_vm == null) return;

            bool selectorUp = _vm.CurrentScreen != CustomiserScreens.SelectButtons;
            if (selectorUp == _selectorUp) return;
            _selectorUp = selectorUp;
            SetSelectorMode(selectorUp);
        }

        private void SetSelectorMode(bool on)
        {
            var rootGo = GameObject.Find(UiRootPath);
            var lobbyCanvas = rootGo == null ? null : rootGo.transform.Find(LobbyCanvasSub);
            if (lobbyCanvas != null) lobbyCanvas.gameObject.SetActive(!on);

            var lobbyScreen = GameObject.Find("Menu_Screen_Lobby(Clone)");
            if (lobbyScreen != null)
            {
                var fg = lobbyScreen.transform.Find("ForegroundCanvas");
                if (fg != null) fg.gameObject.SetActive(!on);
            }

            StartCoroutine(SlideParty(on ? PartyShifted : PartyHome).WrapToIl2Cpp());
        }

        private IEnumerator Open()
        {
            MainMenuBuilder builder = null;
            Transform lobby = null;
            float waited = 0f;
            while (waited < 10f)
            {
                var rootGo = GameObject.Find(UiRootPath);
                var root = rootGo == null ? null : rootGo.transform;
                lobby = root == null ? null : root.Find(LobbyCanvasSub);

                var builderGo = root == null ? null : root.Find("MainMenuBuilder(Clone)");
                builder = builderGo == null ? null : builderGo.GetComponent<MainMenuBuilder>();

                if (lobby != null && builder != null) break;
                yield return new WaitForSeconds(0.1f);
                waited += 0.1f;
            }

            if (lobby == null || builder == null)
            {
                Plugin.Log.LogWarning("lobby canvas or menu builder never showed up, customiser stays closed");
                yield break;
            }

            var view = builder.SwitchableView;
            var parent = builder._customiseUIParent;
            int index = -1;
            var views = view.Views;
            for (int i = 0; i < views.Length; i++)
                if (views[i] != null && views[i].transform == parent) { index = i; break; }

            if (index < 0)
            {
                Plugin.Log.LogWarning($"customiser isn't in the menu's view list ({views.Length} views), giving up");
                yield break;
            }

            _previousView = view.CurrentViewIndex;
            view.SetView(index, false, false, false);
            _open = true;

            var topBar = GameObject.Find(UiRootPath).transform.Find(TopBarSub);
            if (topBar != null) topBar.gameObject.SetActive(false);

            yield return null;

            var safeArea = parent.Find(SafeAreaSub);
            safeArea.Find("LandingPageTitle_Text").gameObject.SetActive(false);

            var rowT = safeArea.Find("HorizontalLayout");
            var row = rowT.GetComponent<RectTransform>();

            _row = row;
            _origAnchorMin = row.anchorMin;
            _origAnchorMax = row.anchorMax;
            _origPivot = row.pivot;
            _origAnchoredPos = row.anchoredPosition;
            _origSizeDelta = row.sizeDelta;
            _origScale = rowT.localScale;

            var size = row.rect.size;

            row.anchorMin = TopRight;
            row.anchorMax = TopRight;
            row.pivot = TopRight;
            row.sizeDelta = size;
            rowT.localScale = new Vector3(RowScale, RowScale, RowScale);
            row.anchoredPosition = new Vector2(-MarginX, -MarginY);

            foreach (var img in rowT.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                img.pixelsPerUnitMultiplier = PixelsPerUnit;

            _vm = builder.CustomiserScreenViewModel;

            Plugin.Log.LogInfo($"customiser up via SetView({index}), buttons moved top right ({size.x} x {size.y} at {RowScale})");
        }

        private IEnumerator SlideParty(Vector3 target)
        {
            var go = GameObject.Find("Menu_Screen_Lobby(Clone)");
            if (go == null) yield break;
            var party = go.transform.Find("PartyPlacementTransforms");
            if (party == null) yield break;

            var from = party.localPosition;
            float t = 0f;
            while (t < SlideTime)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / SlideTime);
                party.localPosition = Vector3.Lerp(from, target, 1f - Mathf.Pow(1f - k, 3f));
                yield return null;
            }
            party.localPosition = target;
        }

        private void Close()
        {
            if (!_open) return;
            _open = false;

            if (_row != null)
            {
                _row.anchorMin = _origAnchorMin;
                _row.anchorMax = _origAnchorMax;
                _row.pivot = _origPivot;
                _row.anchoredPosition = _origAnchoredPos;
                _row.sizeDelta = _origSizeDelta;
                _row.transform.localScale = _origScale;
                _row.transform.parent.Find("LandingPageTitle_Text").gameObject.SetActive(true);
                _row = null;
            }

            if (_selectorUp) SetSelectorMode(false);
            _selectorUp = false;
            _vm = null;

            var rootGo = GameObject.Find(UiRootPath);
            var builderGo = rootGo == null ? null : rootGo.transform.Find("MainMenuBuilder(Clone)");
            var builder = builderGo == null ? null : builderGo.GetComponent<MainMenuBuilder>();
            if (builder != null && _previousView >= 0)
                builder.SwitchableView.SetView(_previousView, false, false, false);

            _previousView = -1;
            Plugin.Log.LogInfo("customiser closed, menu view handed back");
        }
    }
}
