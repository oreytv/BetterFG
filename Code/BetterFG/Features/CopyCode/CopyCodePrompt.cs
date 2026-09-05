using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Patches.GameStates;
using FallGuysLib.UI;
using FGClient.UI;
using FGClient.UI.Core;
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
        private bool _showListActive;

        private NavigationPromptData _data;
        private bool _injected;

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
        }

        public static void OnLobbyBlur()
        {
            if (Instance == null) return;
            Instance._lobbyVm = null;
        }

        private string _showName;

        // ShowSelectorShowPreviewViewModel.SetIndividualShowData — the highlighted show
        public static void OnShowPreviewed(ShowSelectorShow show)
        {
            if (Instance == null) return;
            Instance._showCode = show != null ? show.ShareCode : null;
            Instance._showName = show?.ShowData?.ShowName?.Text;
        }

        public static void OnShowPreviewCleared()
        {
            if (Instance == null) return;
            Instance._showCode = null;
            Instance._showName = null;
        }

        // FragglePlobbiesManager.ShowHighlighted — private lobby show list nav
        public static void OnPrivateLobbyShowHighlighted(FragglePlobbiesManager mgr)
        {
            if (Instance == null) return;
            Instance._showListCode = mgr.ShareCode;
            Instance._showListActive = true;
        }

        public static void OnPrivateLobbyShowListClosed()
        {
            if (Instance == null) return;
            Instance._showListCode = null;
            Instance._showListActive = false;
        }

        // LevelBrowserTileViewModel.OnSelected, off the same postfix that feeds LevelBrowserPortPrompt
        public static void OnLevelTileSelected(LB.LevelBrowserTileViewModel tile)
        {
            if (Instance == null) return;
            Instance._levelTile = tile;
        }

        public static void OnPublishSuccess(LevelEditorPublishSuccessViewModel vm)
        {
            if (Instance == null) return;
            Instance._publishVm = vm;
        }

        private LevelEditorPublishSuccessViewModel _publishVm;

        private bool PublishScreenUp =>
            _publishVm != null && _publishVm.gameObject.activeInHierarchy && !_publishVm.IsBeingRemoved;

        void Update()
        {
            bool screenActive = ScreenActive();

            if (screenActive != _injected)
            {
                if (screenActive)
                {
                    if (_data == null)
                        _data = NavPromptInjection.BuildData(NavPrompt.Report, "Copy Code", "bfg_copycode_prompt",
                            RewiredConsts.Action.Default_OpenInGameMenu, RewiredConsts.Category.Default);
                    NavPromptInjection.Add(NavPromptInjection.CopyCode, Copy, _data,
                        btn => NavPromptCore.ApplyOwnGlyph(btn, KeyCode.C, RewiredConsts.Action.Default_OpenInGameMenu));
                }
                else NavPromptInjection.Remove(NavPromptInjection.CopyCode);
                _injected = screenActive;
            }
            if (!screenActive) return;

            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrlHeld && (BetterFG.Services.KeybindService.KeyDown(KeyCode.C) ||
                NavPromptCore.PollActionDirect(RewiredConsts.Action.Default_OpenInGameMenu, null, true)))
                Copy();
        }

        private bool ScreenActive()
        {
            if (_showListActive && !PublishScreenUp) return !string.IsNullOrEmpty(_showListCode);
            return !string.IsNullOrEmpty(CurrentCode());
        }

        private string CurrentCode()
        {
            ResolveCurrent(out string code, out _);
            return code;
        }

        private void Copy()
        {
            string code, source;
            ResolveCurrent(out code, out source);
            if (string.IsNullOrEmpty(code)) return;
            GUIUtility.systemCopyBuffer = code;

            string body = source == "lobby"
                ? "Copied lobby code to your clipboard."
                : string.IsNullOrEmpty(source)
                    ? $"Copied [{code}] to your clipboard."
                    : $"Copied {source} [{code}] to your clipboard.";

            NavPromptCore.RegisterCmsString("bfg_copycode_title", "Copied");
            NavPromptCore.SetCmsString("bfg_copycode_body", body);
            PopUp.ShowPopup("bfg_copycode_title", "bfg_copycode_body",
                PopupInteractionType.Info, UIModalMessage.ModalType.MT_OK,
                UIModalMessage.OKButtonType.Default,
                (Action<bool>)(_ => StartCoroutine(RestoreLobbyFocusAfterClose().WrapToIl2Cpp())));
        }

        private static IEnumerator RestoreLobbyFocusAfterClose()
        {
            yield return new WaitForSeconds(1f);
            RestoreLobbyFocus.Kick();
            yield return null;
            var go = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prefab_UI_PrivateLobbyShowSelect(Clone)");
            go?.GetComponent<PrivateLobbyShowListViewModel>()?.OnGainFocus();
        }

        // resolve which source is live right now + a friendly label for it. same priority order
        // as CurrentCode. source is a display string, safe to inline into the popup body (may be
        // null if we don't have a good name).
        private void ResolveCurrent(out string code, out string source)
        {
            if (PublishScreenUp) { code = _publishVm.LevelCode; source = "level"; return; }
            if (!string.IsNullOrEmpty(_showListCode)) { code = _showListCode; source = "show"; return; }
            if (!string.IsNullOrEmpty(_showCode)) { code = _showCode; source = string.IsNullOrEmpty(_showName) ? "show" : $"show \"{_showName}\""; return; }
            if (_levelTile != null && _levelTile.gameObject.activeInHierarchy && _levelTile.HasLevel)
            {
                var td = _levelTile.TileData;
                code = td?.LevelCode;
                source = td != null && !string.IsNullOrEmpty(td.Name) ? $"level \"{td.Name}\"" : "level";
                return;
            }
            if (_lobbyVm != null && _lobbyVm._isInFocus) { code = _lobbyVm.Code; source = "lobby"; return; }
            code = null; source = null;
        }

    }
}
