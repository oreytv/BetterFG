using System;
using System.Collections;
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
using PlayerUtils = FallGuysLib.Players.PlayerUtils;
using Levels.Progression;
using BetterFG.Services;
using BetterFG.Features.UnityRound.Editor;
using KeybindService = BetterFG.Services.KeybindService;

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

        // GameStates/PlayingState. it's a FocusableViewModel, so the game itself drops its focus
        // when the options menu (or a banner, or spectator) takes over and hands it back when that
        // closes - which is the whole reason the prompt follows the options menu correctly instead
        // of us trying to guess when to re-add it.
        private static InGamePlayingState _playingState;
        private static bool _playingStateScanned;

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
            _playingStateScanned = false;
            _eligible = on
                && IsEnabled
                && !GameRulesUtils.IsSurvivalRound()
                && PlayerUtils.GetOtherPlayerIds().Count == 0;
            if (!_eligible) DestroyPrompt();
        }

        void Update()
        {
            if (!_eligible) return;

            // GameObject.Find can't see it - the SwitchableView deactivates whichever state isn't
            // current, so PlayingState is an inactive object most of the time. same scene-filtered
            // FindObjectsOfTypeAll walk NavPromptCore uses for the overlay manager.
            if (_playingState == null && !_playingStateScanned)
            {
                foreach (var st in Resources.FindObjectsOfTypeAll<InGamePlayingState>())
                    if (st != null && st.gameObject.scene.IsValid()) { _playingState = st; break; }
                _playingStateScanned = true;
            }

            // no PlayingState (editor playtest runs its own UI) means nothing is going to steal the
            // row from us, so just keep the prompt up.
            bool shouldShow = _playingState == null || _playingState._isInFocus;

            // step aside while the options menu or a banner owns focus: give the slot's real data
            // back so their prompts resolve normally, but don't clear the row they just claimed.
            if (!shouldShow) { _claimed = false; NavPromptCore.YieldOverlayRow(); return; }

            // re-claim on the frame focus comes back to gameplay (options menu closing, banner
            // ending) and whenever something else switched the row off behind us.
            if (!_claimed || !NavPromptCore.OverlayRowActive) Claim();

            if (_respawning) return;
            if (!NavPromptCore.PollActionDirect(RewiredConsts.Action.Default_ResetRunOrSkipRound, null, true)
                && !KeybindService.KeyDown(KeyCode.R)) return;
            DoRespawn();
        }

        // Report is only the prefab the clone is built from (same as the level-port prompt); the
        // glyph is retargeted to Default_ResetRunOrSkipRound, which is the game's own reset-run
        // action - so Rewired resolves it per layout for us: R on keyboard, Triangle on DS4/DS5,
        // Y on Xbox, and whatever the right button is on anything else.
        private static void Claim()
        {
            NavPromptCore.ClaimOverlayRow(NavPrompt.Report, "bfg_respawn_label", "Respawn", DoRespawn,
                RewiredConsts.Action.Default_ResetRunOrSkipRound, RewiredConsts.Category.Default);
            _claimed = true;
        }

        private static void DestroyPrompt()
        {
            if (!_claimed) return;
            _claimed = false;
            NavPromptCore.ReleaseOverlayRow();
        }

        private static void DoRespawn()
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

            TeleportTo(localFgcc, spawnTransform.position);
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

            TeleportTo(localFgcc, position);
        }

        private static void TeleportTo(FallGuysCharacterController localFgcc, Vector3 position)
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
            Instance.StartCoroutine(FinishTeleport(activeState).WrapToIl2Cpp());
        }

        private static IEnumerator FinishTeleport(MotorFunctionTeleportStateActive activeState)
        {
            yield return new WaitForSeconds(0.5f);
            activeState.End(-1);
            _respawning = false;
        }
    }
}
