using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using FG.Common.Character;
using FG.Common.Character.MotorSystem;
using FallGuysLib.Players;
using FallGuysLib.Round;
using FallGuysLib.UI;
using FG.Common;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;
using Levels.Progression;
using BetterFG.Services;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Features.QualificationTime;
using MPG.Utility;

namespace BetterFG.Tweaks
{
    public class ImmediateRespawnTweak : BfgTweak
    {
        public ImmediateRespawnTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "immediate_respawn";
        public override string TweakLabel => "tweak.immediate_respawn_button";
        public override bool DefaultEnabled => true;

        public static ImmediateRespawnTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private static bool _claimed;
        private static bool _respawning;
        private static bool _eligible;

        private const string QuickModeKey = "tweak.immediate_respawn.quick_mode_start";
        private static bool QuickModeIsStart => SettingsService.Get(QuickModeKey, "false") == "true";
        internal static void SetQuickMode(bool start) => SettingsService.Set(QuickModeKey, start ? "true" : "false");

        public override List<TweakSetting> GetSettings() => new List<TweakSetting>
        {
            new TweakSetting
            {
                Label = "respawn.quick_mode_label",
                Options = new[] { "respawn.quick_option_checkpoint", "respawn.quick_option_start" },
                Selected = () => QuickModeIsStart ? 1 : 0,
                OnPick = i => SettingsService.Set(QuickModeKey, i == 1 ? "true" : "false"),
            },
        };

        private static Vector3? _startSpawn;

        // GameStates/PlayingState. it's a FocusableViewModel, so the game itself drops its focus
        // when the options menu (or a banner, or spectator) takes over and hands it back when that
        // closes - which is the whole reason the prompt follows the options menu correctly instead
        // of us trying to guess when to re-add it.
        private static InGamePlayingState _playingState;
        private static LevelEditorTestOverlayViewModel _testOverlay;
        private static float _nextFocusOwnerScan;

        private static ScreenViewModel _editorPopup;

        internal static void SuppressWhile(ScreenViewModel popup)
        {
            _editorPopup = popup;
            DestroyPrompt();
        }

        private static bool EditorPopupUp
        {
            get
            {
                if (_editorPopup == null) return false;
                if (_editorPopup.gameObject.activeInHierarchy && !_editorPopup.IsBeingRemoved) return true;
                _editorPopup = null;
                return false;
            }
        }

        public override void EnableTweak()
        {
            var gs = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState;
            if (gs?.TryCast<StateGameInProgress>() != null) OnRoundStart();
        }
        public override void DisableTweak()
        {
            _eligible = false;
            DestroyPrompt();
            _respawning = false;
        }

        public override void OnRoundStart()
        {
            var levelName = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName;
            if (!(levelName?.StartsWith("ugc-") ?? false)) { SetEligible(false); return; }
            SetEligible(true);
        }

        public override void OnLevelEditorPlaytest() => SetEligible(true);

        public override void OnLevelEditorPlaytestEnd() => SetEligible(false);

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            if (newState == null || newState.TryCast<StateGameInProgress>() != null) return;
            SetEligible(false);
        }

        private void SetEligible(bool on)
        {
            _respawning = false;
            _playingState = null;
            _testOverlay = null;
            _nextFocusOwnerScan = 0f;
            _startSpawn = null;
            _eligible = on
                && IsEnabled
                && !GameRulesUtils.IsSurvivalRound()
                && PlayerUtils.GetOtherPlayerIds().Count == 0;
            if (!_eligible) DestroyPrompt();
        }

        void Update()
        {
            if (!_eligible) return;

            if (EditorPopupUp) { DestroyPrompt(); return; }

            if (_startSpawn == null) TryCaptureStartSpawn();

            if (_playingState == null && _testOverlay == null && Time.unscaledTime >= _nextFocusOwnerScan)
            {
                _nextFocusOwnerScan = Time.unscaledTime + 0.5f;
                foreach (var st in Resources.FindObjectsOfTypeAll<InGamePlayingState>())
                    if (st != null && st.gameObject.scene.IsValid()) { _playingState = st; break; }
                foreach (var ov in Resources.FindObjectsOfTypeAll<LevelEditorTestOverlayViewModel>())
                    if (ov != null && ov.gameObject.scene.IsValid()) { _testOverlay = ov; break; }
            }

            bool shouldShow = _testOverlay != null && _testOverlay.gameObject.activeInHierarchy
                ? _testOverlay._isInFocus
                : _playingState == null || _playingState._isInFocus;

            if (!shouldShow) { DestroyPrompt(); return; }

            if (!_claimed || !NavPromptCore.OverlayRowActive) Claim();

            if (_respawning || RespawnMenu.AnyOpen) return;
            if (FGInputLockService.IsLocked) return;
            if (!Rewired.ReInput.isReady) return;
            var p = Rewired.ReInput.players.GetPlayer(0);
            if (p == null) return;
            if (p.GetButtonDown(RewiredConsts.Action.Default_ResetRunOrSkipRound)) QuickRespawn();
            else if (MenuKeyDown(p)) OpenRespawnMenu();
        }

        private const KeyCode MenuKey = KeyCode.T;

        private static bool MenuKeyDown(Rewired.Player p)
        {
            var kb = Rewired.ReInput.controllers.Keyboard;
            if (kb != null && kb.GetKeyDown(MenuKey)) return true;

            var sticks = p.controllers.Joysticks;
            for (int i = 0; i < p.controllers.joystickCount; i++)
            {
                var j = sticks[i];
                if (j == null) continue;
                int id = TopFaceElementId(j);
                if (id >= 0 && j.GetButtonDownById(id)) return true;
            }
            return false;
        }

        private static int CurrentPadTopFace()
        {
            if (!Rewired.ReInput.isReady || Rewired.ReInput.players.playerCount == 0) return -1;
            var p = Rewired.ReInput.players.GetPlayer(0);
            var sticks = p.controllers.Joysticks;
            for (int i = 0; i < p.controllers.joystickCount; i++)
            {
                var j = sticks[i];
                if (j == null) continue;
                int id = TopFaceElementId(j);
                if (id >= 0) return id;
            }
            return -1;
        }

        private static int TopFaceElementId(Rewired.Joystick j)
        {
            var ids = j.ButtonElementIdentifiers;
            int n = j.buttonCount;
            for (int b = 0; b < n; b++)
            {
                var id = ids[b];
                if (id == null) continue;
                string name = id.name;
                if (name == "Triangle" || name == "Y") return id.id;
            }
            return -1;
        }

        // "where we first spawned" = the local bean's own position on the first frame it exists this
        // round. same source live and in editor playtest, no per-mode divergence, and it captures the
        // real spawn even when the level authored something other than checkpoint zone 0 for it.
        private static void TryCaptureStartSpawn()
        {
            var fgcc = UnityRoundLoader.InLevelEditor
                ? BeanMonitorService.LocalPlayerBean?.GetComponent<FallGuysCharacterController>()
                : PlayerUtils.PlayerController;
            if (fgcc == null) return;
            _startSpawn = fgcc.transform.position;
        }

        // two prompts in the row: the existing Respawn button (retargeted to Default_ResetRunOrSkipRound
        // so Rewired resolves R on keyboard, Triangle on DS4/DS5, Y on Xbox), plus a Menu opener bound
        // to Default_ShowNames (Tab on keyboard, L3 press on pad — nothing to show in solo UGC).
        private static void Claim()
        {
            NavPromptCore.ClaimOverlayRow(
                new NavPromptCore.OverlayClaim
                {
                    Key = NavPrompt.Report,
                    LabelKey = "bfg_respawn_label",
                    LabelText = "Respawn",
                    OnPressed = QuickRespawn,
                    IconAction = RewiredConsts.Action.Default_ResetRunOrSkipRound,
                    IconCategory = RewiredConsts.Category.Default,
                },
                new NavPromptCore.OverlayClaim
                {
                    Key = NavPromptCore.Favourite,
                    LabelKey = "bfg_respawn_menu_label",
                    LabelText = "Respawn Menu",
                    OnPressed = OpenRespawnMenu,
                    PostBuild = btn => NavPromptCore.ApplyOwnGlyphByElement(btn, MenuKey, CurrentPadTopFace()),
                });
            _claimed = true;
        }

        private static void QuickRespawn()
        {
            if (_respawning || RespawnMenu.AnyOpen) return;
            if (QuickModeIsStart) DoStartRespawn();
            else DoCheckpointRespawn();
        }

        private static void OpenRespawnMenu()
        {
            if (Instance == null || _respawning || RespawnMenu.AnyOpen) return;
            Instance.StartCoroutine(RespawnMenu.OpenRoutine().WrapToIl2Cpp());
        }

        private static void DestroyPrompt()
        {
            if (!_claimed) return;
            _claimed = false;
            NavPromptCore.ReleaseOverlayRow();
        }

        internal static void DoCheckpointRespawn()
        {
            if (UnityRoundLoader.InLevelEditor) { DoEditorRespawn(); return; }

            var localFgcc = PlayerUtils.PlayerController;
            if (localFgcc == null) return;

            uint localNetId = PlayerUtils.GetLocalNetObjectId();
            if (localNetId == 0) return;

            var checkpointMgr = UnityEngine.Object.FindObjectOfType<CheckpointManager>();
            if (checkpointMgr == null) return;

            var cpMap = checkpointMgr.NetIDToCheckpointMap;
            if (cpMap == null || !cpMap.TryGetValue((MPGNetID)localNetId, out uint cpId)) return;

            var zones = checkpointMgr._checkpointZones;
            CheckpointZonePositions targetZone = null;
            for (int i = 0; i < zones.Count; i++)
            {
                var czp = zones[i]?.TryCast<CheckpointZonePositions>();
                if (czp != null && czp.uniqueId == cpId) { targetZone = czp; break; }
            }
            if (targetZone == null) return;

            var spawnTransform = targetZone.GetRandomTransform();
            if (spawnTransform == null) return;

            TeleportTo(localFgcc, spawnTransform.position, resetTime: false);
        }

        // editor playtest: LevelEditorManager knows the current respawn point (current checkpoint, or the level
        // start if none reached yet) — same source the kill-zones use. local bean is LevelEditorManager's player.
        private static void DoEditorRespawn()
        {
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) return;

            var bean = BeanMonitorService.LocalPlayerBean;
            if (bean == null) return;

            var localFgcc = bean.GetComponent<FallGuysCharacterController>();
            if (localFgcc == null) return;

            if (!mgr.TryGetRespawnTransform(out Vector3 position, out _)) return;

            TeleportTo(localFgcc, position, resetTime: false);
        }

        internal static void DoStartRespawn()
        {
            if (_startSpawn == null) { Plugin.Log.LogWarning("respawn-at-start with no cached spawn yet — did the bean not tick?"); return; }

            var localFgcc = UnityRoundLoader.InLevelEditor
                ? BeanMonitorService.LocalPlayerBean?.GetComponent<FallGuysCharacterController>()
                : PlayerUtils.PlayerController;
            if (localFgcc == null) return;

            TeleportTo(localFgcc, _startSpawn.Value, resetTime: true);
        }

        private static void TeleportTo(FallGuysCharacterController localFgcc, Vector3 position, bool resetTime)
        {
            var teleport = localFgcc.TeleportMotorFunction;
            if (teleport == null) return;

            teleport.TeleportPosition = position;

            var states = teleport.OriginalStates;
            if (states == null || states.Count < 2) return;

            var activeState = states[1].TryCast<MotorFunctionTeleportStateActive>();
            if (activeState == null) return;

            _respawning = true;
            activeState.Begin(-1);
            Instance.StartCoroutine(FinishTeleport(activeState, resetTime).WrapToIl2Cpp());
        }

        private static IEnumerator FinishTeleport(MotorFunctionTeleportStateActive activeState, bool resetTime)
        {
            yield return new WaitForSeconds(0.5f);
            activeState.End(-1);
            if (resetTime) FeatureQualificationTime.ResetElapsedBaseline();
            _respawning = false;
        }
    }

    // reuses the game's own UGC "report level" popup, same trick as LevelPortReportMenu: relabel the
    // first two report-reason rows to Checkpoint / Start, hide the rest, point them at the two
    // ImmediateRespawnTweak teleport paths. nothing here ever touches moderation.
    internal static class RespawnMenu
    {
        internal static bool AnyOpen => _popupVm != null && _popupVm.gameObject.activeInHierarchy;

        private static bool _armed;
        private static ReportUGCPopupViewModel _popupVm;
        private static ReportUGCConfigurationElementViewModel _checkpointElem, _startElem;

        internal static IEnumerator OpenRoutine()
        {
            var rm = SingletonBehaviour<ReportManager>.Instance;
            if (rm == null) yield break;

            _armed = false;
            rm.OpenVisualReportUGCPopup("Respawn", "", 0, false);

            ReportUGCPopupViewModel vm = null;
            for (int i = 0; i < 12 && vm == null; i++) { yield return null; vm = FindLivePopup(); }
            if (vm == null) yield break;
            _popupVm = vm;
            yield return null; // let Setup() build the rows

            Apply(vm);

            yield return null;
            _armed = true;
        }

        private static ReportUGCPopupViewModel FindLivePopup()
        {
            foreach (var v in Resources.FindObjectsOfTypeAll<ReportUGCPopupViewModel>())
                if (v != null && v.gameObject.scene.IsValid()) return v;
            return null;
        }

        private static void Apply(ReportUGCPopupViewModel vm)
        {
            var ih = vm._popupInputHandler;
            var elems = ih != null ? ih._settingElements : null;
            if (elems == null || elems.Count < 2) return;

            _checkpointElem = elems[0];
            _startElem = elems[1];

            var content = elems[0].transform.parent;
            for (int i = 0; i < content.childCount; i++)
            {
                var c = content.GetChild(i);
                if (c.GetComponent<ReportUGCConfigurationElementViewModel>() == null)
                    c.gameObject.SetActive(false); // the "Offensive ..." section headers
            }

            var variant = vm.transform.Find("Generic_UI_LE_ReportRoundPopup_Prefab_Variant");
            if (variant != null)
            {
                SetText(variant, "TitleContainer/TitleText", "respawn.menu_title");
                SetText(variant, "BodyText", "respawn.menu_body");
                SetText(variant, "ButtonContainer/RightButton/Content/Text", "ui.confirm");
                SetText(variant, "ButtonContainer/LeftButton/Content/Text", "ui.close");
            }

            WireRow(_checkpointElem, "respawn.checkpoint_label", PickCheckpoint);
            WireRow(_startElem, "respawn.start_label", PickStart);
            for (int i = 2; i < elems.Count; i++) elems[i].gameObject.SetActive(false);

            if (vm._acceptButton != null)
            {
                vm._acceptButton.onClick.RemoveAllListeners();
                vm._acceptButton.onClick.AddListener((UnityAction)(() =>
                {
                    var sel = ih.GetCurrentSelected();
                    Fire(sel != null && sel == _startElem ? PickStart : PickCheckpoint);
                }));
            }
        }

        private static void PickCheckpoint() => ImmediateRespawnTweak.SetQuickMode(false);

        private static void PickStart() => ImmediateRespawnTweak.SetQuickMode(true);

        private static void Fire(Action act)
        {
            if (!_armed || act == null) return;
            _armed = false;
            act();
            CloseMenu();
        }

        private static void SetText(Transform root, string path, string text)
        {
            var t = root.Find(path);
            if (t == null) return;
            var go = t.gameObject;
            var loc = go.GetComponent("LocalisedStaticLabel")?.TryCast<Behaviour>();
            if (loc != null) loc.enabled = false;
            var bind = go.GetComponent("TMPTextBinding")?.TryCast<Behaviour>();
            if (bind != null) bind.enabled = false;
            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp != null) BettrFG.uGUI.UGUIShip.RelabelText(tmp, text);
        }

        private static void WireRow(ReportUGCConfigurationElementViewModel e, string label, Action act)
        {
            SetText(e.transform, "SettingsText", label);

            var call = (UnityAction)(() => Fire(act));
            e._OnPress?.RemoveAllListeners();
            e.AddOnPressListener(call);

            var btn = e.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(call);
            }
        }

        private static void CloseMenu()
        {
            SingletonBehaviour<ReportManager>.Instance?.CloseVisualReportUGCPopup();
        }
    }
}
