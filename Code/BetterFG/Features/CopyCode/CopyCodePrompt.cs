using System;
using BetterFG.Core;
using FallGuysLib.UI;
using FGClient.UI;
using FGClient.UI.PrivateLobby;
using FGClient;
using UnityEngine;
using LB = Wushu.LevelEditor.Runtime.UI.LevelBrowser;

namespace BetterFG.Features.CopyCode
{
    // "Copy Code" on C, in the game's own NavigationOverlay row. Same shape as
    // LevelBrowserPortPrompt: postfixes hand us whichever view model currently owns the screen, and
    // while it's live we float the prompt so it appears and disappears exactly like the game's own
    // Back / Start prompts. Three sources, mutually exclusive in practice:
    //   - private lobby screen -> the lobby code
    //   - discovery / show selector tile -> that show's share code
    //   - creative level browser tile -> that level's share code
    // Persistent singleton spawned from Plugin.InitGameObjects.
    public class CopyCodePrompt : MonoBehaviour
    {
        public CopyCodePrompt(IntPtr ptr) : base(ptr) { }

        public static CopyCodePrompt Instance { get; private set; }

        private PrivateLobbyScreenViewModel _lobbyVm;
        private LB.LevelBrowserTileViewModel _levelTile;

        // never keep the show tile itself: its backing pointer goes stale as the selector rebuilds
        // and any read off it access-violates. the preview panel hands us the show, we keep the code.
        private string _showCode;

        // private lobby's own show list. empty share code = an official show, nothing to copy.
        private string _showListCode;

        private NavPromptHandle _prompt;
        private string _code;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // PrivateLobbyScreenViewModel.OnGainFocus / OnLoseFocus
        public static void OnLobbyFocus(PrivateLobbyScreenViewModel vm)
        {
            if (Instance == null) return;
            Instance._lobbyVm = vm;
            Plugin.Log.LogInfo($"private lobby has focus, code reads \"{vm.Code}\"");
        }

        public static void OnLobbyBlur()
        {
            if (Instance == null) return;
            Instance._lobbyVm = null;
        }

        // ShowSelectorShowPreviewViewModel.SetIndividualShowData — the highlighted show
        public static void OnShowPreviewed(ShowSelectorShow show)
        {
            if (Instance == null) return;
            Instance._showCode = show != null ? show.ShareCode : null;
        }

        public static void OnShowPreviewCleared()
        {
            if (Instance == null) return;
            Instance._showCode = null;
        }

        // FragglePlobbiesManager.ShowHighlighted — private lobby show list nav
        public static void OnPrivateLobbyShowHighlighted(FragglePlobbiesManager mgr)
        {
            if (Instance == null) return;
            Instance._showListCode = mgr.ShareCode;
        }

        public static void OnPrivateLobbyShowListClosed()
        {
            if (Instance == null) return;
            Instance._showListCode = null;
        }

        // LevelBrowserTileViewModel.OnSelected, off the same postfix that feeds LevelBrowserPortPrompt
        public static void OnLevelTileSelected(LB.LevelBrowserTileViewModel tile)
        {
            if (Instance == null) return;
            Instance._levelTile = tile;
        }

        void Update()
        {
            _code = CurrentCode();
            if (string.IsNullOrEmpty(_code))
            {
                DestroyPrompt();
                return;
            }

            if (_prompt == null || !_prompt.IsAlive)
            {
                // NavPrompt.Report is just the prefab source; OwnGlyph swaps in our own key/pad
                // glyphs. C on keyboard, the pad's pause/menu button (Options / Menu / Start).
                _prompt = NavPromptCore.From(NavPrompt.Report)
                    .WithLabel("Copy Code", "bfg_copycode_prompt")
                    .InGameOverlay()
                    .AllowWhileUnfocused()
                    .OwnGlyph()
                    .PollActions(RewiredConsts.Action.Default_OpenInGameMenu)
                    .JoystickOnly()
                    .AlsoAcceptKey(KeyCode.C)
                    .SpawnOn(null);
            }

            if (_prompt != null && _prompt.IsPressed()) Copy();
        }

        private string CurrentCode()
        {
            if (!string.IsNullOrEmpty(_showListCode)) return _showListCode;

            if (!string.IsNullOrEmpty(_showCode)) return _showCode;

            if (_levelTile != null && _levelTile.gameObject.activeInHierarchy && _levelTile.HasLevel)
                return _levelTile.TileData?.LevelCode;

            if (_lobbyVm != null && _lobbyVm._isInFocus) return _lobbyVm.Code;

            return null;
        }

        private void Copy()
        {
            GUIUtility.systemCopyBuffer = _code;
            Plugin.Log.LogInfo($"{_code} -> clipboard");

            NavPromptCore.RegisterCmsString("bfg_copycode_title", "Copied");
            NavPromptCore.RegisterCmsString("bfg_copycode_body", "Code copied to your clipboard.");
            PopUp.ShowPopup("bfg_copycode_title", "bfg_copycode_body",
                PopupInteractionType.Info, UIModalMessage.ModalType.MT_OK,
                UIModalMessage.OKButtonType.Default, (Action<bool>)(_ => { }));
        }

        private void DestroyPrompt()
        {
            _prompt?.Destroy();
            _prompt = null;
        }
    }
}
