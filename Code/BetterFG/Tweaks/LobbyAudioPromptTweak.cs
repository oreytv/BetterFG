using System;
using BetterFG.Core;
using FG.Common;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using FGClient.UI.PrivateLobby;
using UnityEngine;
using UnityEngine.EventSystems;

namespace BetterFG.Tweaks
{
    public class LobbyAudioPromptTweak : BfgTweak
    {
        public LobbyAudioPromptTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "lobby_audio_prompt";
        public override string TweakLabel => "Audio Settings Prompt In Lobby";
        public override bool DefaultEnabled => true;

        private const string UiRootPath = "UICanvas_Client_V2(Clone)/Default";
        private const string LobbyCanvasSub = "Prime_UI_PrivateLobby_Canvas(Clone)";
        private const string SafeAreaPath = "Menu_Screen_Lobby(Clone)/ForegroundCanvas/Prefab_UI_Lobby/UI_Matchmaking_Prime/SafeArea";

        private bool _inLobby;
        private NavPromptHandle _prompt;
        private bool _settingsOpen;
        private GameObject _settingsViewGo;
        private int _lobbyViewIndex = -1;
        private PrivateLobbyScreenViewModel _lobbyVm;
        private float _settingsOpenedAt;

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            _inLobby = newState != null && newState.TryCast<StatePrivateLobby>() != null;
            if (!_inLobby)
            {
                DestroyPrompt();
                _settingsOpen = false;
                _settingsViewGo = null;
                _lobbyViewIndex = -1;
                _lobbyVm = null;
            }
        }

        public override void DisableTweak()
        {
            if (_settingsOpen) CloseSettings();
            DestroyPrompt();
        }

        void Update()
        {
            if (!IsEnabled || !_inLobby) { DestroyPrompt(); return; }

            if (_settingsOpen && Time.unscaledTime - _settingsOpenedAt > 0.5f
                && (_settingsViewGo == null || !_settingsViewGo.activeInHierarchy))
                CloseSettings();

            if (!_settingsOpen && LobbyCustomiserTweak.PopupOpen) { DestroyPrompt(); return; }

            if (_prompt == null || !_prompt.IsAlive)
            {
                var safeArea = GameObject.Find(SafeAreaPath);
                if (safeArea == null) return;

                _prompt = NavPromptCore.From(NavPrompt.Report)
                    .WithLabel("Audio Settings", "bfg_lobby_audio_prompt")
                    .AnchoredAt(NavPromptAnchor.TopLeft)
                    .PollActions(RewiredConsts.Action.Menu_Report)
                    .AllowWhileUnfocused()
                    .SpawnOn(safeArea.transform);
            }

            if (_prompt != null && _prompt.IsPressed())
            {
                if (_settingsOpen) CloseSettings();
                else OpenSettings();
            }
        }

        private void OpenSettings()
        {
            var builder = FindBuilder();
            var view = builder != null ? builder.SwitchableView : null;
            if (view == null)
            {
                Plugin.Log.LogWarning("lobby audio prompt: no MainMenuBuilder/SwitchableView under " + UiRootPath);
                return;
            }

            var settingsParent = builder._settingsUIParent;
            var views = view.Views;
            int index = -1;
            for (int i = 0; i < views.Length; i++)
            {
                if (views[i] != null && views[i].transform == settingsParent) { index = i; break; }
            }

            var screen = builder.SettingsScreenViewModel;
            if (index < 0 || screen == null)
            {
                Plugin.Log.LogWarning($"lobby audio prompt: settings page not found (index={index}, screen={(screen != null)})");
                return;
            }

            _lobbyViewIndex = view.CurrentViewIndex;
            SetLobbyVisible(false);

            _settingsViewGo = views[index];
            view.SetView(index, false, true, false);
            bool opened = screen.OpenSettingsOption(SettingsScreens.Audio);
            _settingsOpen = true;
            _settingsOpenedAt = Time.unscaledTime;
            Plugin.Log.LogInfo($"lobby audio prompt: opened settings view {index} (was {_lobbyViewIndex}), OpenSettingsOption(Audio)={opened}, active={_settingsViewGo.activeInHierarchy}");
        }

        private void CloseSettings()
        {
            var builder = FindBuilder();
            var view = builder != null ? builder.SwitchableView : null;
            if (view != null && _lobbyViewIndex >= 0)
                view.SetView(_lobbyViewIndex, false, true, false);

            bool stillActiveAfterSetView = _settingsViewGo != null && _settingsViewGo.activeInHierarchy;
            if (_settingsViewGo != null) _settingsViewGo.SetActive(false);

            Plugin.Log.LogInfo($"lobby audio prompt: closed, back to view {_lobbyViewIndex}, settings still active after SetView alone={stillActiveAfterSetView}");

            _settingsViewGo = null;
            _settingsOpen = false;
            SetLobbyVisible(true);
            Customization.Player.SkinApplicationService.Instance?.ApplyGameColourPatternToAllBeans();
        }

        private void SetLobbyVisible(bool visible)
        {
            var rootGo = GameObject.Find(UiRootPath);
            var lobbyCanvas = rootGo == null ? null : rootGo.transform.Find(LobbyCanvasSub);
            if (lobbyCanvas != null) lobbyCanvas.gameObject.SetActive(visible);

            var lobbyScreen = GameObject.Find("Menu_Screen_Lobby(Clone)");
            if (lobbyScreen != null)
            {
                var fg = lobbyScreen.transform.Find("ForegroundCanvas");
                if (fg != null) fg.gameObject.SetActive(visible);
            }
            if (visible) UI.Tabs.UIScalingTab.ApplyCanvasScalingFromSettings();

            LobbyCustomiserTweak.Instance?.SetNavSuspended(!visible);

            if (_lobbyVm == null) _lobbyVm = FindLobbyVm();
            if (visible && EventSystem.current != null)
            {
                EventSystem.current.sendNavigationEvents = true;
                var back = _lobbyVm?._inputHandler?._lastSelectedGameObject;
                if (back != null && back.activeInHierarchy) EventSystem.current.SetSelectedGameObject(back);
            }
        }

        private static MainMenuBuilder FindBuilder()
        {
            var rootGo = GameObject.Find(UiRootPath);
            var builderGo = rootGo == null ? null : rootGo.transform.Find("MainMenuBuilder(Clone)");
            return builderGo == null ? null : builderGo.GetComponent<MainMenuBuilder>();
        }

        private static PrivateLobbyScreenViewModel FindLobbyVm()
        {
            var go = GameObject.Find("Menu_Screen_Lobby(Clone)");
            return go == null ? null : go.GetComponentInChildren<PrivateLobbyScreenViewModel>(true);
        }

        private void DestroyPrompt()
        {
            if (_prompt == null) return;
            _prompt.Destroy();
            _prompt = null;
        }
    }
}
