using System;
using BetterFG.Core;
using FG.Common;
using FGClient;
using UnityEngine;
using FallGuysLib.Camera;

namespace BetterFG.Tweaks
{
    public class CinematicSpectatorTweak : BfgTweak
    {
        public CinematicSpectatorTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "cinematic_spectator";
        public override string TweakLabel => "Cinematic Spectator";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "While spectating, lock the camera to a fixed cinematic angle that always faces whoever you're watching. Cuts to a new angle when you switch target or they run too far, and zooms in with distance.";

        public static CinematicSpectatorTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const float FarDistance = 45f;
        const float MinShotDuration = 7f;
        const float FrameHeight = 15f;
        const float MinFov = 15f;
        const float MaxFov = 60f;
        const float EyeHeight = 0.8f;
        const float MinDriftSpeed = 0.25f;
        const float MaxDriftSpeed = 0.7f;

        NavPromptHandle _prompt;
        bool _promptShowsOn;
        bool _cinematicOn;
        bool _sessionOver;
        ClientGameManager _cgm;
        CameraDirector _director;
        Cinemachine.CinemachineBrain _brain;
        Camera _cam;
        Transform _lastTarget;
        Vector3 _lockedPos;
        Vector3 _drift;
        float _lastAngle;
        float _lastCutTime;
        float _originalFov;

        public override void DisableTweak() => Shutdown();

        public static void OnClientGameManagerShutdown()
        {
            if (Instance == null) return;
            Instance._sessionOver = true;
            Instance.Shutdown();
        }

        public override void OnSpectatorMode()
        {
            _sessionOver = false;
            ClearCaches();
        }

        public override void OnRoundStart()
        {
            _sessionOver = false;
            Shutdown();
        }

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            if (newState == null || newState.TryCast<StateGameInProgress>() == null) Shutdown();
        }

        void Update()
        {
            if (!IsEnabled || _sessionOver) { Shutdown(); return; }

            if (_cgm == null) GlobalGameStateClient.Instance?.GameStateView?.GetLiveClientGameManager(out _cgm);
            if (_cgm == null || !_cgm.IsSpectatorMode) { Shutdown(); return; }

            EnsurePrompt();
            if (_prompt != null && _prompt.IsPressed()) SetCinematic(!_cinematicOn);
        }

        void LateUpdate()
        {
            if (!_cinematicOn) return;

            var target = CurrentTargetTransform();
            if (target == null || _cam == null) return;

            if (_brain != null && _brain.enabled) _brain.enabled = false;

            _lockedPos += _drift * Time.deltaTime;

            var look = target.position + Vector3.up * EyeHeight;
            bool switched = target != _lastTarget;
            bool ranOff = Vector3.Distance(_lockedPos, look) > FarDistance
                          && Time.time - _lastCutTime > MinShotDuration;
            if (switched || ranOff)
            {
                Reposition(target);
                look = target.position + Vector3.up * EyeHeight;
            }

            var dir = look - _lockedPos;
            if (dir.sqrMagnitude < 0.0001f) return;

            var t = _cam.transform;
            t.position = _lockedPos;
            t.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            _cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(FrameHeight * 0.5f / dir.magnitude) * Mathf.Rad2Deg, MinFov, MaxFov);
        }

        Transform CurrentTargetTransform()
        {
            if (_director == null) _director = CameraLocator.GetCameraDirector();
            if (_director == null) return null;
            var selected = _director._selectedPlayerSpectatorCameraTarget;
            if (selected != null && selected.Transform != null) return selected.Transform;
            return _director.SpectatedPlayerController?.transform;
        }

        void SetCinematic(bool on)
        {
            if (!on) { Disengage(); return; }

            if (_director == null) _director = CameraLocator.GetCameraDirector();
            if (_director == null) return;

            var target = CurrentTargetTransform();
            if (target == null)
            {
                Plugin.Log.LogWarning("cinematic cam: nobody selected to spectate, not engaging");
                return;
            }

            _cam = _director.MainNativeCam;
            _brain = _director.Brain;
            if (_cam == null) { Plugin.Log.LogWarning("cinematic cam: no native camera to drive"); return; }

            _originalFov = _cam.fieldOfView;
            if (_brain != null) _brain.enabled = false;

            _cinematicOn = true;
            _lastAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Reposition(target);
            Plugin.Log.LogInfo($"cinematic cam on, watching {_director._selectedPlayerSpectatorCameraTarget?.Name}");
        }

        void Disengage()
        {
            if (!_cinematicOn) return;
            _cinematicOn = false;
            if (_cam != null) _cam.fieldOfView = _originalFov;
            if (_brain != null) _brain.enabled = true;
            _lastTarget = null;
            _cam = null;
            _brain = null;
            Plugin.Log.LogInfo("cinematic cam off, cinemachine has the camera back");
        }

        void Reposition(Transform target)
        {
            _lastTarget = target;
            _lastCutTime = Time.time;
            var look = target.position + Vector3.up * EyeHeight;

            float height = UnityEngine.Random.Range(4f, 9f);
            Vector3 chosen = Vector3.zero;
            for (int i = 0; i < 6; i++)
            {
                _lastAngle += UnityEngine.Random.Range(Mathf.PI * 0.55f, Mathf.PI * 1.45f);
                float radius = UnityEngine.Random.Range(14f, 22f);
                chosen = look + new Vector3(Mathf.Sin(_lastAngle) * radius, height, Mathf.Cos(_lastAngle) * radius);
                if (!Physics.Linecast(look, chosen)) break;
            }
            _lockedPos = chosen;

            float driftAngle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            _drift = new Vector3(Mathf.Sin(driftAngle), 0f, Mathf.Cos(driftAngle))
                     * UnityEngine.Random.Range(MinDriftSpeed, MaxDriftSpeed);
        }

        void EnsurePrompt()
        {
            if (_prompt != null && _prompt.IsAlive && _promptShowsOn == _cinematicOn) return;
            _prompt?.Destroy();
            _promptShowsOn = _cinematicOn;
            _prompt = NavPromptCore.From(NavPrompt.Favourite)
                .WithLabel(_cinematicOn ? "Cinematic Cam: On" : "Cinematic Cam: Off",
                           _cinematicOn ? "bfg_cinematic_spectator_on" : "bfg_cinematic_spectator_off")
                .AnchoredAt(NavPromptAnchor.TopRight, new Vector2(-360f, -70f))
                .OnOwnCanvas()
                .PollActions(RewiredConsts.Action.Menu_Favourite)
                .AllowWhileUnfocused()
                .SpawnOn(null);
        }

        void Shutdown()
        {
            Disengage();
            _prompt?.Destroy();
            _prompt = null;
            ClearCaches();
        }

        void ClearCaches()
        {
            _cgm = null;
            _director = null;
            _lastTarget = null;
        }
    }
}
