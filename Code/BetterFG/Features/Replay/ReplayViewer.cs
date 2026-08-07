using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.UI.Tab;
using BetterFG.Utilities;
using Cinemachine;
using FallGuysLib.Camera;
using FallGuysLib.NPC;
using FG.Common;
using FG.Common.Fraggle;
using FGClient;
using MPG.Utility;
using UnityEngine;
using Wushu.Integration;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BetterFG.Features.Replay
{
    public partial class ReplayViewer : MonoBehaviour
    {
        public ReplayViewer(IntPtr ptr) : base(ptr) { }

        public static ReplayViewer Instance;

        public static bool PadFlight => Instance != null && Instance._worldHeld && Input.GetMouseButton(0);

        const float MARGIN = 16f;
        const float WIN_W = 320f;
        const float WIN_H = 216f;
        const float ROW = 22f;
        const float PAD = 8f;
        const float HEADER_H = ROW + 6f;
        const float PLAYPAUSE_SIZE = 20f;
        const float LANE_NAME_W = 62f;
        const float TICKS_Y = PAD + HEADER_H;
        // the edge markers get their own band above the scrub strip, so grabbing one can never be
        // confused with dragging the playhead
        const float EDGE_Y = TICKS_Y + 15f;
        const float EDGE_BAND_H = 13f;
        const float SCRUB_Y = EDGE_Y + EDGE_BAND_H;
        const float SCRUB_H = 12f;
        const float LANE_H = 24f;
        const float LANE_GAP = 2f;
        const int LANE_COUNT = 3;
        const float LANES_Y = SCRUB_Y + SCRUB_H + 3f;
        const float VIS_LANE_Y = LANES_Y;
        const float FX_LANE_Y = LANES_Y + (LANE_COUNT - 2) * (LANE_H + LANE_GAP);
        const float CAM_LANE_Y = LANES_Y + (LANE_COUNT - 1) * (LANE_H + LANE_GAP);
        const float LANES_H = CAM_LANE_Y + LANE_H - LANES_Y;
        const float KEYROW_Y = CAM_LANE_Y + (LANE_H - KEY_H) * 0.5f;
        const float VIS_KEYROW_Y = VIS_LANE_Y + (LANE_H - KEY_H) * 0.5f;
        const float FX_KEYROW_Y = FX_LANE_Y + (LANE_H - KEY_H) * 0.5f;
        const float VIS_SPLIT_Y = VIS_LANE_Y + LANE_H + LANE_GAP * 0.5f;
        const float FX_SPLIT_Y = FX_LANE_Y + LANE_H + LANE_GAP * 0.5f;
        const float NAME_LABEL_HEIGHT = 2.35f;
        const float NAME_LABEL_SCALE = 0.18f;
        const float NAME_FADE_START = 30f;
        const float NAME_FADE_END = 50f;
        const float LANES_BOTTOM = CAM_LANE_Y + LANE_H + 6f;
        const float ZOOMBAR_GAP = 10f;
        const float ZOOMBAR_H = 10f;
        const float ZOOMBAR_Y = LANES_BOTTOM + ZOOMBAR_GAP;
        const float ZOOMBAR_MARGIN_B = 6f;
        const float TIMELINE_H = ZOOMBAR_Y + ZOOMBAR_H + ZOOMBAR_MARGIN_B;
        const float CONTROLS_H = ROW + 8f;
        const float TIMELINE_Y = MARGIN + CONTROLS_H + 8f;
        const float MIN_VIEW_SPAN = 0.5f;
        const float MIN_EXPORT_SPEED = 0.05f;
        const float SPEED_MIN = 2f;
        const float SPEED_MAX = 90f;
        const float FOV_MIN = 15f;
        const float FOV_MAX = 120f;
        const float RULER_TARGET_TICKS = 7f;
        static readonly float[] RULER_STEPS =
            { 0.1f, 0.2f, 0.5f, 1f, 2f, 5f, 10f, 15f, 30f, 60f, 120f, 300f, 600f, 900f, 1800f, 3600f };
        const int MINOR_DIVISIONS = 5;
        const float MINOR_TICK_H = SCRUB_H * 0.5f;
        static readonly Color TICK_MINOR = new Color(1f, 1f, 1f, 0.1f);
        const float MENU_W = 168f;
        const float DRAG_DEADZONE = 5f;
        const float DUP_GAP = 0.5f;
        const float DOUBLE_CLICK = 0.35f;
        const float FRAME_RATE = 60f;
        const float FRAME_TIME = 1f / FRAME_RATE;
        const float FRAME_HOLD_THRESHOLD = 0.5f;
        const float FRAME_HOLD_REPEAT = 0.05f;
        const float SNAP_BTN_GAP = 6f;

        static readonly Color PANEL_BG = new Color(0.06f, 0.07f, 0.09f, 0.92f);
        static readonly Color HEADER_BG = new Color(0.13f, 0.15f, 0.19f, 1f);
        static readonly Color BTN_DARK = new Color(0.18f, 0.18f, 0.2f, 1f);
        static readonly Color BTN_RED = new Color(0.45f, 0.18f, 0.18f, 1f);
        static readonly Color BTN_GREEN = new Color(0.22f, 0.42f, 0.26f, 1f);
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        static readonly Color TICK = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color KEY_IDLE = new Color(0.42f, 0.76f, 1f, 1f);
        static readonly Color KEY_SELECTED = new Color(1f, 0.82f, 0.25f, 1f);
        static readonly Color KEY_EDITING = new Color(0.38f, 0.85f, 0.45f, 1f);
        static readonly Color KEY_OUTLINE = new Color(0.04f, 0.05f, 0.07f, 1f);
        static readonly Color KEY_CUT_OUTLINE = new Color(1f, 0.3f, 0.15f, 1f);
        static readonly Color MENU_BG = new Color(0.1f, 0.11f, 0.14f, 0.97f);
        static readonly Vector3 TITLE_POS = new Vector3(3.5219f, 9.2927f, 0f);

        const float KEY_W = 15f;
        const float KEY_H = 18f;

        static Sprite _fillSprite;
        static Sprite _outlineSprite;
        static Sprite _stripeSprite;
        static Sprite _playheadFill;
        static Sprite _playheadOutline;

        static Sprite _edgeFill;
        static Sprite _edgeOutline;
        static Sprite _timelineBg;
        static Sprite _playFillSprite;
        static Sprite _playOutlineSprite;
        static Sprite _pauseFillSprite;
        static Sprite _pauseOutlineSprite;
        static Sprite _snapFillSprite;
        static Sprite _snapOutlineSprite;
        static Sprite _unsnapFillSprite;
        static Sprite _unsnapOutlineSprite;

        // 1200x500 art: the slab's top edge runs y 229..235 and its bottom sits at 496, with the camera
        // cluster parked at x 856..1199, y 13..219 — flush to the right edge. sliced with the camera as
        // the top-right corner it stays its own size while the slab stretches to whatever width the
        // timeline is.
        const float BG_SCALE = 0.31f;
        const float BG_BORDER_L = 8f;
        const float BG_BORDER_B = 8f;
        const float BG_BORDER_R = 344f;
        // 238, not the 229 where the slab's top edge starts: the camera's base keeps going to 237 and
        // anything of it below the border lands in the stretched middle and smears
        const float BG_BORDER_T = 238f;
        // a smaller multiplier scales the sliced borders up, so the overhang has to grow with it or the
        // slab stops lining up with the bar
        const float BG_PPU_MULT = 0.5f;
        const float BG_OVERHANG = BG_BORDER_T * BG_SCALE / BG_PPU_MULT;

        // both markers are drawn off-centre art: the playhead's stem sits at x 23..26 of a 43 wide
        // canvas and the edge marker's at x 40..53 of 63, with their grab arms flaring off to the
        // side, so the stem centre is what lands on the time — not the middle of the image.
        const float PH_H = CAM_LANE_Y + LANE_H - SCRUB_Y;
        const float PH_W = PH_H * 43f / 150f;
        const float PH_STEM = 24.5f / 43f;
        const float EDGE_H = CAM_LANE_Y + LANE_H - EDGE_Y;
        const float EDGE_W = EDGE_H * 63f / 180f;
        const float EDGE_STEM = 46.5f / 63f;
        const float GRAB_PX = 10f;

        const float MIN_TRIM = 0.25f;
        static readonly Color PLAYHEAD_YELLOW = new Color(1f, 0.83f, 0.25f, 1f);
        static readonly Color EDGE_BLUE = new Color(0.36f, 0.62f, 1f, 1f);
        static readonly Color OUTSIDE_DIM = new Color(0f, 0f, 0f, 0.45f);
        static readonly Color LANE_BG = new Color(1f, 1f, 1f, 0.05f);
        static Texture2D _highlightTex;
        static Sprite _gizmoSprite;

        const float HIGHLIGHT_HEIGHT = 2.9f;
        const float HIGHLIGHT_SIZE = 0.8f;
        const float GIZMO_REACH = 3f;
        const float GIZMO_ICON = 65f;
        const float GIZMO_LINE_W = 2f;

        static readonly Color GIZMO_TINT = new Color(1f, 1f, 1f, 0.85f);
        static readonly Color GIZMO_LINE_TINT = new Color(1f, 1f, 1f, 0.5f);

        ReplayRecording _rec;
        ReplayAudioPlayer _audio;
        ReplayWorldPlayer _world;
        ReplayStarchartPlayer _starchart;
        ReplayVfxPlayer _vfx;
        ReplaySpeechPlayer _speech;
        ReplayPostFx _postFx;
        Camera _cam;
        Canvas _canvas;
        class Track
        {
            public ReplayPlayer player;
            public GameObject bean;
            public Animator anim;
            public Transform upperBody;
            public Transform armLeft;
            public Transform armRight;
            public int cursor;
            public GameObject nameLabel;
            public bool isGhost;
        }

        readonly List<GameObject> _beans = new List<GameObject>();
        readonly List<Track> _tracks = new List<Track>();
        readonly List<AsyncOperationHandle<SceneInstance>> _sceneHandles = new List<AsyncOperationHandle<SceneInstance>>();
        readonly List<Scene> _loadedScenes = new List<Scene>();
        readonly List<CinemachineBrain> _brains = new List<CinemachineBrain>();
        Scene _prevActiveScene;
        Transform _camParent;
        Vector3 _camLocalPos;
        Quaternion _camLocalRot;
        float _camFov;
        bool _tookCamera;
        static GameObject _beanPrefab;
        Il2CppSystem.Object _prevLevelLoader;
        bool _touchedFraggle;
        bool _sceneLoaded;
        bool _exiting;
        bool _swapping;
        bool _loading;
        float _exitStart;
        GameObject _fallbackLight;
        GameObject _highlight;
        uint _highlightPlayer;
        int _highlightObject = -1;
        bool _highlightOn;
        RectTransform _gizmoRoot;
        RectTransform _gizmoIcon;
        RectTransform _gizmoLineL;
        RectTransform _gizmoLineR;
        readonly List<GameObject> _hidden = new List<GameObject>();
        bool _menuMusicWasPlaying;

        float _time;
        float _shakeTime;
        bool _paused = true;
        float _yaw;
        float _pitch;
        float _speed = 14f;
        Vector3 _lastMouse;
        bool _looking;

        Text _clock;
        Text _info;
        Text _playLabel;
        Image _playPauseFill;
        Image _playPauseOutline;
        Image _snapFill;
        Image _snapOutline;
        bool _snapToKeyframes = true;
        bool _snapGuideActive;
        bool _timelineFocused;
        float _leftArrowHeldAt = -1f;
        float _leftArrowRepeatAt;
        float _rightArrowHeldAt = -1f;
        float _rightArrowRepeatAt;
        Button _deleteBtn;
        RectTransform _controlsRt;
        Button _restoreBtn;
        Button _doneBtn;
        bool _minimized;
        bool _editingCam;
        bool _picking;
        bool _pickObjects;
        bool _pickWasFree;
        bool _pickLookTarget;
        bool _pickForNames;
        bool _pickVisibleOnly;
        ReplayKeyframe _pickKeyframe;
        ReplayVisibilityKeyframe _pickVisKeyframe;
        List<int> _pickAllowed;
        int _pickHover = -1;
        uint _pickHoverPlayer;
        Text _pickHint;
        Text _camEditHint;
        ReplayKeyframe _editKeyframe;
        readonly List<GameObject> _hiddenUi = new List<GameObject>();
        bool _exporting;
        bool _exportCancelled;
        string _exportWav;
        float _exportAudioLead;
        readonly List<Text> _rulerLabels = new List<Text>();
        readonly List<RectTransform> _rulerTicks = new List<RectTransform>();
        readonly List<RectTransform> _minorTicks = new List<RectTransform>();
        RectTransform _rulerRoot;
        RectTransform _windowRt;
        RectTransform _marker;
        RectTransform _inMarker;
        RectTransform _outMarker;
        RectTransform _dimLeft;
        RectTransform _dimRight;
        RectTransform _cutOverlayRoot;
        readonly List<RectTransform> _cutOverlays = new List<RectTransform>();
        Image _playheadImg;
        Image _inFill;
        Image _outFill;
        bool _draggingIn;
        bool _draggingOut;
        RectTransform _zoomFillRt;
        bool _draggingZoomLeft;
        bool _draggingZoomRight;
        bool _draggingZoomPan;
        float _dragZoomPanGrabX;
        float _dragZoomPanStartViewStart;
        bool _hoverZoomEdge;
        RectTransform _timelineRt;
        RectTransform _contextMenu;
        RectTransform _keyTicks;
        RectTransform _visTicks;
        RectTransform _fxTicks;
        readonly List<ReplayKeyframe> _selected = new List<ReplayKeyframe>();
        readonly List<float> _dragStartTimes = new List<float>();
        ReplayKeyframe _dragKeyframe;
        float _dragGrabTime;
        float _dragGrabMouseX;
        bool _dragMoved;
        ReplayKeyframe _clickedKeyframe;
        float _clickedAt;
        readonly List<ReplayVisibilityKeyframe> _selectedVis = new List<ReplayVisibilityKeyframe>();
        readonly List<float> _dragStartTimesVis = new List<float>();
        ReplayVisibilityKeyframe _dragVisKeyframe;
        float _dragGrabTimeVis;
        float _dragGrabMouseXVis;
        bool _dragMovedVis;
        bool _marqueeingVis;
        Vector2 _marqueeAnchorVis;
        RectTransform _marqueeRtVis;
        readonly List<ReplayVisibilityKeyframe> _marqueeBaseVis = new List<ReplayVisibilityKeyframe>();
        readonly List<ReplayPostFxKeyframe> _selectedFx = new List<ReplayPostFxKeyframe>();
        readonly List<float> _dragStartTimesFx = new List<float>();
        ReplayPostFxKeyframe _dragFxKeyframe;
        float _dragGrabTimeFx;
        float _dragGrabMouseXFx;
        bool _dragMovedFx;
        bool _marqueeingFx;
        Vector2 _marqueeAnchorFx;
        RectTransform _marqueeRtFx;
        readonly List<ReplayPostFxKeyframe> _marqueeBaseFx = new List<ReplayPostFxKeyframe>();
        static GameObject _nametagPrefab;
        bool _freeLook;
        bool _worldHeld;
        bool _worldTap;
        bool _scrubbing;
        bool _marqueeing;
        Vector2 _marqueeAnchor;
        RectTransform _marqueeRt;
        readonly List<ReplayKeyframe> _marqueeBase = new List<ReplayKeyframe>();
        float _trackLeft;
        float _trackWidth;
        float _viewStart;
        float _viewSpan;

        public static void Open(ReplayRecording rec)
        {
            if (rec == null || Instance != null) return;

            var go = new GameObject("BettrFG_ReplayViewer");
            DontDestroyOnLoad(go);
            var viewer = go.AddComponent<ReplayViewer>();
            Instance = viewer;
            viewer._rec = rec;
            viewer._loading = true;
            LoadingScreenService.Show();
            DiscordPresenceService.OnReplayViewerOpened();
            viewer.PushReplayPresence();
            viewer.StartCoroutine(viewer.OpenRoutine().WrapToIl2Cpp());
        }

        IEnumerator OpenRoutine()
        {
            yield return new WaitForSecondsRealtime(0.5f);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _audio = new ReplayAudioPlayer(_rec);
            _prevActiveScene = SceneManager.GetActiveScene();

            HideGame();
            yield return StartCoroutine(LoadLevel().WrapToIl2Cpp());
            yield return null;
            ApplySets();
            DisableControllerLeftovers();
            yield return StartCoroutine(LoadBeanPrefab().WrapToIl2Cpp());

            Build();

            _world = new ReplayWorldPlayer(_rec, _loadedScenes);
            _starchart = new ReplayStarchartPlayer(_rec, _loadedScenes);
            yield return StartCoroutine(_world.Prepare().WrapToIl2Cpp());

            SpawnBeans();
            SyncUi();
            Plugin.Log.LogInfo($"replay viewer up: {_rec.roundName} on {_rec.sceneName}, {_beans.Count} beans, {_rec.duration:0.0}s");

            yield return StartCoroutine(_audio.Prepare(_cam.transform).WrapToIl2Cpp());

            _loading = false;
            LoadingScreenService.Hide();

            yield return StartCoroutine(ThumbnailRoutine().WrapToIl2Cpp());
        }

        IEnumerator ThumbnailRoutine()
        {
            float settle = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < settle) yield return null;
            yield return new WaitForEndOfFrame();
            ReplayThumbnail.Capture(_cam, _rec.sourcePath);
        }

        public void Swap(ReplayRecording rec)
        {
            if (rec == null || _exiting || _swapping) return;
            StartCoroutine(SwapRoutine(rec).WrapToIl2Cpp());
        }

        IEnumerator SwapRoutine(ReplayRecording rec)
        {
            _swapping = true;
            ReplayKeyframeWindow.Instance?.Close();
            ReplayVisibilityKeyframeWindow.Instance?.Close();
            ReplayPostFxKeyframeWindow.Instance?.Close();
            CloseContextMenu();
            _audio.Release();
            _world?.Release();
            _world = null;
            _speech?.Release();
            _speech = null;
            _postFx?.Release();
            _postFx = null;

            foreach (var bean in _beans)
                if (bean != null) Destroy(bean);
            _beans.Clear();
            foreach (var track in _tracks)
                if (track.nameLabel != null) Destroy(track.nameLabel);
            _tracks.Clear();

            if (_tookCamera && FromLoadedLevel(_cam.gameObject))
            {
                var camPos = _cam.transform.position;
                var camRot = _cam.transform.rotation;
                float camFov = _cam.fieldOfView;
                RestoreCamera();
                MakeBareCamera(camPos, camRot, camFov);
                Plugin.Log.LogInfo("the camera we're flying belongs to the level going away, parked on a bare one so the unload can't take it with it");
            }

            if (_prevActiveScene.IsValid() && _prevActiveScene.isLoaded)
                SceneManager.SetActiveScene(_prevActiveScene);
            yield return StartCoroutine(UnloadLevel().WrapToIl2Cpp());

            _rec = rec;
            _time = 0f;
            _shakeTime = 0f;
            _paused = true;
            _freeLook = false;
            _snapToKeyframes = true;
            _selected.Clear();
            _dragKeyframe = null;
            _selectedVis.Clear();
            _dragVisKeyframe = null;
            _selectedFx.Clear();
            _dragFxKeyframe = null;
            _scrubbing = false;
            EndMarquee();
            EndMarqueeVis();
            EndMarqueeFx();
            _audio = new ReplayAudioPlayer(_rec);
            PushReplayPresence();

            yield return StartCoroutine(LoadLevel().WrapToIl2Cpp());
            yield return null;
            ApplySets();
            DisableControllerLeftovers();
            if (_beanPrefab == null) yield return StartCoroutine(LoadBeanPrefab().WrapToIl2Cpp());

            MatchScene();
            Destroy(_timelineRt.gameObject);
            BuildTimeline();
            KillNavigation();

            _world = new ReplayWorldPlayer(_rec, _loadedScenes);
            _starchart = new ReplayStarchartPlayer(_rec, _loadedScenes);
            yield return StartCoroutine(_world.Prepare().WrapToIl2Cpp());

            SpawnBeans();
            _swapping = false;
            ApplyTime();
            SyncUi();
            Plugin.Log.LogInfo($"swapped the viewer over to {_rec.roundName} on {_rec.sceneName}: {_beans.Count} beans, {_rec.keyframes.Count} keyframes, {_rec.duration:0.0}s");

            yield return StartCoroutine(_audio.Prepare(_cam.transform).WrapToIl2Cpp());
            yield return StartCoroutine(ThumbnailRoutine().WrapToIl2Cpp());
        }

        IEnumerator LoadLevel()
        {
            if (_rec.isUgc)
            {
                yield return StartCoroutine(LoadCreativeLevel().WrapToIl2Cpp());
                yield break;
            }

            if (string.IsNullOrEmpty(_rec.sceneName))
            {
                Plugin.Log.LogInfo("no scene on this one, so the editor comes up empty");
                yield break;
            }

            yield return StartCoroutine(LoadScene(_rec.sceneName, true).WrapToIl2Cpp());
        }

        IEnumerator LoadScene(string sceneName, bool makeActive)
        {
            var handle = Addressables.LoadSceneAsync(
                (Il2CppSystem.Object)(Il2CppSystem.String)sceneName,
                LoadSceneMode.Additive, true, 100);

            while (!handle.IsDone) yield return null;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Plugin.Log.LogWarning($"addressables wouldn't give us {sceneName} ({handle.Status})");
                yield break;
            }

            _sceneHandles.Add(handle);

            var scene = handle.Result.Scene;
            if (!scene.IsValid() || !scene.isLoaded) yield break;

            if (makeActive) SceneManager.SetActiveScene(scene);
            _loadedScenes.Add(scene);
            _sceneLoaded = true;
            Plugin.Log.LogInfo($"level scene {scene.name} in, {scene.rootCount} roots");
        }

        IEnumerator LoadCreativeLevel()
        {
            var builder = new ReplayCreativeBuilder(_rec, this, LoadScene);
            yield return StartCoroutine(builder.Build().WrapToIl2Cpp());

            _prevLevelLoader = builder.PreviousLevelLoader;
            _touchedFraggle = builder.TouchedFraggle;
        }

        void ApplySets()
        {
            if (_rec.sets.Count == 0) return;

            var switchers = new List<SetSwitcher>();
            foreach (var sw in Resources.FindObjectsOfTypeAll<SetSwitcher>())
                if (sw != null && sw.gameObject.scene.IsValid()) switchers.Add(sw);

            int applied = 0;
            var unmatched = new List<string>();
            foreach (var set in _rec.sets)
            {
                SetSwitcher target = null;
                int hit = -1;
                var recorded = string.IsNullOrEmpty(set.path) ? null : ReplayWorldPath.Resolve(set.path, "", _loadedScenes);
                if (recorded != null)
                    for (int i = 0; i < switchers.Count && hit < 0; i++)
                        if (switchers[i].transform == recorded) hit = i;
                if (hit < 0)
                    for (int i = 0; i < switchers.Count && hit < 0; i++)
                    {
                        string key = string.IsNullOrEmpty(switchers[i]._cmsKey) ? switchers[i].gameObject.name : switchers[i]._cmsKey;
                        if (key == set.key) hit = i;
                    }

                if (hit >= 0) { target = switchers[hit]; switchers.RemoveAt(hit); }
                if (target == null) { unmatched.Add(set.key); continue; }

                bool matched = false;
                foreach (var mapping in target.SwitchableSetMappings)
                {
                    if (mapping == null || mapping.SwitchableSetHolder == null) continue;
                    bool on = mapping.CMSKey == set.chosen;
                    mapping.SwitchableSetHolder.SetActive(on);
                    matched |= on;
                }

                if (!matched) { unmatched.Add(set.key + "/" + set.chosen); continue; }
                target.ChosenKey = set.chosen;
                applied++;
            }

            Plugin.Log.LogInfo($"variations: {applied}/{_rec.sets.Count} switchers back on the recorded set"
                + (unmatched.Count > 0 ? $" — no match for {string.Join(", ", unmatched)}" : ""));
        }

        void HideGame()
        {
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    if (!s.isLoaded) continue;
                    foreach (var root in s.GetRootGameObjects())
                    {
                        if (root == null || !root.activeSelf) continue;
                        if (root.name.StartsWith("BetterFG_") || root.name.StartsWith("BettrFG_")) continue;
                        if (!_rec.isUgc && root.name.StartsWith("Background_")) continue;
                        if (root.GetComponentInChildren<SoundBankLoadingListener>(true) != null) continue;
                        root.SetActive(false);
                        _hidden.Add(root);
                    }
                }

                var gameUi = GameObject.Find("UICanvas_Client_V2(Clone)");
                if (gameUi != null) { gameUi.SetActive(false); _hidden.Add(gameUi); }

                // the nav prompt overlay lives in DontDestroyOnLoad, which the scene sweep above never sees
                var navOverlay = GameObject.Find("Prefab_UI_NavigationOverlay(Clone)/SafeArea/SubMenuNavigation_Center");
                if (navOverlay != null) { navOverlay.SetActive(false); _hidden.Add(navOverlay); }
            }

            _menuMusicWasPlaying = MenuMusicService.IsPlaying;
            MenuMusicService.Pause();
            MenuMusicService.SetGameMenuMusicPaused(true);
        }

        // creative levels leave their editor-only controller rigs in the scene (camera controllers,
        // gizmo helpers etc) — they never show in a live round because the game itself hides them,
        // but the replay viewer loads the level directly and skips whatever does that
        void DisableControllerLeftovers()
        {
            foreach (var scene in _loadedScenes)
            {
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null || !root.activeSelf || !root.name.Contains("Controller_")) continue;
                    root.SetActive(false);
                }
            }
        }

        void ShowGame()
        {
            foreach (var go in _hidden)
                if (go != null) go.SetActive(true);
            _hidden.Clear();

            if (_menuMusicWasPlaying) MenuMusicService.Resume();
            MenuMusicService.SetGameMenuMusicPaused(MenuMusicService.Enabled);
        }

        // exiting reloads the client, which takes GlobalGameStateClient's configuration with it — so on
        // every open after the first there was nothing to read the character prefab out of and every
        // replay came up empty. the prefab itself stays loaded, so hang onto it for the session and
        // fall back to whatever's already in memory when the config isn't there.
        IEnumerator LoadBeanPrefab()
        {
            if (_beanPrefab != null) yield break;

            var cfg = GlobalGameStateClient.Instance?._configuration;
            if (cfg != null && cfg.sceneNetworkPrefabs != null)
            {
                foreach (var group in cfg.sceneNetworkPrefabs)
                {
                    if (group == null || group.sceneName != "FallGuy_Shared") continue;
                    foreach (var aref in group.networkPrefabAssetRefs)
                    {
                        if (aref == null || !aref.RuntimeKeyIsValid()) continue;
                        var handle = Addressables.LoadAssetAsync<GameObject>(aref.RuntimeKey);
                        while (!handle.IsDone) yield return null;
                        if (handle.Status != AsyncOperationStatus.Succeeded) continue;

                        var candidate = handle.Result;
                        if (candidate == null || candidate.GetComponent<FallGuysCharacterController>() == null)
                        {
                            Addressables.Release(handle);
                            continue;
                        }

                        _beanPrefab = candidate;
                        Plugin.Log.LogInfo($"bean prefab off FallGuy_Shared: {candidate.name}");
                        yield break;
                    }
                }
                Plugin.Log.LogWarning("FallGuy_Shared had no character prefab in it");
            }
            else Plugin.Log.LogWarning("no game configuration to read the character prefab from (client reloaded?), looking for one already in memory");

            foreach (var fgcc in Resources.FindObjectsOfTypeAll<FallGuysCharacterController>())
            {
                if (fgcc == null || fgcc.gameObject.scene.IsValid()) continue;
                _beanPrefab = fgcc.gameObject;
                Plugin.Log.LogInfo($"bean prefab picked up out of memory: {_beanPrefab.name}");
                yield break;
            }

            Plugin.Log.LogWarning("no character prefab anywhere, this replay's beans will be missing");
        }

        void SpawnBeans()
        {
            var speakers = new Dictionary<uint, Transform>();
            var vfxControllers = new Dictionary<uint, FG.Common.Character.FallGuyVFXController>();
            foreach (var p in _rec.players)
            {
                string label = string.IsNullOrEmpty(p.name) ? ("Player " + p.playerId) : p.name;
                var bean = MakeBean(label, p);
                if (bean == null) continue;

                bean.transform.position = p.frames.Count > 0 ? p.frames[0].pos : new Vector3(_beans.Count * 1.6f, 0f, 0f);
                bean.SetActive(true);
                StartCoroutine(DressBean(bean, p).WrapToIl2Cpp());

                var rag = bean.GetComponentInChildren<FG.Common.Character.RagdollController>(true);
                var track = new Track
                {
                    player = p,
                    bean = bean,
                    anim = BeanAnimationUtil.FindAnimator(bean),
                    upperBody = rag?.GetJoint(FG.Common.Character.RagdollJoint.ID.UpperBody)?.CachedTransform,
                    armLeft = rag?.GetJoint(FG.Common.Character.RagdollJoint.ID.ArmLeft)?.CachedTransform,
                    armRight = rag?.GetJoint(FG.Common.Character.RagdollJoint.ID.ArmRight)?.CachedTransform,
                };
                if (track.anim != null) track.anim.speed = 0f;
                if (rag != null) rag.enabled = false;

                _beans.Add(bean);
                _tracks.Add(track);
                speakers[p.playerId] = bean.transform;
                var vfx = bean.GetComponent<FG.Common.Character.FallGuyVFXController>();
                if (vfx != null) vfxControllers[p.playerId] = vfx;
            }

            SpawnGhostBeans();

            _speech = new ReplaySpeechPlayer(_rec, transform);
            _speech.Prepare(speakers);

            _vfx = new ReplayVfxPlayer(_rec, vfxControllers);

            _postFx = new ReplayPostFx(_rec, transform);
        }

        void SpawnGhostBeans()
        {
            if (_rec.ghosts.Count == 0) return;

            ReplayPlayer localP = null;
            foreach (var p in _rec.players) if (p.isLocal) { localP = p; break; }
            if (localP == null && _rec.players.Count > 0) localP = _rec.players[0];
            if (localP == null) return;

            for (int i = 0; i < _rec.ghosts.Count; i++)
            {
                var ghost = _rec.ghosts[i];
                if (ghost.frames.Count == 0) continue;

                var synthetic = new ReplayPlayer
                {
                    playerId = 0x80000000u + (uint)i,
                    name = ghost.name,
                    colour = localP.colour,
                    pattern = localP.pattern,
                    costumeTop = localP.costumeTop,
                    costumeBottom = localP.costumeBottom,
                    costumeFull = localP.costumeFull,
                    faceplate = localP.faceplate,
                    bfgScale = localP.bfgScale,
                    bfgCosmetics = localP.bfgCosmetics,
                    bfgColour = localP.bfgColour,
                    bfgPattern = localP.bfgPattern,
                    bfgFaceplate = localP.bfgFaceplate,
                    nametag = localP.nametag?.WithoutCustomName(),
                    platformId = localP.platformId,
                    fameEarnedBadge = localP.fameEarnedBadge,
                    fameUpdatedAt = localP.fameUpdatedAt,
                    outTime = ghost.frames[ghost.frames.Count - 1].t,
                };
                synthetic.bfgSkins.AddRange(localP.bfgSkins);
                synthetic.bfgTextures.AddRange(localP.bfgTextures);
                synthetic.frames.AddRange(ghost.frames);

                var bean = MakeBean(ghost.name, synthetic);
                if (bean == null) continue;

                bean.transform.position = ghost.frames[0].pos;
                bean.SetActive(true);
                StartCoroutine(DressGhostBean(bean, synthetic).WrapToIl2Cpp());

                var rag = bean.GetComponentInChildren<FG.Common.Character.RagdollController>(true);
                var track = new Track
                {
                    player = synthetic,
                    bean = bean,
                    anim = BeanAnimationUtil.FindAnimator(bean),
                    upperBody = rag?.GetJoint(FG.Common.Character.RagdollJoint.ID.UpperBody)?.CachedTransform,
                    armLeft = rag?.GetJoint(FG.Common.Character.RagdollJoint.ID.ArmLeft)?.CachedTransform,
                    armRight = rag?.GetJoint(FG.Common.Character.RagdollJoint.ID.ArmRight)?.CachedTransform,
                    isGhost = true,
                };
                if (track.anim != null) track.anim.speed = 0f;
                if (rag != null) rag.enabled = false;

                _beans.Add(bean);
                _tracks.Add(track);
            }
        }

        IEnumerator DressGhostBean(GameObject bean, ReplayPlayer p)
        {
            yield return DressBean(bean, p);
            if (bean == null) yield break;

            var mat = BetterFG.Core.AssetManager.GhostMaterial;
            if (mat == null) yield break;
            foreach (var smr in bean.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                for (int m = 0; m < mats.Length; m++) mats[m] = mat;
                smr.sharedMaterials = mats;
            }
            foreach (var mr in bean.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = mr.sharedMaterials;
                for (int m = 0; m < mats.Length; m++) mats[m] = mat;
                mr.sharedMaterials = mats;
            }
        }

        IEnumerator DressBean(GameObject bean, ReplayPlayer p)
        {
            if (p.bfgScale > 0f)
                PlayerScaleService.ApplySkinScaleToBean(bean, p.bfgScale, PlayerScaleService.BeanScaleMode.Remote);

            bool replacesBean = false;
            var loader = CustomizationServices.LoaderService;
            var app = CustomizationServices.ApplicationService;

            if (loader != null && app != null)
            {
                foreach (var entry in p.bfgSkins)
                {
                    ActiveSkinSlot slot = null;
                    yield return loader.ResolveProfileSlot(entry, new Action<ActiveSkinSlot>(s => slot = s)).WrapToIl2Cpp();
                    if (slot == null || bean == null) continue;
                    if (slot.type == SkinType.Costume && !slot.skinInfo.keepBase) replacesBean = true;
                    yield return app.ApplySkinToBean(slot, bean).WrapToIl2Cpp();
                }
            }

            if (bean == null) yield break;

            if (replacesBean)
            {
                SkinApplicationService.ApplyEntriesToBean(p.bfgTextures, bean);
                yield break;
            }

            bool done = false;
            ApplyLook(bean, p, new Action(() =>
            {
                SkinApplicationService.ApplyEntriesToBean(p.bfgTextures, bean);
                done = true;
            }));
            while (!done) yield return null;
        }

        static void ApplyLook(GameObject bean, ReplayPlayer p, Action onDone)
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) { onDone?.Invoke(); return; }

            var costumes = new List<string>();
            if (!string.IsNullOrEmpty(p.costumeFull)) costumes.Add("gamecosm:" + p.costumeFull);
            if (!string.IsNullOrEmpty(p.costumeTop)) costumes.Add("gamecosm:" + p.costumeTop);
            if (!string.IsNullOrEmpty(p.costumeBottom)) costumes.Add("gamecosm:" + p.costumeBottom);

            foreach (string id in p.bfgCosmetics.Split('|'))
                if (!string.IsNullOrEmpty(id) && !costumes.Contains(id)) costumes.Add(id);

            svc.ApplyProfileCosmeticsToBean(
                string.Join("|", costumes),
                Worn(p.bfgColour, p.colour, "gamecolour:"),
                Worn(p.bfgPattern, p.pattern, "gamepattern:"),
                Worn(p.bfgFaceplate, p.faceplate, "gamefaceplate:"),
                bean,
                onDone);
        }

        static string Worn(string bfgId, string rosterId, string tag) =>
            !string.IsNullOrEmpty(bfgId) ? bfgId
            : string.IsNullOrEmpty(rosterId) ? "" : tag + rosterId;

        void ApplyTime()
        {
            foreach (var track in _tracks)
            {
                var frames = track.player.frames;
                if (track.bean == null || frames.Count == 0) continue;

                int i = track.cursor;
                if (i >= frames.Count || frames[i].t > _time) i = 0;
                while (i + 1 < frames.Count && frames[i + 1].t <= _time) i++;
                track.cursor = i;

                float f = 0f;
                var vel = Vector3.zero;
                if (i + 1 < frames.Count)
                {
                    float den = frames[i + 1].t - frames[i].t;
                    f = den > 0f ? Mathf.Clamp01((_time - frames[i].t) / den) : 0f;
                    track.bean.transform.position = Vector3.Lerp(frames[i].pos, frames[i + 1].pos, f);
                    track.bean.transform.rotation = Quaternion.Slerp(frames[i].rot, frames[i + 1].rot, f);
                }
                else
                {
                    track.bean.transform.position = frames[i].pos;
                    track.bean.transform.rotation = frames[i].rot;
                }

                int lo = i;
                int hi = i;
                while (lo > 0 && frames[i].t - frames[lo].t < 0.12f) lo--;
                while (hi + 1 < frames.Count && frames[hi].t - frames[i].t < 0.12f) hi++;

                float window = frames[hi].t - frames[lo].t;
                if (window > 0.0001f) vel = (frames[hi].pos - frames[lo].pos) / window;

                BeanAnimationUtil.DriveLocomotion(track.anim, track.bean.transform, vel);

                int hash = frames[i].stateHash;
                if (track.anim == null || hash == 0) continue;

                float animTime = frames[i].animTime;
                if (i + 1 < frames.Count && frames[i + 1].stateHash == hash)
                {
                    float next = frames[i + 1].animTime;
                    if (next < animTime) next += 1f;
                    animTime = Mathf.Lerp(animTime, next, f);
                }

                track.anim.Play(hash, 0, animTime);
                track.anim.Update(0f);

                if (!frames[i].ragdoll || track.upperBody == null || track.armLeft == null || track.armRight == null) continue;

                var b = i + 1 < frames.Count ? frames[i + 1] : frames[i];
                track.upperBody.localRotation = Quaternion.Slerp(frames[i].upperBody, b.upperBody, f);
                track.armLeft.localRotation = Quaternion.Slerp(frames[i].armLeft, b.armLeft, f);
                track.armRight.localRotation = Quaternion.Slerp(frames[i].armRight, b.armRight, f);
            }

            if (!_freeLook) EvaluateKeyframeCamera();
            ApplyVisibility();
            _speech?.Apply(_time, _cam);
            _postFx?.Apply(_time, _cam);
            UpdateHighlight();
            UpdateCameraGizmo();
        }

        ReplayVisibilityKeyframe VisibilityAt(float time)
        {
            ReplayVisibilityKeyframe best = null;
            var list = _rec.visibilityKeyframes;
            for (int i = 0; i < list.Count; i++)
                if (list[i].time <= time && (best == null || list[i].time > best.time)) best = list[i];
            return best;
        }

        ReplayPostFxKeyframe PostFxAt(float time)
        {
            ReplayPostFxKeyframe best = null;
            var list = _rec.postFxKeyframes;
            for (int i = 0; i < list.Count; i++)
                if (list[i].time <= time && (best == null || list[i].time > best.time)) best = list[i];
            return best;
        }

        static bool VisibleIn(ReplayVisibilityMode mode, List<uint> only, uint playerId) =>
            mode == ReplayVisibilityMode.All ? true
            : mode == ReplayVisibilityMode.None ? false
            : only.Contains(playerId);

        void ApplyVisibility()
        {
            var kf = VisibilityAt(_time);
            bool showPhrases = kf == null || kf.showPhrases;
            var mode = kf?.players ?? ReplayVisibilityMode.All;
            var nameMode = kf?.names ?? ReplayVisibilityMode.All;

            if (_speech != null)
            {
                _speech.PhrasesVisible = showPhrases;
                _speech.PlayerVisible = kf == null ? null : (Func<uint, bool>)(id => VisibleIn(mode, kf.onlyPlayers, id));
            }

            for (int i = 0; i < _tracks.Count; i++)
            {
                var track = _tracks[i];
                if (track.bean == null) continue;

                bool shown = track.isGhost ? (kf == null || kf.showGhosts) : (kf == null || VisibleIn(mode, kf.onlyPlayers, track.player.playerId));
                if (shown && track.player.outTime >= 0f && _time >= track.player.outTime) shown = false;
                if (shown && track.isGhost && track.player.frames.Count > 0 && _time < track.player.frames[0].t) shown = false;
                if (track.bean.activeSelf != shown) track.bean.SetActive(shown);

                bool nameShown = kf == null || VisibleIn(nameMode, kf.nameOnlyPlayers, track.player.playerId);
                UpdateNameLabel(track, shown && nameShown);
            }
        }

        void UpdateNameLabel(Track track, bool show)
        {
            if (!show)
            {
                if (track.nameLabel != null && track.nameLabel.activeSelf) track.nameLabel.SetActive(false);
                return;
            }

            if (track.nameLabel == null) BuildNameLabel(track);
            if (track.nameLabel == null) return;

            var pos = track.bean.transform.position + Vector3.up * NAME_LABEL_HEIGHT;
            float dist = Vector3.Distance(pos, _cam.transform.position);
            float fade = 1f - Mathf.InverseLerp(NAME_FADE_START, NAME_FADE_END, dist);

            if (fade <= 0f)
            {
                if (track.nameLabel.activeSelf) track.nameLabel.SetActive(false);
                return;
            }

            if (!track.nameLabel.activeSelf) track.nameLabel.SetActive(true);
            track.nameLabel.transform.SetPositionAndRotation(pos, _cam.transform.rotation);
            track.nameLabel.transform.localScale = Vector3.one * (dist * NAME_LABEL_SCALE);
            ApplyNameFade(track.nameLabel, fade);
        }

        static void ApplyNameFade(GameObject nameLabel, float fade)
        {
            var disp = nameLabel.GetComponent<PlayerInfoDisplayGameObject>();
            if (disp == null) return;

            if (disp._text != null) disp._text.alpha = fade;
            if (disp._arrowRenderer != null)
            {
                var c = disp._arrowRenderer.color;
                c.a = fade;
                disp._arrowRenderer.color = c;
            }
            if (disp._platformIconRenderer != null)
            {
                var c = disp._platformIconRenderer.color;
                c.a = fade;
                disp._platformIconRenderer.color = c;
            }
            NametagIconApplicator.SetIconAlphaForDisplay(disp, fade);
        }

        void BuildNameLabel(Track track)
        {
            var prefab = FindNametagPrefab();
            if (prefab == null) return;

            var clone = Instantiate(prefab);
            clone.name = "BettrFG_ReplayNametag_" + track.player.playerId;
            clone.transform.SetParent(transform, false);
            clone.SetActive(true);
            track.nameLabel = clone;

            var disp = clone.GetComponent<PlayerInfoDisplayGameObject>();
            if (disp == null || disp._text == null) return;

            string cleaned = FallGuysLib.Players.PlayerUtils.CleanPlayerName(track.player.name);
            string fallback = string.IsNullOrEmpty(cleaned) ? ("Player " + track.player.playerId) : cleaned;

            if (track.player.nametag != null)
                NametagIconApplicator.ApplyRemoteToNameplate(disp._text, fallback, track.player.nametag);
            else
            {
                disp.SetText(fallback);
                disp._text.color = Color.white;
            }

            bool platformHidden = track.player.nametag != null && track.player.nametag.platformHide == "true";
            string platformCustom = track.player.nametag?.platformCustom ?? "";
            if (platformHidden || !string.IsNullOrEmpty(platformCustom))
                NametagIconApplicator.ApplyPlatformIcon(clone, platformHidden, platformCustom);
            else
            {
                disp.SetPlatformIcon(track.player.platformId);
                NametagIconApplicator.ApplyPlatformIconByName(disp, track.player.platformId);
            }
            disp.SetNameVisualsDependingOnFame(track.player.isLocal, track.player.fameEarnedBadge,
                new Il2CppSystem.DateTime(track.player.fameUpdatedAt.Ticks));
        }

        static GameObject FindNametagPrefab()
        {
            if (_nametagPrefab != null) return _nametagPrefab;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.name != "NetworkedPlayerTagSprite") continue;
                _nametagPrefab = go;
                break;
            }
            if (_nametagPrefab == null) Plugin.Log.LogWarning("replay: no NetworkedPlayerTagSprite prefab around, names won't show");
            return _nametagPrefab;
        }

        GameObject MakeBean(string label, ReplayPlayer p)
        {
            var cust = new NPCCustomization(p.costumeTop, p.costumeBottom, p.pattern, p.faceplate, 0);
            var fgcc = SpawnBeanUtils.SpawnBean(label, cust);
            if (fgcc != null) return Neutralise(fgcc.gameObject, fgcc);

            if (_beanPrefab == null)
            {
                Plugin.Log.LogWarning($"nothing to build {label} out of, the game wouldn't spawn one and there's no prefab cached");
                return null;
            }
            var clone = Instantiate(_beanPrefab);
            clone.name = "BettrFG_ReplayBean_" + label;
            return Neutralise(clone, clone.GetComponent<FallGuysCharacterController>());
        }

        GameObject Neutralise(GameObject bean, FallGuysCharacterController fgcc)
        {
            if (fgcc != null) fgcc.enabled = false;

            // interpolation renders a body between its last two physics poses, so a bean we place by
            // hand every frame draws a step behind where we put it — dead obvious against a keyframed
            // camera, and it puts the beans out of sync with the shot in an export.
            foreach (var rb in bean.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.interpolation = RigidbodyInterpolation.None;
                rb.isKinematic = true;
                rb.useGravity = false;
            }
            foreach (var col in bean.GetComponentsInChildren<Collider>(true))
                Destroy(col);
            return bean;
        }

        void Build()
        {
            SetupCamera();
            PositionCameraOnPlayer();
            MatchScene();

            if (EventSystem.current == null)
            {
                var esGo = new GameObject("BettrFG_ReplayEventSystem");
                esGo.transform.SetParent(transform, false);
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }

            _canvas = UGUIShip.CreateCanvas("BettrFG_ReplayCanvas");
            BuildWindow();
            BuildTimeline();
            BuildControls();
            KillNavigation();
            UIScaleService.OnRescaled += RebuildTimeline;
        }

        void OnDestroy() => UIScaleService.OnRescaled -= RebuildTimeline;

        // the bar's width comes from the scaled reference resolution, not from anchors, so a rescale
        // leaves it the wrong length until it's built again
        void RebuildTimeline()
        {
            if (_timelineRt == null || _exiting) return;

            float start = _viewStart;
            float span = _viewSpan;

            CloseContextMenu();
            EndMarquee();
            EndMarqueeVis();
            EndMarqueeFx();
            _scrubbing = false;
            _draggingIn = false;
            _draggingOut = false;
            _draggingZoomLeft = false;
            _draggingZoomRight = false;
            _draggingZoomPan = false;
            _dragKeyframe = null;
            _dragVisKeyframe = null;
            _dragFxKeyframe = null;

            Destroy(_timelineRt.gameObject);
            BuildTimeline();
            KillNavigation();

            _viewStart = start;
            _viewSpan = span;
            RefreshRuler();
            SyncUi();
        }

        void BuildControls()
        {
            const float rowW = 80f;
            var row = UGUIShip.CreatePanel(_canvas.transform, new Rect(0f, 0f, rowW, CONTROLS_H), Color.clear, "ReplayControls");
            row.anchorMin = new Vector2(0f, 0f);
            row.anchorMax = new Vector2(0f, 0f);
            row.pivot = new Vector2(0f, 0f);
            row.anchoredPosition = new Vector2(MARGIN, MARGIN);
            row.sizeDelta = new Vector2(rowW, CONTROLS_H);
            row.GetComponent<Image>().raycastTarget = false;
            _controlsRt = row;

            _deleteBtn = UGUIShip.CreateButton(row, new Rect(0f, 0f, rowW, CONTROLS_H), "DELETE", BTN_RED, Color.white, UIScale.FS_SM, new Action(DeleteSelected));
            _deleteBtn.gameObject.SetActive(false);
        }

        void SetReplayUiVisible(bool visible)
        {
            _windowRt.gameObject.SetActive(visible && !_minimized);
            _timelineRt.gameObject.SetActive(visible);
            _controlsRt.gameObject.SetActive(visible);
        }

        void KillNavigation()
        {
            foreach (var selectable in _canvas.GetComponentsInChildren<Selectable>(true))
            {
                var nav = selectable.navigation;
                nav.mode = Navigation.Mode.None;
                selectable.navigation = nav;
            }
        }

        void MatchScene()
        {
            if (!_tookCamera) _cam.clearFlags = _sceneLoaded ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;

            if (_sceneLoaded)
            {
                if (_fallbackLight != null) { Destroy(_fallbackLight); _fallbackLight = null; }
                AdoptLevelCamera();
                return;
            }
            if (_fallbackLight != null) return;

            _fallbackLight = new GameObject("BettrFG_ReplayLight");
            _fallbackLight.transform.SetParent(transform, false);
            var light = _fallbackLight.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            _fallbackLight.transform.rotation = Quaternion.Euler(48f, 32f, 0f);
        }

        // the slab covers the bar and the camera hangs above its top edge, so the image is taller than
        // the timeline by exactly the sliced top border

        RectTransform BuildMarker(RectTransform parent, string name, float w, float h, Sprite outline, Sprite fill, out Image fillImg)
        {
            var root = UGUIShip.CreatePanel(parent, new Rect(0f, SCRUB_Y, w, h), Color.clear, name);
            root.GetComponent<Image>().raycastTarget = false;

            var outlineRt = UGUIShip.CreatePanel(root, new Rect(0f, 0f, w, h), KEY_OUTLINE, "Outline");
            var outlineImg = outlineRt.GetComponent<Image>();
            outlineImg.sprite = outline;
            outlineImg.raycastTarget = false;

            var fillRt = UGUIShip.CreatePanel(root, new Rect(0f, 0f, w, h), Color.white, "Fill");
            fillImg = fillRt.GetComponent<Image>();
            fillImg.sprite = fill;
            fillImg.raycastTarget = false;
            return root;
        }

        float ViewSpan => _viewSpan > 0f ? _viewSpan : Mathf.Max(_rec.duration, 0.001f);

        float TimeToFraction(float time) => (time - _viewStart) / ViewSpan;

        float FractionToTime(float f) => _viewStart + f * ViewSpan;

        RectTransform BuildRulerTick(out Text label)
        {
            var root = UGUIShip.CreatePanel(_rulerRoot, new Rect(0f, 0f, 1f, 1f), Color.clear, "RulerTick");
            root.GetComponent<Image>().raycastTarget = false;
            UGUIShip.CreatePanel(root, new Rect(0f, SCRUB_Y, 1f, SCRUB_H), TICK, "Line").GetComponent<Image>().raycastTarget = false;
            label = UGUIShip.CreateLabel(root, new Rect(-28f, TICKS_Y, 56f, 14f), "", UIScale.FS_SM - 2, TICK, TextAnchor.MiddleCenter);
            return root;
        }

        RectTransform BuildMinorTick()
        {
            var tick = UGUIShip.CreatePanel(_rulerRoot, new Rect(0f, SCRUB_Y, 1f, MINOR_TICK_H), TICK_MINOR, "MinorTick");
            tick.GetComponent<Image>().raycastTarget = false;
            return tick;
        }

        static bool OnStep(float t, float step) => Mathf.Abs(t / step - Mathf.Round(t / step)) < 0.001f;

        float ZoomEdgeLeftX() => _trackLeft + _trackWidth * Mathf.Clamp01(_viewStart / Mathf.Max(_rec.duration, 0.001f));

        float ZoomEdgeRightX() => _trackLeft + _trackWidth * Mathf.Clamp01((_viewStart + ViewSpan) / Mathf.Max(_rec.duration, 0.001f));

        RectTransform BuildCutOverlay()
        {
            var rt = UGUIShip.CreatePanel(_cutOverlayRoot, new Rect(0f, LANES_Y, 0f, LANES_H), new Color(0f, 0f, 0f, 0.4f), "CutOverlay");
            var img = rt.GetComponent<Image>();
            img.sprite = _stripeSprite;
            img.type = Image.Type.Tiled;
            img.raycastTarget = false;
            return rt;
        }

        float TrackX(float time) => _trackLeft + _trackWidth * Mathf.Clamp01(TimeToFraction(time));

        // hovering a marker's own band near its stem is what makes it grabbable, so that's what turns
        // it white
        ReplayKeyframe HoveredKeyframe()
        {
            if (_canvas == null) return _dragKeyframe;
            if (_dragKeyframe != null) return _dragKeyframe;

            var cursor = TimelineCursor();
            if (!OverTimeline(cursor) || !OverCameraRow(cursor)) return null;
            return KeyframeNear(TimeAt(cursor.x));
        }

        ReplayVisibilityKeyframe HoveredVisKeyframe()
        {
            if (_canvas == null) return _dragVisKeyframe;
            if (_dragVisKeyframe != null) return _dragVisKeyframe;

            var cursor = TimelineCursor();
            if (!OverTimeline(cursor) || !OverVisRow(cursor)) return null;
            return VisKeyframeNear(TimeAt(cursor.x));
        }

        ReplayPostFxKeyframe HoveredFxKeyframe()
        {
            if (_canvas == null) return _dragFxKeyframe;
            if (_dragFxKeyframe != null) return _dragFxKeyframe;

            var cursor = TimelineCursor();
            if (!OverTimeline(cursor) || !OverFxRow(cursor)) return null;
            return FxKeyframeNear(TimeAt(cursor.x));
        }

        void HandleFrameStepKeys()
        {
            StepOnHold(KeyCode.LeftArrow, -1f, ref _leftArrowHeldAt, ref _leftArrowRepeatAt);
            StepOnHold(KeyCode.RightArrow, 1f, ref _rightArrowHeldAt, ref _rightArrowRepeatAt);
        }

        void StepOnHold(KeyCode key, float dir, ref float heldAt, ref float repeatAt)
        {
            if (Input.GetKeyDown(key))
            {
                heldAt = Time.unscaledTime;
                repeatAt = heldAt + FRAME_HOLD_THRESHOLD;
                StepFrame(dir);
                return;
            }
            if (!Input.GetKey(key)) { heldAt = -1f; return; }
            if (heldAt < 0f) return;

            float now = Time.unscaledTime;
            if (now < repeatAt) return;
            repeatAt = now + FRAME_HOLD_REPEAT;
            StepFrame(dir);
        }

        void OnSpeedChanged(float f) => _speed = Mathf.Lerp(SPEED_MIN, SPEED_MAX, f);

        void TogglePause()
        {
            if (_rec.duration <= 0f) return;
            if (_time >= _rec.trimEnd - 0.001f) _time = _rec.trimStart;
            _paused = !_paused;
            if (!_paused) _freeLook = false;
            _audio.Seek(_time);
            _starchart.Seek(_time);
            _vfx.Seek(_time);
            SyncUi();
        }

        void SeekTo(float time)
        {
            float framed = SnapFrame(time);
            if (ShiftHeld()) framed = MagnetSnap(framed, null, null, null, out _);
            _time = SnapOutOfCut(Mathf.Clamp(framed, _rec.trimStart, _rec.trimEnd));
            _paused = true;
            _freeLook = false;
            _audio.Seek(_time);
            _starchart.Seek(_time);
            _vfx.Seek(_time);
            FollowPlayhead();
            ApplyTime();
            SyncUi();
        }

        void StepFrame(float dir) => SeekTo(_time + dir * FRAME_TIME);

        float SnapOutOfCut(float time)
        {
            for (int i = 0; i < _rec.keyframes.Count; i++)
            {
                var k = _rec.keyframes[i];
                if (!k.cutToNext) continue;

                float end = i + 1 < _rec.keyframes.Count ? _rec.keyframes[i + 1].time : _rec.duration;
                if (time <= k.time || time >= end) continue;

                return time - k.time <= end - time ? k.time : end;
            }
            return time;
        }

        static float SnapFrame(float t) => Mathf.Round(t * FRAME_RATE) / FRAME_RATE;

        // trim handles always magnet onto nearby keyframes when the toggle is on, same as dragging
        // a keyframe does — the playhead itself only gets that via a held shift (see SeekTo)
        float SnapEdge(float raw)
        {
            float framed = SnapFrame(raw);
            return _snapToKeyframes ? MagnetSnap(framed, null, null, null, out _) : framed;
        }

        // magnet snap always lands on the 60fps grid already, since every keyframe time on the
        // record is itself grid-aligned — nothing here needs to re-quantize its result
        float MagnetSnap(float framed, List<ReplayKeyframe> excludeCam, List<ReplayVisibilityKeyframe> excludeVis, List<ReplayPostFxKeyframe> excludeFx, out bool snapped)
        {
            float tol = _trackWidth > 0f ? (KEY_W * 0.75f / _trackWidth) * ViewSpan : 0f;
            float best = framed;
            float bestDelta = tol;
            snapped = false;

            foreach (var k in _rec.keyframes)
            {
                if (excludeCam != null && excludeCam.Contains(k)) continue;
                float d = Mathf.Abs(k.time - framed);
                if (d < bestDelta) { bestDelta = d; best = k.time; snapped = true; }
            }
            foreach (var k in _rec.visibilityKeyframes)
            {
                if (excludeVis != null && excludeVis.Contains(k)) continue;
                float d = Mathf.Abs(k.time - framed);
                if (d < bestDelta) { bestDelta = d; best = k.time; snapped = true; }
            }
            foreach (var k in _rec.postFxKeyframes)
            {
                if (excludeFx != null && excludeFx.Contains(k)) continue;
                float d = Mathf.Abs(k.time - framed);
                if (d < bestDelta) { bestDelta = d; best = k.time; snapped = true; }
            }
            return best;
        }

        bool AdvanceTime(float delta)
        {
            _time += delta;
            if (KeyframeSpan(out var ka, out var kb, out _) && ka != null && ka.cutToNext && kb != null && _time < kb.time)
            {
                _time = kb.time;
                return true;
            }
            return false;
        }

        void SyncUi()
        {
            PositionMarker();
            RefreshKeyframeTicks();
            RefreshVisibilityTicks();
            RefreshPostFxTicks();
            RefreshCutOverlays();
            _clock.text = Stamp(_time) + "  /  " + Stamp(_rec.trimEnd);
            _playLabel.text = _paused ? "paused" : "playing";
            RefreshPlayPauseIcon();
            RefreshSnapIcon();
            if (_deleteBtn.gameObject.activeSelf != _selected.Count > 0)
                _deleteBtn.gameObject.SetActive(_selected.Count > 0);
            _info.text =
                (string.IsNullOrEmpty(_rec.roundName) ? _rec.roundId : _rec.roundName) + "\n" +
                (string.IsNullOrEmpty(_rec.shareCode) ? _rec.sceneName : _rec.shareCode) + (_sceneLoaded ? "" : "  (not loaded)") + "\n" +
                _beans.Count + "/" + _rec.players.Count + " beans · " + _rec.keyframes.Count + " keyframes\n" +
                (_world != null ? _world.Count : 0) + "/" + _rec.worldObjects.Count + " objects · " + _rec.recordedAt;
        }

        static string Stamp(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            var t = TimeSpan.FromSeconds(seconds);
            return string.Format("{0:D2}:{1:D2}.{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
        }

        void Update()
        {
            if (_exiting || _exporting || _cam == null || _loading) return;

            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            if (_picking)
            {
                UpdateTargetPick();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_editingCam) EndCameraEdit();
                else if (ReplayKeyframeWindow.Instance != null || ReplayVisibilityKeyframeWindow.Instance != null) Deselect();
            }

            if (Input.GetMouseButtonDown(0) && !PointerOverUi()) { _worldHeld = true; _worldTap = true; }
            if (_worldHeld && ControllerManager.Stick != Vector2.zero) _worldTap = false;
            bool worldTap = false;
            if (Input.GetMouseButtonUp(0)) { worldTap = _worldHeld && _worldTap; _worldHeld = false; }

            if (_editingCam)
            {
                HandleLook();
                HandleMove();
                HandleFov();
                if (_freeLook) CaptureIntoKeyframe(_editKeyframe);
                if (worldTap) EndCameraEdit();
            }
            else if (!HandleTimelineMouse() && _dragKeyframe == null)
            {
                if (!OverTimeline(TimelineCursor()))
                {
                    if (worldTap) Deselect();
                    HandleLook();
                    HandleMove();
                    HandleFov();
                }
            }

            if (!_editingCam && _hoverZoomEdge) BetterFG.Utilities.Win32CursorUtil.SetSizeWe();

            var es = EventSystem.current;
            var selected = es?.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<InputField>() == null)
                es.SetSelectedGameObject(null);

            if (!TypingInField())
            {
                if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
                if (Input.GetKeyDown(KeyCode.X)) DeleteSelected();
                if (_timelineFocused) HandleFrameStepKeys();
            }

            _audio.Tick();

            if (!_paused && _rec.duration > 0f)
            {
                float from = _time;
                float speed = PlaybackSpeed();
                _shakeTime += Time.unscaledDeltaTime;
                bool cutJump = AdvanceTime(Time.unscaledDeltaTime * speed);

                if (_time >= _rec.trimEnd)
                {
                    _time = _rec.trimEnd;
                    _paused = true;
                }

                _audio.SetSpeed(speed);
                if (cutJump) { _audio.Seek(_time); _starchart.Seek(_time); _vfx.Seek(_time); }
                else { _audio.Advance(from, _time); _starchart.Advance(from, _time); _vfx.Advance(from, _time); }
                FollowPlayhead();
            }
            else _audio.SetSpeed(1f);

            ApplyTime();
            SyncUi();
        }

        void LateUpdate()
        {
            if (_exiting || _world == null) return;
            _world.Apply(_time);
        }

        static ReplayKeyframe Clone(ReplayKeyframe k) => new ReplayKeyframe
        {
            time = k.time,
            cameraType = k.cameraType,
            lookAt = k.lookAt,
            easingCurve = k.easingCurve,
            easingDirection = k.easingDirection,
            position = k.position,
            rotation = k.rotation,
            fov = k.fov,
            targetPlayerId = k.targetPlayerId,
            lookAtPlayerId = k.lookAtPlayerId,
            targetObject = k.targetObject,
            lookAtObject = k.lookAtObject,
            speed = k.speed,
            cut = k.cut,
            cutToNext = k.cutToNext,
            shakeKind = k.shakeKind,
            shakeTier = k.shakeTier,
        };

        static ReplayPostFxKeyframe CloneFx(ReplayPostFxKeyframe k) => new ReplayPostFxKeyframe
        {
            time = k.time,
            exposure = k.exposure,
            contrast = k.contrast,
            saturation = k.saturation,
            temperature = k.temperature,
            tint = k.tint,
            vignette = k.vignette,
            chromaticAberration = k.chromaticAberration,
            bloomIntensity = k.bloomIntensity,
            bloomThreshold = k.bloomThreshold,
            sharpenAmount = k.sharpenAmount,
            sharpenRadius = k.sharpenRadius,
        };

        // flying the camera only writes to a keyframe inside this mode, so opening the editor and
        // nudging the view can't quietly rewrite the shot

        // picking a player in the keyframe window is blind otherwise — there's no telling which of 30
        // beans "Attached to" just landed on. the marker only shows while a player parameter is the
        // thing being touched.

        // the built-in additive shaders are stripped out of the game's build (Shader.Find only ever
        // came back with Sprites/Default, which is why the marker read as flat alpha), so if they
        // aren't there, borrow the blend setup off a material the game itself draws additively.

        // the shot the keyframes describe is invisible from the free cam, so mark it on the replay
        // canvas: the camera icon sits where that camera projects to, and the two lines are its
        // frustum edges projected the same way, so they widen with the keyframed fov.

        // canvas pixels, x right and y down from the top left, the same space SetPixelRect lays out in

        RectTransform BuildGizmoLine()
        {
            var go = new GameObject("Line");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(_gizmoRoot, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);

            var img = go.AddComponent<Image>();
            img.color = GIZMO_LINE_TINT;
            img.raycastTarget = false;
            return rt;
        }

        Quaternion KeyframeRotation(ReplayKeyframe k, Vector3 camPos)
        {
            if (k.cameraType == ReplayCameraType.Gameplay)
            {
                SampleFrames(_rec.cameraFrames, _time, out _, out var camRot);
                return camRot;
            }
            if (k.lookAt != ReplayLookAt.FixedRotation)
            {
                var dir = TargetPositionAt(k.lookAtObject, k.lookAtPlayerId, _time) + Vector3.up - camPos;
                if (dir.sqrMagnitude > 0.0001f) return Quaternion.LookRotation(dir);
            }
            return k.rotation;
        }

        // the shot the keyframes describe at the current time, whether or not the view camera is the
        // thing showing it — the free-cam gizmo draws the same pose.

        Vector2 TimelineCursor()
        {
            float sf = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            return new Vector2(
                Input.mousePosition.x / sf - MARGIN,
                TIMELINE_Y + TIMELINE_H - Input.mousePosition.y / sf);
        }

        // the lane-name gutter pushed the track right, and the old bound cut the last 50-odd pixels off
        // the timeline — which is exactly where the end marker parks
        bool OverTimeline(Vector2 cursor) =>
            cursor.x >= 0f && cursor.x <= _trackLeft + _trackWidth + PAD && cursor.y >= 0f && cursor.y <= TIMELINE_H;

        static bool OverEdgeBand(Vector2 cursor) => cursor.y >= EDGE_Y - 6f && cursor.y < SCRUB_Y;

        static bool OverScrub(Vector2 cursor) => cursor.y >= SCRUB_Y && cursor.y < LANES_Y - 1f;

        static bool OverKeyRow(Vector2 cursor) => cursor.y >= LANES_Y - 1f && cursor.y <= LANES_BOTTOM;

        static bool OverVisRow(Vector2 cursor) => cursor.y >= LANES_Y - 1f && cursor.y < VIS_SPLIT_Y;

        static bool OverFxRow(Vector2 cursor) => cursor.y >= VIS_SPLIT_Y && cursor.y < FX_SPLIT_Y;

        static bool OverCameraRow(Vector2 cursor) => cursor.y >= FX_SPLIT_Y && cursor.y <= LANES_BOTTOM;

        static bool OverZoomBar(Vector2 cursor) => cursor.y >= LANES_BOTTOM && cursor.y <= ZOOMBAR_Y + ZOOMBAR_H + 2f;

        float TimeAt(float x) =>
            FractionToTime(Mathf.Clamp01(_trackWidth > 0f ? (x - _trackLeft) / _trackWidth : 0f));

        ReplayKeyframe KeyframeNear(float time)
        {
            if (_trackWidth <= 0f || _rec.duration <= 0f) return null;
            float tolerance = (KEY_W * 0.75f / _trackWidth) * ViewSpan;

            ReplayKeyframe best = null;
            float bestDelta = float.MaxValue;
            foreach (var k in _rec.keyframes)
            {
                float delta = Mathf.Abs(k.time - time);
                if (delta <= tolerance && delta < bestDelta) { bestDelta = delta; best = k; }
            }
            return best;
        }

        ReplayVisibilityKeyframe VisKeyframeNear(float time)
        {
            if (_trackWidth <= 0f || _rec.duration <= 0f) return null;
            float tolerance = (KEY_W * 0.75f / _trackWidth) * ViewSpan;

            ReplayVisibilityKeyframe best = null;
            float bestDelta = float.MaxValue;
            foreach (var k in _rec.visibilityKeyframes)
            {
                float delta = Mathf.Abs(k.time - time);
                if (delta <= tolerance && delta < bestDelta) { bestDelta = delta; best = k; }
            }
            return best;
        }

        ReplayPostFxKeyframe FxKeyframeNear(float time)
        {
            if (_trackWidth <= 0f || _rec.duration <= 0f) return null;
            float tolerance = (KEY_W * 0.75f / _trackWidth) * ViewSpan;

            ReplayPostFxKeyframe best = null;
            float bestDelta = float.MaxValue;
            foreach (var k in _rec.postFxKeyframes)
            {
                float delta = Mathf.Abs(k.time - time);
                if (delta <= tolerance && delta < bestDelta) { bestDelta = delta; best = k; }
            }
            return best;
        }

        static bool CtrlHeld() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        static bool TypingInField()
        {
            var sel = EventSystem.current?.currentSelectedGameObject;
            return sel != null && sel.GetComponent<InputField>() != null;
        }

        static bool PointerOverUi() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        string ReplayName()
        {
            if (!string.IsNullOrEmpty(_rec.name)) return _rec.name;
            if (!string.IsNullOrEmpty(_rec.roundName)) return _rec.roundName;
            return string.IsNullOrEmpty(_rec.roundId) ? "replay" : _rec.roundId;
        }

        string SafeName() => string.Concat(ReplayName().Split(System.IO.Path.GetInvalidFileNameChars()));

        void OnExportWindowClosed() => _windowRt.gameObject.SetActive(!_minimized);

        // the video pass is stepped, so nothing can be recorded off it. this plays the replay through
        // once at real speed with the system output looped back into a wav — same speed curve as the
        // video, so the two line up end to end.

        void LoadFrom()
        {
            WinDialogs.PickFile("Open replay", new Action<string>(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                var loaded = LoadReplay.Read(path);
                Swap(loaded);
            }), SaveReplay.PickerFilter);
        }

        void Exit()
        {
            if (_exiting) return;

            _exiting = true;
            LoadingScreenService.Show();
            _exitStart = Time.realtimeSinceStartup;
            Instance = null;
            DiscordPresenceService.OnReplayViewerClosed();
            StopAllCoroutines();
            StartCoroutine(ExitRoutine().WrapToIl2Cpp());
        }

        IEnumerator ExitRoutine()
        {
            ReplayKeyframeWindow.Instance?.Close();
            ReplayVisibilityKeyframeWindow.Instance?.Close();
            ReplayPostFxKeyframeWindow.Instance?.Close();
            _audio.Release();
            _world?.Release();
            _speech?.Release();
            _postFx?.Release();
            RestoreCamera();

            foreach (var bean in _beans)
                if (bean != null) Destroy(bean);
            _beans.Clear();

            if (_canvas != null) Destroy(_canvas.gameObject);

            if (_prevActiveScene.IsValid() && _prevActiveScene.isLoaded)
                SceneManager.SetActiveScene(_prevActiveScene);

            yield return StartCoroutine(UnloadLevel().WrapToIl2Cpp());

            int restored = _hidden.Count;
            ShowGame();

            int orphans = FmodUtil.StopSnapshots(null);
            Plugin.Log.LogInfo($"out of the replay viewer, {restored} game roots back on, {orphans} snapshots killed before they could outlive the client");

            // hold the loading screen up (and finish its outro) BEFORE kicking the reload - reloading
            // first let the game's own scene transition race our fade and stomp it, which is why it
            // looked like the loading screen vanished instantly
            float remaining = _exitStart + 1f - Time.realtimeSinceStartup;
            if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);
            yield return StartCoroutine(LoadingScreenService.HideRoutine().WrapToIl2Cpp());

            var client = GlobalGameStateClient.Instance;
            if (client != null)
            {
                client.ForceMainMenuSceneReload = true;
                client.ReloadGame(true, EnumDisconnectReasonGraceful.NoReason);
            }

            Destroy(gameObject);
        }

        IEnumerator UnloadLevel()
        {
            for (int i = _sceneHandles.Count - 1; i >= 0; i--)
            {
                var unload = Addressables.UnloadSceneAsync(_sceneHandles[i], true);
                while (!unload.IsDone) yield return null;
            }
            _sceneHandles.Clear();
            _loadedScenes.Clear();
            _sceneLoaded = false;

            if (!_touchedFraggle) yield break;

            var fcm = SingletonBehaviour<FraggleCommonManager>.Instance;
            if (fcm != null) fcm.LevelLoader = _prevLevelLoader;
            LevelIO.ResetObjects();
            _touchedFraggle = false;
        }

        void RestoreCamera()
        {
            foreach (var brain in _brains)
                if (brain != null) brain.enabled = true;
            _brains.Clear();

            if (!_tookCamera || _cam == null) return;
            _tookCamera = false;

            var t = _cam.transform;
            t.SetParent(_camParent, false);
            t.localPosition = _camLocalPos;
            t.localRotation = _camLocalRot;
            _cam.fieldOfView = _camFov;
        }
    }
}
