using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
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
    public class ReplayViewer : MonoBehaviour
    {
        public ReplayViewer(IntPtr ptr) : base(ptr) { }

        public static ReplayViewer Instance;

        const float MARGIN = 16f;
        const float WIN_W = 320f;
        const float WIN_H = 216f;
        const float ROW = 22f;
        const float PAD = 8f;
        const float HEADER_H = ROW + 6f;
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
        const int LANE_COUNT = 2;
        const float LANES_Y = SCRUB_Y + SCRUB_H + 3f;
        const float CAM_LANE_Y = LANES_Y + (LANE_COUNT - 1) * (LANE_H + LANE_GAP);
        const float LANES_H = CAM_LANE_Y + LANE_H - LANES_Y;
        const float KEYROW_Y = CAM_LANE_Y + (LANE_H - KEY_H) * 0.5f;
        const float TIMELINE_H = CAM_LANE_Y + LANE_H + 6f;
        const float CONTROLS_H = ROW + 8f;
        const float TIMELINE_Y = MARGIN + CONTROLS_H + 8f;
        const float MIN_VIEW_SPAN = 0.5f;
        const float MIN_EXPORT_SPEED = 0.05f;
        const float SPEED_MIN = 2f;
        const float SPEED_MAX = 90f;
        const float FOV_MIN = 15f;
        const float FOV_MAX = 120f;
        const int TICKS = 8;
        const float MENU_W = 168f;
        const float DRAG_DEADZONE = 5f;
        const float DUP_GAP = 0.5f;
        const float DOUBLE_CLICK = 0.35f;

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
        ReplaySpeechPlayer _speech;
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
        bool _paused = true;
        float _yaw;
        float _pitch;
        float _speed = 14f;
        Vector3 _lastMouse;
        bool _looking;

        Text _clock;
        Text _info;
        Text _playLabel;
        Button _deleteBtn;
        Button _restoreBtn;
        Button _doneBtn;
        bool _minimized;
        bool _editingCam;
        bool _picking;
        bool _pickObjects;
        bool _pickWasFree;
        bool _pickLookTarget;
        ReplayKeyframe _pickKeyframe;
        List<int> _pickAllowed;
        int _pickHover = -1;
        uint _pickHoverPlayer;
        Text _pickHint;
        ReplayKeyframe _editKeyframe;
        readonly List<GameObject> _hiddenUi = new List<GameObject>();
        bool _exporting;
        string _exportWav;
        float _exportAudioLead;
        readonly List<Text> _rulerLabels = new List<Text>();
        RectTransform _windowRt;
        RectTransform _marker;
        RectTransform _inMarker;
        RectTransform _outMarker;
        RectTransform _dimLeft;
        RectTransform _dimRight;
        Image _playheadImg;
        Image _inFill;
        Image _outFill;
        bool _draggingIn;
        bool _draggingOut;
        RectTransform _timelineRt;
        RectTransform _contextMenu;
        RectTransform _keyTicks;
        readonly List<ReplayKeyframe> _selected = new List<ReplayKeyframe>();
        readonly List<float> _dragStartTimes = new List<float>();
        ReplayKeyframe _dragKeyframe;
        float _dragGrabTime;
        float _dragGrabMouseX;
        bool _dragMoved;
        ReplayKeyframe _clickedKeyframe;
        float _clickedAt;
        bool _freeLook;
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
            viewer.StartCoroutine(viewer.OpenRoutine().WrapToIl2Cpp());
        }

        IEnumerator OpenRoutine()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _audio = new ReplayAudioPlayer(_rec);
            _prevActiveScene = SceneManager.GetActiveScene();

            HideGame();
            yield return StartCoroutine(LoadLevel().WrapToIl2Cpp());
            yield return null;
            ApplySets();
            yield return StartCoroutine(LoadBeanPrefab().WrapToIl2Cpp());

            Build();

            _world = new ReplayWorldPlayer(_rec, _loadedScenes);
            yield return StartCoroutine(_world.Prepare().WrapToIl2Cpp());

            SpawnBeans();
            SyncUi();
            Plugin.Log.LogInfo($"replay viewer up: {_rec.roundName} on {_rec.sceneName}, {_beans.Count} beans, {_rec.duration:0.0}s");

            yield return StartCoroutine(_audio.Prepare(_cam.transform).WrapToIl2Cpp());
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
            CloseContextMenu();
            _audio.Release();
            _world?.Release();
            _world = null;
            _speech?.Release();
            _speech = null;

            foreach (var bean in _beans)
                if (bean != null) Destroy(bean);
            _beans.Clear();
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
            _paused = true;
            _freeLook = false;
            _selected.Clear();
            _dragKeyframe = null;
            _scrubbing = false;
            EndMarquee();
            _audio = new ReplayAudioPlayer(_rec);

            yield return StartCoroutine(LoadLevel().WrapToIl2Cpp());
            yield return null;
            ApplySets();
            if (_beanPrefab == null) yield return StartCoroutine(LoadBeanPrefab().WrapToIl2Cpp());

            MatchScene();
            Destroy(_timelineRt.gameObject);
            BuildTimeline();
            KillNavigation();

            _world = new ReplayWorldPlayer(_rec, _loadedScenes);
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
            }

            _speech = new ReplaySpeechPlayer(_rec, transform);
            _speech.Prepare(speakers);
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

            if (replacesBean) SkinApplicationService.ApplyEntriesToBean(p.bfgTextures, bean);
            else ApplyLook(bean, p, new Action(() => SkinApplicationService.ApplyEntriesToBean(p.bfgTextures, bean)));
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
            _speech?.Apply(_time, _cam);
            UpdateHighlight();
            UpdateCameraGizmo();
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
            if (fgcc != null) Destroy(fgcc);

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
            _scrubbing = false;
            _draggingIn = false;
            _draggingOut = false;
            _dragKeyframe = null;

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
            const float rowW = 168f;
            var row = UGUIShip.CreatePanel(_canvas.transform, new Rect(0f, 0f, rowW, CONTROLS_H), Color.clear, "ReplayControls");
            row.anchorMin = new Vector2(0f, 0f);
            row.anchorMax = new Vector2(0f, 0f);
            row.pivot = new Vector2(0f, 0f);
            row.anchoredPosition = new Vector2(MARGIN, MARGIN);
            row.sizeDelta = new Vector2(rowW, CONTROLS_H);
            row.GetComponent<Image>().raycastTarget = false;

            UGUIShip.CreateButton(row, new Rect(0f, 0f, 80f, CONTROLS_H), "PLAY", BTN_GREEN, Color.white, UIScale.FS_SM, new Action(TogglePause));
            _deleteBtn = UGUIShip.CreateButton(row, new Rect(88f, 0f, 80f, CONTROLS_H), "DELETE", BTN_RED, Color.white, UIScale.FS_SM, new Action(DeleteSelected));
            _deleteBtn.gameObject.SetActive(false);
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

        void SetupCamera()
        {
            var existing = CameraUtils.GetMainCamera();
            if (existing != null)
            {
                TakeOverCamera(existing);
                return;
            }

            Plugin.Log.LogWarning("level had no main camera, falling back to a bare one");
            MakeBareCamera(new Vector3(0f, 8f, -18f), Quaternion.identity, 60f);
            _pitch = 12f;
            ApplyPostProcessing();
        }

        void MakeBareCamera(Vector3 pos, Quaternion rot, float fov)
        {
            var camGo = new GameObject("BettrFG_ReplayCam");
            camGo.transform.SetParent(transform, false);
            camGo.transform.SetPositionAndRotation(pos, rot);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = _sceneLoaded ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.08f, 0.09f, 0.12f);
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 4000f;
            _cam.cullingMask = ~0;
            _cam.fieldOfView = fov;
            SeedLook();
        }

        void TakeOverCamera(Camera cam)
        {
            _cam = cam;
            foreach (var brain in _cam.GetComponentsInChildren<CinemachineBrain>(true))
            {
                if (!brain.enabled) continue;
                brain.enabled = false;
                _brains.Add(brain);
            }

            var t = _cam.transform;
            _camParent = t.parent;
            _camLocalPos = t.localPosition;
            _camLocalRot = t.localRotation;
            _camFov = _cam.fieldOfView;
            _tookCamera = true;

            t.SetParent(null, true);
            SeedLook();
            Plugin.Log.LogInfo($"freecam took over {_cam.gameObject.name}, its post-processing comes along");
        }

        void SeedLook()
        {
            var euler = _cam.transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }

        bool FromLoadedLevel(GameObject go)
        {
            var scene = go.scene;
            foreach (var loaded in _loadedScenes)
                if (loaded == scene) return true;
            return false;
        }

        void AdoptLevelCamera()
        {
            if (FromLoadedLevel(_cam.gameObject)) return;

            var level = CameraUtils.GetMainCamera();
            if (level == null || level == _cam || !FromLoadedLevel(level.gameObject))
            {
                ApplyPostProcessing();
                return;
            }

            var pos = _cam.transform.position;
            var rot = _cam.transform.rotation;
            float fov = _cam.fieldOfView;

            if (_tookCamera) RestoreCamera();
            else Destroy(_cam.gameObject);

            TakeOverCamera(level);
            _cam.transform.SetPositionAndRotation(pos, rot);
            _cam.fieldOfView = fov;
            SeedLook();
        }

        void ApplyPostProcessing()
        {
            PostProcessLayer template = null;
            foreach (var layer in Resources.FindObjectsOfTypeAll<PostProcessLayer>())
            {
                if (layer == null || layer.m_Resources == null || layer.gameObject == _cam.gameObject) continue;
                if (template == null) template = layer;
                if (layer.GetComponent<Camera>() == null) continue;
                if (FromLoadedLevel(layer.gameObject)) { template = layer; break; }
                if (layer.gameObject.scene.IsValid()) template = layer;
            }
            if (template == null)
            {
                Plugin.Log.LogWarning("nothing to copy post-processing from, replay cam renders raw");
                return;
            }

            int mask = template.volumeLayer.value;
            if (mask == 0) mask = ~0;

            _cam.allowHDR = true;

            var ppl = _cam.GetComponent<PostProcessLayer>();
            if (ppl == null) ppl = _cam.gameObject.AddComponent<PostProcessLayer>();
            ppl.enabled = false;
            ppl.Init(template.m_Resources);
            ppl.volumeLayer = mask;
            ppl.volumeTrigger = _cam.transform;
            ppl.antialiasingMode = template.antialiasingMode;
            ppl.stopNaNPropagation = template.stopNaNPropagation;
            ppl.enabled = true;

            int volumes = Resources.FindObjectsOfTypeAll<PostProcessVolume>().Length;
            Plugin.Log.LogInfo($"post-processing off {template.gameObject.name} (scene '{template.gameObject.scene.name}'), volumeLayer {mask}, AA {template.antialiasingMode}, {volumes} volumes around");
        }

        void BuildWindow()
        {
            var win = UGUIShip.CreatePanel(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H), Color.clear, "ReplayWindow");
            _windowRt = win;
            ReplayWindowKit.MainBackdrop(win);
            ReplayWindowKit.Title(win, WIN_W, "BettrFG Replay", TITLE_POS);
            UGUIShip.CreateButton(win, new Rect(WIN_W - 30f, 3f, 26f, 20f), "<<", BTN_DARK, Color.white, UIScale.FS_SM, new Action(Minimize));

            _restoreBtn = UGUIShip.CreateButton(_canvas.transform, new Rect(2f, 2f, 34f, 20f), ">>",
                BTN_DARK, Color.white, UIScale.FS_SM, new Action(Restore));
            _restoreBtn.gameObject.SetActive(false);

            _doneBtn = UGUIShip.CreateButton(_canvas.transform, new Rect(2f, 2f, 62f, 20f), "DONE",
                BTN_GREEN, Color.white, UIScale.FS_SM, new Action(EndCameraEdit));
            _doneBtn.gameObject.SetActive(false);

            float y = 26f + PAD;
            _info = UGUIShip.CreateLabel(win, new Rect(PAD, y, WIN_W - PAD * 2f, ROW * 4f), "", UIScale.FS_SM, HINT, TextAnchor.UpperLeft);
            y += ROW * 4f + 2f;

            var nameField = UGUIShip.CreateInputField(win, new Rect(PAD, y, WIN_W - PAD * 2f, ROW),
                "replay name...", new Color(0f, 0f, 0f, 0.55f), Color.white, UIScale.FS_SM);
            nameField.text = ReplayName();
            nameField.onEndEdit.AddListener(new Action<string>(OnNameChanged));
            y += ROW + PAD;

            float bw = (WIN_W - PAD * 5f) / 4f;
            UGUIShip.CreateButton(win, new Rect(PAD, y, bw, ROW + 4f), "Save", BTN_DARK, Color.white, UIScale.FS_SM, new Action(SaveAs));
            UGUIShip.CreateButton(win, new Rect(PAD * 2f + bw, y, bw, ROW + 4f), "Load", BTN_DARK, Color.white, UIScale.FS_SM, new Action(LoadFrom));
            UGUIShip.CreateButton(win, new Rect(PAD * 3f + bw * 2f, y, bw, ROW + 4f), "Export", BTN_DARK, Color.white, UIScale.FS_SM, new Action(OpenExportWindow));
            UGUIShip.CreateButton(win, new Rect(PAD * 4f + bw * 3f, y, bw, ROW + 4f), "Exit", BTN_RED, Color.white, UIScale.FS_SM, new Action(Exit));
            y += ROW + 4f + PAD;

            UGUIShip.CreateSlider(win, PAD, y, WIN_W - PAD * 2f, "", Mathf.InverseLerp(SPEED_MIN, SPEED_MAX, _speed), ROW, PAD, UIScale.FS_SM,
                new Action<float>(OnSpeedChanged), null, null, false, Mathf.InverseLerp(SPEED_MIN, SPEED_MAX, 14f));
        }

        void BuildTimeline()
        {
            float w = UIScaleService.CurrentRef.x - MARGIN * 2f;
            var bar = UGUIShip.CreatePanel(_canvas.transform, new Rect(0f, 0f, w, TIMELINE_H), Color.clear, "ReplayTimeline");
            _timelineRt = bar;
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(0f, 0f);
            bar.pivot = new Vector2(0f, 0f);
            bar.anchoredPosition = new Vector2(MARGIN, TIMELINE_Y);
            bar.sizeDelta = new Vector2(w, TIMELINE_H);
            BuildTimelineBackdrop(bar, w);

            _playLabel = UGUIShip.CreateLabel(bar, new Rect(PAD, PAD, 200f, HEADER_H), "", UIScale.FS_SM, HINT);
            _clock = UGUIShip.CreateLabel(bar, new Rect(w - 190f - PAD, PAD, 190f, HEADER_H), "", UIScale.FS, Color.white, TextAnchor.MiddleRight);

            _trackLeft = PAD + LANE_NAME_W;
            float trackW = w - _trackLeft - PAD;
            _trackWidth = trackW;
            _viewStart = 0f;
            _viewSpan = _rec.duration;
            NormaliseTrim();

            // the striped strip is the scrub band: it sits exactly where the playhead's grab handle
            // is, and everything below it is lanes.
            var scrub = UGUIShip.CreatePanel(bar, new Rect(_trackLeft, SCRUB_Y, trackW, SCRUB_H), new Color(1f, 1f, 1f, 0.12f), "ScrubBand");
            if (_stripeSprite == null) _stripeSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.stripe.png", 400f);
            var scrubImg = scrub.GetComponent<Image>();
            scrubImg.sprite = _stripeSprite;
            scrubImg.type = Image.Type.Tiled;
            scrubImg.raycastTarget = false;

            _rulerLabels.Clear();
            for (int i = 0; i <= TICKS; i++)
            {
                float f = i / (float)TICKS;
                float x = _trackLeft + trackW * f;
                UGUIShip.CreatePanel(bar, new Rect(x, SCRUB_Y, 1f, SCRUB_H), TICK, "Tick");
                _rulerLabels.Add(UGUIShip.CreateLabel(bar, new Rect(x - 28f, TICKS_Y, 56f, 14f),
                    "", UIScale.FS_SM - 2, TICK, TextAnchor.MiddleCenter));
            }
            RefreshRuler();

            for (int i = 0; i < LANE_COUNT; i++)
            {
                float y = LANES_Y + i * (LANE_H + LANE_GAP);
                UGUIShip.CreatePanel(bar, new Rect(_trackLeft, y, trackW, LANE_H), LANE_BG, "Lane").GetComponent<Image>().raycastTarget = false;

                bool camera = i == LANE_COUNT - 1;
                UGUIShip.CreateLabel(bar, new Rect(PAD, y, LANE_NAME_W - 6f, LANE_H),
                    camera ? "Camera" : "", UIScale.FS_SM - 1, camera ? HINT : TICK, TextAnchor.MiddleRight);
            }

            _dimLeft = UGUIShip.CreatePanel(bar, new Rect(_trackLeft, LANES_Y, 0f, LANES_H), OUTSIDE_DIM, "OutsideLeft");
            _dimLeft.GetComponent<Image>().raycastTarget = false;
            _dimRight = UGUIShip.CreatePanel(bar, new Rect(_trackLeft, LANES_Y, 0f, LANES_H), OUTSIDE_DIM, "OutsideRight");
            _dimRight.GetComponent<Image>().raycastTarget = false;

            var ticksGo = new GameObject("KeyframeTicks");
            _keyTicks = ticksGo.AddComponent<RectTransform>();
            _keyTicks.SetParent(bar, false);
            UGUIShip.SetPixelRect(_keyTicks, new Rect(0f, 0f, w, TIMELINE_H));

            if (_playheadFill == null)
            {
                _playheadFill = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.playhead_fill.png");
                _playheadOutline = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.playhead_outline.png");
                _edgeFill = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.edgemarker_fill.png");
                _edgeOutline = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.edgemarker_outline.png");
            }

            _inMarker = BuildMarker(bar, "TrimIn", EDGE_W, EDGE_H, _edgeOutline, _edgeFill, out _inFill);
            _outMarker = BuildMarker(bar, "TrimOut", EDGE_W, EDGE_H, _edgeOutline, _edgeFill, out _outFill);
            _outMarker.localScale = new Vector3(-1f, 1f, 1f);

            _marker = BuildMarker(bar, "Playhead", PH_W, PH_H, _playheadOutline, _playheadFill, out _playheadImg);
        }

        // the slab covers the bar and the camera hangs above its top edge, so the image is taller than
        // the timeline by exactly the sliced top border
        void BuildTimelineBackdrop(RectTransform bar, float w)
        {
            if (_timelineBg == null)
            {
                var tex = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.replay.timeline.png");
                if (tex == null) return;

                _timelineBg = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), Vector2.zero,
                    100f / BG_SCALE, 0, SpriteMeshType.FullRect,
                    new Vector4(BG_BORDER_L, BG_BORDER_B, BG_BORDER_R, BG_BORDER_T));
                _timelineBg.hideFlags = HideFlags.HideAndDontSave;
            }

            var rt = UGUIShip.CreatePanel(bar, new Rect(0f, -BG_OVERHANG, w, TIMELINE_H + BG_OVERHANG), Color.white, "Backdrop");
            var img = rt.GetComponent<Image>();
            img.sprite = _timelineBg;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = BG_PPU_MULT;
            img.raycastTarget = false;
            rt.SetAsFirstSibling();
        }

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

        void RefreshRuler()
        {
            for (int i = 0; i < _rulerLabels.Count; i++)
                _rulerLabels[i].text = Stamp(FractionToTime(i / (float)TICKS));
        }

        void ZoomTimeline(float notches, float pivot)
        {
            float duration = Mathf.Max(_rec.duration, MIN_VIEW_SPAN);
            float anchor = Mathf.Clamp01(TimeToFraction(pivot));
            _viewSpan = Mathf.Clamp(ViewSpan * Mathf.Pow(0.82f, notches), MIN_VIEW_SPAN, duration);
            _viewStart = Mathf.Clamp(pivot - anchor * _viewSpan, 0f, duration - _viewSpan);
            RefreshRuler();
        }

        void PanTimeline(float notches)
        {
            float duration = Mathf.Max(_rec.duration, MIN_VIEW_SPAN);
            if (ViewSpan >= duration) return;
            _viewStart = Mathf.Clamp(_viewStart - notches * ViewSpan * 0.15f, 0f, duration - ViewSpan);
            RefreshRuler();
        }

        void FollowPlayhead()
        {
            if (ViewSpan >= _rec.duration) return;
            if (_time >= _viewStart && _time <= _viewStart + ViewSpan) return;
            _viewStart = Mathf.Clamp(_time - ViewSpan * 0.5f, 0f, _rec.duration - ViewSpan);
            RefreshRuler();
        }

        void RefreshKeyframeTicks()
        {
            if (_keyTicks.childCount != _rec.keyframes.Count)
            {
                for (int i = _keyTicks.childCount - 1; i >= 0; i--)
                    DestroyImmediate(_keyTicks.GetChild(i).gameObject);

                for (int i = 0; i < _rec.keyframes.Count; i++)
                    BuildKeyframeTick();
            }

            var editing = ReplayKeyframeWindow.Instance?.Keyframe;
            var hovered = HoveredKeyframe();
            for (int i = 0; i < _rec.keyframes.Count && i < _keyTicks.childCount; i++)
            {
                var k = _rec.keyframes[i];
                float f = TimeToFraction(k.time);

                var rt = _keyTicks.GetChild(i).GetComponent<RectTransform>();
                bool onScreen = f >= -0.01f && f <= 1.01f;
                if (rt.gameObject.activeSelf != onScreen) rt.gameObject.SetActive(onScreen);
                if (!onScreen) continue;

                rt.anchoredPosition = new Vector2(_trackLeft + _trackWidth * f - KEY_W * 0.5f, -KEYROW_Y);

                var fill = rt.GetChild(0).GetComponent<Image>();
                fill.color = k == hovered ? Color.white
                    : k == editing ? KEY_EDITING
                    : _selected.Contains(k) ? KEY_SELECTED
                    : KEY_IDLE;
            }
        }

        void BuildKeyframeTick()
        {
            if (_fillSprite == null)
            {
                _fillSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.marker_fill.png");
                _outlineSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.marker_outline.png");
            }

            var tick = UGUIShip.CreatePanel(_keyTicks, new Rect(0f, KEYROW_Y, KEY_W, KEY_H), Color.clear, "Keyframe");
            tick.GetComponent<Image>().raycastTarget = false;

            var fill = UGUIShip.CreatePanel(tick, new Rect(0f, 0f, KEY_W, KEY_H), KEY_IDLE, "Fill");
            var fillImg = fill.GetComponent<Image>();
            fillImg.sprite = _fillSprite;
            fillImg.raycastTarget = false;

            var outline = UGUIShip.CreatePanel(tick, new Rect(0f, 0f, KEY_W, KEY_H), KEY_OUTLINE, "Outline");
            var outlineImg = outline.GetComponent<Image>();
            outlineImg.sprite = _outlineSprite;
            outlineImg.raycastTarget = false;
        }

        void NormaliseTrim()
        {
            if (_rec.trimEnd <= 0f || _rec.trimEnd > _rec.duration) _rec.trimEnd = _rec.duration;
            _rec.trimStart = Mathf.Clamp(_rec.trimStart, 0f, Mathf.Max(0f, _rec.trimEnd - MIN_TRIM));
        }

        float TrackX(float time) => _trackLeft + _trackWidth * Mathf.Clamp01(TimeToFraction(time));

        void PositionMarker()
        {
            _marker.anchoredPosition = new Vector2(TrackX(_time) - PH_W * PH_STEM, -SCRUB_Y);
            _playheadImg.color = _scrubbing || NearMarker(_time, false) ? Color.white : PLAYHEAD_YELLOW;

            float inX = TrackX(_rec.trimStart);
            float outX = TrackX(_rec.trimEnd);
            _inMarker.anchoredPosition = new Vector2(inX - EDGE_W * EDGE_STEM, -EDGE_Y);
            _outMarker.anchoredPosition = new Vector2(outX + EDGE_W * EDGE_STEM, -EDGE_Y);
            _inFill.color = _draggingIn || NearMarker(_rec.trimStart, true) ? Color.white : EDGE_BLUE;
            _outFill.color = _draggingOut || NearMarker(_rec.trimEnd, true) ? Color.white : EDGE_BLUE;

            UGUIShip.SetPixelRect(_dimLeft, new Rect(_trackLeft, LANES_Y, inX - _trackLeft, LANES_H));
            UGUIShip.SetPixelRect(_dimRight, new Rect(outX, LANES_Y, _trackLeft + _trackWidth - outX, LANES_H));
        }

        // hovering a marker's own band near its stem is what makes it grabbable, so that's what turns
        // it white
        ReplayKeyframe HoveredKeyframe()
        {
            if (_canvas == null) return _dragKeyframe;
            if (_dragKeyframe != null) return _dragKeyframe;

            var cursor = TimelineCursor();
            if (!OverTimeline(cursor) || !OverKeyRow(cursor)) return null;
            return KeyframeNear(TimeAt(cursor.x));
        }

        bool NearMarker(float time, bool edgeBand)
        {
            if (_canvas == null) return false;
            var cursor = TimelineCursor();
            if (edgeBand ? !OverEdgeBand(cursor) : !OverScrub(cursor)) return false;
            return Mathf.Abs(cursor.x - TrackX(time)) <= GRAB_PX;
        }

        void OnSpeedChanged(float f) => _speed = Mathf.Lerp(SPEED_MIN, SPEED_MAX, f);

        void TogglePause()
        {
            if (_rec.duration <= 0f) return;
            if (_time >= _rec.trimEnd - 0.001f) _time = _rec.trimStart;
            _paused = !_paused;
            if (!_paused) _freeLook = false;
            _audio.Seek(_time);
            SyncUi();
        }

        void SeekTo(float time)
        {
            _time = Mathf.Clamp(time, _rec.trimStart, _rec.trimEnd);
            _paused = true;
            _freeLook = false;
            _audio.Seek(_time);
            ApplyTime();
            SyncUi();
        }

        void SyncUi()
        {
            FollowPlayhead();
            PositionMarker();
            RefreshKeyframeTicks();
            _clock.text = Stamp(_time) + "  /  " + Stamp(_rec.trimEnd);
            _playLabel.text = _paused ? "paused" : "playing";
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
            if (_exiting || _exporting || _cam == null) return;

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

            if (_editingCam)
            {
                HandleLook();
                HandleMove();
                HandleFov();
                if (_freeLook) CaptureIntoKeyframe(_editKeyframe);
                if (Input.GetMouseButtonDown(0) && !PointerOverUi()) EndCameraEdit();
            }
            else if (!HandleTimelineMouse() && _dragKeyframe == null)
            {
                if (Input.GetMouseButtonDown(0) && !PointerOverUi()) Deselect();
                HandleLook();
                HandleMove();
                HandleFov();
            }

            var es = EventSystem.current;
            var selected = es?.currentSelectedGameObject;
            if (selected != null && selected.GetComponent<InputField>() == null)
                es.SetSelectedGameObject(null);

            if (!TypingInField())
            {
                if (Input.GetKeyDown(KeyCode.Space)) TogglePause();
                if (Input.GetKeyDown(KeyCode.X)) DeleteSelected();
            }

            _audio.Tick();

            if (!_paused && _rec.duration > 0f)
            {
                float from = _time;
                float speed = PlaybackSpeed();
                _time += Time.unscaledDeltaTime * speed;
                if (_time >= _rec.trimEnd)
                {
                    _time = _rec.trimEnd;
                    _paused = true;
                }
                _audio.SetSpeed(speed);
                _audio.Advance(from, _time);
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

        void HandleLook()
        {
            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
                _looking = true;
                _lastMouse = Input.mousePosition;
                SeedLook();
            }
            if (Input.GetMouseButtonUp(1)) _looking = false;
            if (!_looking) return;

            Vector3 now = Input.mousePosition;
            Vector3 delta = now - _lastMouse;
            _lastMouse = now;

            if (delta.sqrMagnitude > 0f) _freeLook = true;
            _yaw += delta.x * 0.18f;
            _pitch = Mathf.Clamp(_pitch - delta.y * 0.18f, -89f, 89f);
            _cam.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void HandleMove()
        {
            if (TypingInField()) return;

            var dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) dir -= Vector3.forward;
            if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
            if (Input.GetKey(KeyCode.A)) dir -= Vector3.right;
            if (Input.GetKey(KeyCode.E)) dir += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) dir -= Vector3.up;
            if (dir == Vector3.zero) return;

            _freeLook = true;
            float mul = ShiftHeld() ? 3.5f : 1f;
            var t = _cam.transform;
            var world = t.rotation * new Vector3(dir.x, 0f, dir.z) + Vector3.up * dir.y;
            t.position += world.normalized * _speed * mul * Time.unscaledDeltaTime;
        }

        void ShowContextMenu()
        {
            OpenMenu(_time, 1);
            MenuItem(0, "Add camera keyframe", new Action(AddKeyframeHere));
        }

        void ShowKeyframeMenu(ReplayKeyframe k)
        {
            OpenMenu(k.time, 2);
            MenuItem(0, "Duplicate", new Action(() => { CloseContextMenu(); Duplicate(k); }));
            MenuItem(1, _selected.Count > 1 ? "Delete " + _selected.Count + " keyframes" : "Delete",
                new Action(DeleteSelected));
        }

        void OpenMenu(float atTime, int rows)
        {
            CloseContextMenu();

            float h = rows * (ROW + 4f) + 4f;
            float f = _rec.duration > 0f ? Mathf.Clamp01(atTime / _rec.duration) : 0f;
            float x = Mathf.Min(_trackLeft + _trackWidth * f + 8f, _trackLeft + _trackWidth - MENU_W);
            _contextMenu = UGUIShip.CreatePanel(_timelineRt, new Rect(x, -h - 6f, MENU_W, h),
                MENU_BG, "KeyframeMenu");
        }

        void MenuItem(int row, string label, Action onClick)
        {
            var btn = UGUIShip.CreateButton(_contextMenu, new Rect(4f, 4f + row * (ROW + 4f), MENU_W - 8f, ROW),
                label, BTN_DARK, Color.white, UIScale.FS_SM, onClick);
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
        }

        void CloseContextMenu()
        {
            if (_contextMenu == null) return;
            Destroy(_contextMenu.gameObject);
            _contextMenu = null;
        }

        void Duplicate(ReplayKeyframe k)
        {
            var copy = Clone(k);
            copy.time = Mathf.Min(k.time + DUP_GAP, _rec.duration);
            _rec.keyframes.Add(copy);
            _rec.SortKeyframes();
            SelectOnly(copy);
            Plugin.Log.LogInfo($"copied the {Stamp(k.time)} keyframe over to {Stamp(copy.time)}");
            SyncUi();
        }

        void DeleteSelected()
        {
            CloseContextMenu();
            if (_selected.Count == 0) return;

            int gone = 0;
            foreach (var k in _selected)
                if (_rec.keyframes.Remove(k)) gone++;
            _selected.Clear();
            _dragKeyframe = null;

            var window = ReplayKeyframeWindow.Instance;
            if (window != null && !_rec.keyframes.Contains(window.Keyframe)) window.Close();

            Plugin.Log.LogInfo($"deleted {gone}, {_rec.keyframes.Count} keyframes left");
            ApplyTime();
            SyncUi();
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
        };

        void AddKeyframeHere()
        {
            CloseContextMenu();

            ReplayKeyframe kf;
            if (KeyframeSpan(out var neighbour, out _, out _)) kf = Clone(neighbour);
            else
            {
                kf = new ReplayKeyframe();
                if (_rec.players.Count > 0)
                {
                    kf.targetPlayerId = _rec.players[0].playerId;
                    kf.lookAtPlayerId = _rec.players[0].playerId;
                }
            }

            kf.time = _time;
            CaptureIntoKeyframe(kf);

            _rec.keyframes.Add(kf);
            _rec.SortKeyframes();
            SelectOnly(kf);
            _freeLook = false;
            Plugin.Log.LogInfo($"keyframe at {Stamp(kf.time)}, {_rec.keyframes.Count} on this replay now");

            OpenKeyframeWindow(kf);
        }

        // flying the camera only writes to a keyframe inside this mode, so opening the editor and
        // nudging the view can't quietly rewrite the shot
        public void BeginCameraEdit(ReplayKeyframe kf)
        {
            _editKeyframe = kf;
            _editingCam = true;
            _freeLook = false;
            ClearPlayerHighlight();
            SeekTo(kf.time);

            _hiddenUi.Clear();
            for (int i = 0; i < _canvas.transform.childCount; i++)
            {
                var child = _canvas.transform.GetChild(i).gameObject;
                if (child == _doneBtn.gameObject || !child.activeSelf) continue;
                child.SetActive(false);
                _hiddenUi.Add(child);
            }
            _doneBtn.gameObject.SetActive(true);
        }

        public void BeginTargetPick(ReplayKeyframe kf, bool lookTarget, bool objects, List<int> allowed)
        {
            _pickKeyframe = kf;
            _pickLookTarget = lookTarget;
            _pickObjects = objects;
            _pickAllowed = allowed;
            _pickHover = -1;
            _pickHoverPlayer = 0;
            _picking = true;
            _pickWasFree = _freeLook;

            _hiddenUi.Clear();
            for (int i = 0; i < _canvas.transform.childCount; i++)
            {
                var child = _canvas.transform.GetChild(i).gameObject;
                if (!child.activeSelf) continue;
                child.SetActive(false);
                _hiddenUi.Add(child);
            }

            _pickHint = UGUIShip.CreateLabel(_canvas.transform, new Rect(0f, 12f, Screen.width, 26f),
                objects ? "click the object you want  ·  right click cancels" : "click the player you want  ·  right click cancels",
                UIScale.FS, Color.white, TextAnchor.MiddleCenter);
        }

        void UpdateTargetPick()
        {
            var mouse = (Vector2)Input.mousePosition;
            float best = 140f * 140f;
            int bestObject = -1;
            uint bestPlayer = 0;

            if (_pickObjects)
            {
                for (int i = 0; i < _pickAllowed.Count; i++)
                {
                    var tf = _rec.worldObjects[_pickAllowed[i]].live;
                    if (tf == null) continue;

                    var screen = _cam.WorldToScreenPoint(tf.position);
                    if (screen.z <= 0f) continue;

                    float d = ((Vector2)screen - mouse).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    bestObject = _pickAllowed[i];
                }
            }
            else
            {
                for (int i = 0; i < _tracks.Count; i++)
                {
                    if (_tracks[i].bean == null) continue;

                    var screen = _cam.WorldToScreenPoint(_tracks[i].bean.transform.position);
                    if (screen.z <= 0f) continue;

                    float d = ((Vector2)screen - mouse).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    bestPlayer = _tracks[i].player.playerId;
                }
            }

            if (bestObject != _pickHover || bestPlayer != _pickHoverPlayer)
            {
                _pickHover = bestObject;
                _pickHoverPlayer = bestPlayer;

                if (_pickObjects && bestObject >= 0) HighlightObject(bestObject);
                else if (!_pickObjects && bestPlayer != 0) HighlightPlayer(bestPlayer);
                else ClearPlayerHighlight();
            }
            else UpdateHighlight();

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape)) { EndTargetPick(); return; }
            if (!Input.GetMouseButtonDown(0)) return;

            if (_pickObjects && _pickHover >= 0)
            {
                if (_pickLookTarget) { _pickKeyframe.lookAt = ReplayLookAt.Object; _pickKeyframe.lookAtObject = _pickHover; }
                else
                {
                    var world = KeyframePosition(_pickKeyframe);
                    _pickKeyframe.cameraType = ReplayCameraType.FixedObject;
                    _pickKeyframe.targetObject = _pickHover;
                    _pickKeyframe.position = world - TargetPositionAt(_pickHover, 0, _pickKeyframe.time);
                }
            }
            else if (!_pickObjects && _pickHoverPlayer != 0)
            {
                if (_pickLookTarget) { _pickKeyframe.lookAt = ReplayLookAt.Player; _pickKeyframe.lookAtPlayerId = _pickHoverPlayer; }
                else
                {
                    var world = KeyframePosition(_pickKeyframe);
                    _pickKeyframe.cameraType = ReplayCameraType.Fixed;
                    _pickKeyframe.targetPlayerId = _pickHoverPlayer;
                    _pickKeyframe.position = world - PlayerPositionAt(_pickHoverPlayer, _pickKeyframe.time);
                }
            }
            EndTargetPick();
        }

        void EndTargetPick()
        {
            _picking = false;
            ClearPlayerHighlight();

            if (_pickHint != null) Destroy(_pickHint.gameObject);
            _pickHint = null;

            foreach (var go in _hiddenUi)
                if (go != null) go.SetActive(true);
            _hiddenUi.Clear();

            var kf = _pickKeyframe;
            _pickKeyframe = null;
            _pickAllowed = null;

            if (kf != null) OpenKeyframeWindow(kf);

            _freeLook = _pickWasFree;
            ApplyTime();
            SyncUi();
        }

        void EndCameraEdit()
        {
            _editingCam = false;
            _freeLook = false;
            _doneBtn.gameObject.SetActive(false);

            foreach (var go in _hiddenUi)
                if (go != null) go.SetActive(true);
            _hiddenUi.Clear();

            Plugin.Log.LogInfo($"shot set on the {Stamp(_editKeyframe.time)} keyframe");
            _editKeyframe = null;
            ApplyTime();
            SyncUi();
        }

        void OpenKeyframeWindow(ReplayKeyframe kf)
        {
            _freeLook = false;
            ReplayKeyframeWindow.Show(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H), _rec, kf,
                new Action(OnKeyframeWindowClosed));
            _windowRt.gameObject.SetActive(false);
        }

        void Minimize()
        {
            _minimized = true;
            _windowRt.gameObject.SetActive(false);
            _restoreBtn.gameObject.SetActive(true);
        }

        void Restore()
        {
            _minimized = false;
            _restoreBtn.gameObject.SetActive(false);
            _windowRt.gameObject.SetActive(true);
        }

        void OnKeyframeWindowClosed()
        {
            _windowRt.gameObject.SetActive(!_minimized);
            _rec.SortKeyframes();
            for (int i = _selected.Count - 1; i >= 0; i--)
                if (!_rec.keyframes.Contains(_selected[i])) _selected.RemoveAt(i);
            _freeLook = false;
            ApplyTime();
            SyncUi();
        }

        void CaptureIntoKeyframe(ReplayKeyframe kf)
        {
            var pos = _cam.transform.position;
            kf.rotation = _cam.transform.rotation;
            kf.position = kf.cameraType == ReplayCameraType.Fixed || kf.cameraType == ReplayCameraType.FixedObject
                ? pos - TargetPositionAt(kf.targetObject, kf.targetPlayerId, kf.time)
                : pos;
            kf.fov = _cam.fieldOfView;
        }

        static void SampleFrames(List<ReplayFrame> frames, float time, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            if (frames == null || frames.Count == 0) return;

            int i = 0;
            while (i + 1 < frames.Count && frames[i + 1].t <= time) i++;
            if (i + 1 < frames.Count)
            {
                float den = frames[i + 1].t - frames[i].t;
                float f = den > 0f ? Mathf.Clamp01((time - frames[i].t) / den) : 0f;
                pos = Vector3.Lerp(frames[i].pos, frames[i + 1].pos, f);
                rot = Quaternion.Slerp(frames[i].rot, frames[i + 1].rot, f);
                return;
            }
            pos = frames[i].pos;
            rot = frames[i].rot;
        }

        // picking a player in the keyframe window is blind otherwise — there's no telling which of 30
        // beans "Attached to" just landed on. the marker only shows while a player parameter is the
        // thing being touched.
        public void HighlightObject(int index)
        {
            _highlightObject = index;
            _highlightPlayer = 0;
            _highlightOn = true;

            if (_highlight == null) BuildHighlight();
            if (_highlight != null) _highlight.SetActive(true);
            UpdateHighlight();
        }

        public void HighlightPlayer(uint playerId)
        {
            _highlightPlayer = playerId;
            _highlightObject = -1;
            _highlightOn = true;

            if (_highlight == null) BuildHighlight();
            if (_highlight != null) _highlight.SetActive(true);
            UpdateHighlight();
        }

        public void ClearPlayerHighlight()
        {
            _highlightOn = false;
            if (_highlight != null) _highlight.SetActive(false);
        }

        void BuildHighlight()
        {
            var mat = AdditiveMaterial();
            if (mat == null)
            {
                Plugin.Log.LogWarning("nothing to draw the player marker with, skipping it");
                return;
            }

            if (_highlightTex == null)
                _highlightTex = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.replay.highlight_player.png");

            mat.mainTexture = _highlightTex;
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _highlightTex);
            if (mat.HasProperty("_Color")) mat.color = Color.white;
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", Color.white);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = 3100;

            _highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _highlight.name = "BettrFG_ReplayPlayerMarker";
            _highlight.transform.SetParent(transform, false);
            _highlight.transform.localScale = new Vector3(HIGHLIGHT_SIZE, HIGHLIGHT_SIZE, 1f);
            Destroy(_highlight.GetComponent<Collider>());

            var renderer = _highlight.GetComponent<Renderer>();
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // the built-in additive shaders are stripped out of the game's build (Shader.Find only ever
        // came back with Sprites/Default, which is why the marker read as flat alpha), so if they
        // aren't there, borrow the blend setup off a material the game itself draws additively.
        static Material AdditiveMaterial()
        {
            foreach (var name in new[] { "Particles/Additive", "Legacy Shaders/Particles/Additive", "Mobile/Particles/Additive" })
            {
                var shader = Shader.Find(name);
                if (shader == null) continue;
                Plugin.Log.LogInfo($"player marker on {name}");
                return new Material(shader);
            }

            // has to actually draw a textured surface: the engine's own hidden shaders blend additively
            // and render nothing at all, which is how the marker ended up invisible.
            Material best = null;
            int bestScore = 0;
            foreach (var candidate in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (candidate == null || candidate.shader == null) continue;

                string shaderName = candidate.shader.name;
                if (string.IsNullOrEmpty(shaderName) || shaderName.StartsWith("Hidden/")) continue;
                if (!candidate.HasProperty("_MainTex")) continue;
                if (!candidate.HasProperty("_SrcBlend") || !candidate.HasProperty("_DstBlend")) continue;
                if ((int)candidate.GetFloat("_DstBlend") != (int)UnityEngine.Rendering.BlendMode.One) continue;

                int src = (int)candidate.GetFloat("_SrcBlend");
                if (src != (int)UnityEngine.Rendering.BlendMode.SrcAlpha && src != (int)UnityEngine.Rendering.BlendMode.One) continue;

                string lower = shaderName.ToLowerInvariant();
                int score = 1;
                if (lower.Contains("particle")) score += 4;
                if (lower.Contains("additive")) score += 4;
                if (lower.Contains("unlit")) score += 2;
                if (lower.Contains("vfx") || lower.Contains("glow") || lower.Contains("sprite")) score += 2;

                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
                if (bestScore >= 8) break;
            }

            if (best != null)
            {
                Plugin.Log.LogInfo($"player marker borrowing {best.name} ({best.shader.name}) for its additive blend");
                return new Material(best);
            }

            var sprites = Shader.Find("Sprites/Default");
            if (sprites == null) return null;

            Plugin.Log.LogWarning("no additive material anywhere in the build, the player marker falls back to alpha blending");
            return new Material(sprites);
        }

        // the shot the keyframes describe is invisible from the free cam, so mark it on the replay
        // canvas: the camera icon sits where that camera projects to, and the two lines are its
        // frustum edges projected the same way, so they widen with the keyframed fov.
        void UpdateCameraGizmo()
        {
            if (!_freeLook || _exporting || _editingCam || !PreviewPose(out var pos, out var rot, out float fov))
            {
                ShowGizmo(false);
                return;
            }

            float half = (fov > 0f ? fov : _cam.fieldOfView) * 0.5f * Mathf.Deg2Rad;
            var reach = rot * Vector3.forward * Mathf.Cos(half) * GIZMO_REACH;
            var spread = rot * Vector3.right * Mathf.Sin(half) * GIZMO_REACH;

            if (!CanvasPoint(pos, out var origin))
            {
                ShowGizmo(false);
                return;
            }

            if (_gizmoRoot == null) BuildGizmo();
            ShowGizmo(true);

            _gizmoIcon.anchoredPosition = new Vector2(origin.x, -origin.y);

            if (CanvasPoint(pos - spread + reach, out var left)) StretchLine(_gizmoLineL, origin, left);
            else _gizmoLineL.gameObject.SetActive(false);

            if (CanvasPoint(pos + spread + reach, out var right)) StretchLine(_gizmoLineR, origin, right);
            else _gizmoLineR.gameObject.SetActive(false);
        }

        // canvas pixels, x right and y down from the top left, the same space SetPixelRect lays out in
        bool CanvasPoint(Vector3 world, out Vector2 point)
        {
            var screen = _cam.WorldToScreenPoint(world);
            float sf = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            point = new Vector2(screen.x / sf, (Screen.height - screen.y) / sf);
            return screen.z > 0f;
        }

        static void StretchLine(RectTransform line, Vector2 from, Vector2 to)
        {
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);

            var delta = to - from;
            line.anchoredPosition = new Vector2(from.x, -from.y);
            line.sizeDelta = new Vector2(delta.magnitude, GIZMO_LINE_W);
            line.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-delta.y, delta.x) * Mathf.Rad2Deg);
        }

        void ShowGizmo(bool show)
        {
            if (_gizmoRoot == null || _gizmoRoot.gameObject.activeSelf == show) return;
            _gizmoRoot.gameObject.SetActive(show);
        }

        void BuildGizmo()
        {
            var rootGo = new GameObject("CameraGizmo");
            _gizmoRoot = rootGo.AddComponent<RectTransform>();
            _gizmoRoot.SetParent(_canvas.transform, false);
            _gizmoRoot.anchorMin = _gizmoRoot.anchorMax = new Vector2(0f, 1f);
            _gizmoRoot.pivot = new Vector2(0f, 1f);
            _gizmoRoot.anchoredPosition = Vector2.zero;
            _gizmoRoot.sizeDelta = Vector2.zero;
            _gizmoRoot.SetAsFirstSibling();

            _gizmoLineL = BuildGizmoLine();
            _gizmoLineR = BuildGizmoLine();

            if (_gizmoSprite == null)
                _gizmoSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.replay.camera_gizmo.png");

            var iconGo = new GameObject("Icon");
            _gizmoIcon = iconGo.AddComponent<RectTransform>();
            _gizmoIcon.SetParent(_gizmoRoot, false);
            _gizmoIcon.anchorMin = _gizmoIcon.anchorMax = new Vector2(0f, 1f);
            _gizmoIcon.pivot = new Vector2(0.5f, 0.5f);
            _gizmoIcon.sizeDelta = new Vector2(GIZMO_ICON, GIZMO_ICON);

            var img = iconGo.AddComponent<Image>();
            img.sprite = _gizmoSprite;
            img.color = GIZMO_TINT;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

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

        void UpdateHighlight()
        {
            if (!_highlightOn || _highlight == null) return;

            var pos = TargetPositionAt(_highlightObject, _highlightPlayer, _time) + Vector3.up * HIGHLIGHT_HEIGHT;
            _highlight.transform.position = pos;
            _highlight.transform.rotation = Quaternion.LookRotation(pos - _cam.transform.position, Vector3.up);
        }

        public Vector3 TargetPositionAt(int objectIndex, uint playerId, float time)
        {
            if (objectIndex >= 0 && objectIndex < _rec.worldObjects.Count)
            {
                var tf = _rec.worldObjects[objectIndex].live;
                if (tf != null) return tf.position;
            }
            return PlayerPositionAt(playerId, time);
        }

        public Vector3 PlayerPositionAt(uint playerId, float time)
        {
            for (int i = 0; i < _tracks.Count; i++)
            {
                if (_tracks[i].player.playerId != playerId) continue;
                if (_tracks[i].bean != null) return _tracks[i].bean.transform.position;
                SampleFrames(_tracks[i].player.frames, time, out var p, out _);
                return p;
            }
            return Vector3.zero;
        }

        public Vector3 KeyframePosition(ReplayKeyframe k)
        {
            if (k.cameraType == ReplayCameraType.Gameplay)
            {
                SampleFrames(_rec.cameraFrames, _time, out var camPos, out _);
                return camPos;
            }
            if (k.cameraType == ReplayCameraType.Fixed || k.cameraType == ReplayCameraType.FixedObject)
                return TargetPositionAt(k.targetObject, k.targetPlayerId, _time) + k.position;
            return k.position;
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

        bool KeyframeSpan(out ReplayKeyframe a, out ReplayKeyframe b, out float raw)
        {
            a = null;
            b = null;
            raw = 0f;

            var keys = _rec.keyframes;
            if (keys.Count == 0) return false;

            int i = 0;
            while (i + 1 < keys.Count && keys[i + 1].time <= _time) i++;

            a = keys[i];
            b = i + 1 < keys.Count ? keys[i + 1] : null;
            if (b == null || b.cut) return true;

            float den = b.time - a.time;
            raw = den > 0f ? Mathf.Clamp01((_time - a.time) / den) : 0f;
            return true;
        }

        float PlaybackSpeed()
        {
            if (!KeyframeSpan(out var a, out var b, out float raw)) return 1f;
            return b == null ? a.speed : Mathf.Lerp(a.speed, b.speed, raw);
        }

        // the shot the keyframes describe at the current time, whether or not the view camera is the
        // thing showing it — the free-cam gizmo draws the same pose.
        bool PreviewPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            fov = 0f;

            if (!KeyframeSpan(out var a, out var b, out float raw)) return false;
            float f = ReplayEase.Apply(a.easingCurve, a.easingDirection, raw);

            pos = KeyframePosition(a);
            if (b != null) pos = Vector3.Lerp(pos, KeyframePosition(b), f);

            rot = KeyframeRotation(a, pos);
            if (b != null) rot = Quaternion.Slerp(rot, KeyframeRotation(b, pos), f);

            if (a.fov > 0f) fov = b != null && b.fov > 0f ? Mathf.Lerp(a.fov, b.fov, f) : a.fov;
            return true;
        }

        void EvaluateKeyframeCamera()
        {
            if (!PreviewPose(out var pos, out var rot, out float fov)) return;

            _cam.transform.position = pos;
            _cam.transform.rotation = rot;
            if (fov > 0f) _cam.fieldOfView = fov;
        }

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

        static bool OverKeyRow(Vector2 cursor) => cursor.y >= LANES_Y - 1f && cursor.y <= TIMELINE_H;

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

        bool HandleTimelineMouse()
        {
            if (_dragKeyframe == null && _contextMenu != null
                && RectTransformUtility.RectangleContainsScreenPoint(_contextMenu, Input.mousePosition, null))
                return true;

            var cursor = TimelineCursor();
            float time = TimeAt(cursor.x);

            if (_dragKeyframe != null)
            {
                if (Input.GetMouseButton(0))
                {
                    if (!_dragMoved && Mathf.Abs(Input.mousePosition.x - _dragGrabMouseX) < DRAG_DEADZONE) return true;
                    _dragMoved = true;
                    _clickedKeyframe = null;
                    MoveSelection(time - _dragGrabTime);
                }
                else
                {
                    if (_dragMoved)
                        Plugin.Log.LogInfo(_selected.Count > 1
                            ? $"shifted {_selected.Count} keyframes, the one you grabbed sits at {Stamp(_dragKeyframe.time)} now"
                            : $"moved a keyframe to {Stamp(_dragKeyframe.time)}");
                    _dragKeyframe = null;
                    _dragMoved = false;
                }
                return true;
            }

            if (_draggingIn || _draggingOut)
            {
                if (Input.GetMouseButton(0)) DragTrim(time);
                else
                {
                    Plugin.Log.LogInfo($"composition is {Stamp(_rec.trimStart)} to {Stamp(_rec.trimEnd)} now");
                    _draggingIn = _draggingOut = false;
                }
                return true;
            }

            if (_scrubbing)
            {
                if (Input.GetMouseButton(0)) SeekTo(time);
                else _scrubbing = false;
                return true;
            }

            if (_marqueeing)
            {
                if (Input.GetMouseButton(0)) DragMarquee(cursor);
                else EndMarquee();
                return true;
            }

            if (!OverTimeline(cursor)) return false;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f && !ShiftHeld())
            {
                if (CtrlHeld()) ZoomTimeline(scroll, time);
                else PanTimeline(scroll);
                SyncUi();
                return true;
            }

            if (OverEdgeBand(cursor))
            {
                if (!Input.GetMouseButtonDown(0)) return true;

                CloseContextMenu();
                Deselect();

                float inGap = Mathf.Abs(cursor.x - TrackX(_rec.trimStart));
                float outGap = Mathf.Abs(cursor.x - TrackX(_rec.trimEnd));
                if (inGap <= GRAB_PX && inGap <= outGap) _draggingIn = true;
                else if (outGap <= GRAB_PX) _draggingOut = true;
                return true;
            }

            if (OverScrub(cursor))
            {
                if (Input.GetMouseButtonDown(0))
                {
                    CloseContextMenu();
                    Deselect();
                    _scrubbing = true;
                    SeekTo(time);
                    return true;
                }
                if (Input.GetMouseButtonDown(1))
                {
                    CloseContextMenu();
                    Deselect();
                    SeekTo(time);
                    ShowContextMenu();
                    return true;
                }
                return false;
            }

            if (!OverKeyRow(cursor)) return false;

            if (Input.GetMouseButtonDown(0))
            {
                CloseContextMenu();
                var k = KeyframeNear(time);
                if (k == null)
                {
                    BeginMarquee(cursor);
                    return true;
                }

                if (ShiftHeld())
                {
                    if (_selected.Contains(k)) { _selected.Remove(k); return true; }
                    _selected.Add(k);
                }
                else
                {
                    if (k == _clickedKeyframe && Time.realtimeSinceStartup - _clickedAt <= DOUBLE_CLICK)
                    {
                        _clickedKeyframe = null;
                        SelectOnly(k);
                        SeekTo(k.time);
                        OpenKeyframeWindow(k);
                        BeginCameraEdit(k);
                        return true;
                    }

                    _clickedKeyframe = k;
                    _clickedAt = Time.realtimeSinceStartup;

                    if (_selected.Count <= 1)
                    {
                        SelectOnly(k);
                        SeekTo(k.time);
                        OpenKeyframeWindow(k);
                    }
                }

                _dragKeyframe = k;
                _dragGrabTime = time;
                _dragGrabMouseX = Input.mousePosition.x;
                _dragMoved = false;
                _dragStartTimes.Clear();
                foreach (var sel in _selected) _dragStartTimes.Add(sel.time);
                return true;
            }

            if (Input.GetMouseButtonDown(1))
            {
                CloseContextMenu();
                var k = KeyframeNear(time);
                if (k != null)
                {
                    if (!_selected.Contains(k)) SelectOnly(k);
                    ShowKeyframeMenu(k);
                }
                else
                {
                    SeekTo(time);
                    ShowContextMenu();
                }
                return true;
            }

            return false;
        }

        void Deselect()
        {
            _selected.Clear();
            ReplayKeyframeWindow.Instance?.Close();
        }

        void BeginMarquee(Vector2 cursor)
        {
            _marqueeing = true;
            _marqueeAnchor = cursor;
            _marqueeBase.Clear();
            if (ShiftHeld()) _marqueeBase.AddRange(_selected);
            else Deselect();
            _selected.Clear();
            _selected.AddRange(_marqueeBase);

            _marqueeRt = UGUIShip.CreatePanel(_timelineRt, new Rect(cursor.x, cursor.y, 0f, 0f),
                new Color(1f, 0.83f, 0.25f, 0.16f), "Marquee");
            _marqueeRt.GetComponent<Image>().raycastTarget = false;
        }

        void DragMarquee(Vector2 cursor)
        {
            float left = Mathf.Min(_marqueeAnchor.x, cursor.x);
            float right = Mathf.Max(_marqueeAnchor.x, cursor.x);
            float top = Mathf.Min(_marqueeAnchor.y, cursor.y);
            float bottom = Mathf.Max(_marqueeAnchor.y, cursor.y);
            UGUIShip.SetPixelRect(_marqueeRt, new Rect(left, top, right - left, bottom - top));

            float from = TimeAt(left);
            float to = TimeAt(right);

            _selected.Clear();
            _selected.AddRange(_marqueeBase);
            foreach (var k in _rec.keyframes)
                if (k.time >= from && k.time <= to && !_selected.Contains(k)) _selected.Add(k);
        }

        void EndMarquee()
        {
            _marqueeing = false;
            if (_marqueeRt != null) Destroy(_marqueeRt.gameObject);
            _marqueeRt = null;
            if (_selected.Count > 0) Plugin.Log.LogInfo($"{_selected.Count} keyframes in the box");
        }

        static bool CtrlHeld() => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        void DragTrim(float time)
        {
            if (_draggingIn) _rec.trimStart = Mathf.Clamp(time, 0f, _rec.trimEnd - MIN_TRIM);
            else _rec.trimEnd = Mathf.Clamp(time, _rec.trimStart + MIN_TRIM, _rec.duration);
            _time = Mathf.Clamp(_time, _rec.trimStart, _rec.trimEnd);
        }

        static bool ShiftHeld() => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        void SelectOnly(ReplayKeyframe k)
        {
            _selected.Clear();
            _selected.Add(k);
        }

        void MoveSelection(float delta)
        {
            float earliest = float.MaxValue;
            float latest = float.MinValue;
            foreach (float start in _dragStartTimes)
            {
                if (start < earliest) earliest = start;
                if (start > latest) latest = start;
            }
            delta = Mathf.Clamp(delta, -earliest, _rec.duration - latest);

            for (int i = 0; i < _selected.Count && i < _dragStartTimes.Count; i++)
                _selected[i].time = _dragStartTimes[i] + delta;
            _rec.SortKeyframes();

            if (_selected.Count > 1) return;
            _time = _dragKeyframe.time;
            _freeLook = false;
        }

        void HandleFov()
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f) return;
            if (!ShiftHeld()) return;

            _cam.fieldOfView = Mathf.Clamp(_cam.fieldOfView - scroll * 2.5f, FOV_MIN, FOV_MAX);
            _freeLook = true;
        }

        static bool TypingInField()
        {
            var sel = EventSystem.current?.currentSelectedGameObject;
            return sel != null && sel.GetComponent<InputField>() != null;
        }

        static bool PointerOverUi() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        void SaveAs()
        {
            System.IO.Directory.CreateDirectory(SaveReplay.ReplayDir);
            string suggested = System.IO.Path.Combine(SaveReplay.ReplayDir, SafeName());
            WinDialogs.SaveFile("Save replay", "bfgreplay", suggested, new Action<string>(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                SaveReplay.WriteTo(_rec, path);
                _rec.sourcePath = path;
                ReplayThumbnail.Capture(_cam, path);
                Plugin.Log.LogInfo($"replay written out to {path}");
            }));
        }

        string ReplayName()
        {
            if (!string.IsNullOrEmpty(_rec.name)) return _rec.name;
            if (!string.IsNullOrEmpty(_rec.roundName)) return _rec.roundName;
            return string.IsNullOrEmpty(_rec.roundId) ? "replay" : _rec.roundId;
        }

        void OnNameChanged(string value)
        {
            _rec.name = (value ?? "").Trim();
            SyncUi();
        }

        string SafeName() => string.Concat(ReplayName().Split(System.IO.Path.GetInvalidFileNameChars()));

        void OpenExportWindow()
        {
            ReplayKeyframeWindow.Instance?.Close();
            ReplayExportWindow.Show(_canvas.transform, new Rect(MARGIN, MARGIN, WIN_W, WIN_H),
                new Action(StartExport), new Action(OnExportWindowClosed));
            _windowRt.gameObject.SetActive(false);
        }

        void OnExportWindowClosed() => _windowRt.gameObject.SetActive(!_minimized);

        void StartExport()
        {
            if (_exporting) return;
            StartCoroutine(ExportRoutine().WrapToIl2Cpp());
        }

        // the video pass is stepped, so nothing can be recorded off it. this plays the replay through
        // once at real speed with the system output looped back into a wav — same speed curve as the
        // video, so the two line up end to end.
        IEnumerator AudioPass(string dir)
        {
            var capture = new ReplayProcessAudioCapture();
            string path = System.IO.Path.Combine(dir, "audio.wav");
            if (!capture.Start(path)) yield break;

            ReplayExportWindow.Instance?.SetStatus("audio pass, playing it through...");
            float settle = Time.realtimeSinceStartup + 0.35f;
            while (Time.realtimeSinceStartup < settle) yield return null;

            _exportAudioLead = capture.Seconds;
            _time = _rec.trimStart;
            _paused = false;
            _freeLook = false;
            _audio.Seek(_time);

            while (_time < _rec.trimEnd)
            {
                float from = _time;
                float speed = PlaybackSpeed();
                _time = Mathf.Min(_time + Time.unscaledDeltaTime * speed, _rec.trimEnd);
                _audio.Tick();
                _audio.SetSpeed(speed);
                _audio.Advance(from, _time);
                ApplyTime();
                _world.Apply(_time);
                SyncUi();
                yield return null;
            }

            _paused = true;
            capture.Stop();

            float deadline = Time.realtimeSinceStartup + 3f;
            while (!capture.Finished && Time.realtimeSinceStartup < deadline) yield return null;

            _exportWav = path;
            Plugin.Log.LogInfo($"audio pass done, {capture.Seconds:0.0}s captured with {_exportAudioLead:0.00}s of lead-in to trim");
        }

        IEnumerator ExportRoutine()
        {
            _exporting = true;
            _paused = true;
            _freeLook = false;
            CloseContextMenu();
            ClearPlayerHighlight();

            int w = ReplayExport.Width;
            int h = ReplayExport.Height;
            int fps = ReplayExport.FrameRate;
            string dir = ReplayExport.NewFolder(ReplayName());
            float from = _rec.trimStart;
            float until = _rec.trimEnd;
            int cap = Mathf.CeilToInt(Mathf.Max(1f, until - from) * fps / MIN_EXPORT_SPEED);

            _exportWav = null;
            _exportAudioLead = 0f;
            if (ReplayExport.AudioOn) yield return StartCoroutine(AudioPass(dir).WrapToIl2Cpp());

            string outPath = dir + ".mp4";
            var encoder = new ReplayEncoder(outPath, w, h, fps, ReplayExport.Kbps, _exportWav, _exportAudioLead);

            var rt = new RenderTexture(w, h, 24);
            var shot = new Texture2D(w, h, TextureFormat.BGRA32, false);
            var readRect = new Rect(0f, 0f, w, h);
            var endOfFrame = new WaitForEndOfFrame();
            Plugin.Log.LogInfo($"export: {w}x{h} @ {fps}fps, {ReplayExport.Kbps}kbps, {Stamp(from)}-{Stamp(until)} of replay -> {dir}");

            int frame = 0;
            _time = from;
            while (_time < until && frame < cap)
            {
                ApplyTime();
                _world.Apply(_time);
                SyncUi();

                // costumes, dangly bits and everything else that chases the skeleton do it in
                // LateUpdate, so the shot has to be taken after that or they trail a frame behind.
                yield return endOfFrame;

                _cam.targetTexture = rt;
                _cam.Render();
                _cam.targetTexture = null;

                var was = RenderTexture.active;
                RenderTexture.active = rt;
                shot.ReadPixels(readRect, 0, 0, false);
                shot.Apply(false);
                RenderTexture.active = was;

                encoder.WriteFrame(shot.GetRawTextureData());

                frame++;
                // one output frame is 1/fps of FINISHED footage, so the replay only advances by the
                // keyframed game speed — that's what turns a 0.25x stretch into real slow motion.
                _time += Mathf.Max(MIN_EXPORT_SPEED, PlaybackSpeed()) / fps;
                ReplayExportWindow.Instance?.SetStatus(
                    $"frame {frame} · {Mathf.RoundToInt(100f * (_time - from) / Mathf.Max(0.01f, until - from))}%");
            }

            Destroy(shot);
            rt.Release();
            Destroy(rt);

            ReplayExportWindow.Instance?.SetStatus("finishing up...");
            encoder.Dispose();
            ReplayExport.CleanFrames(dir);

            ReplayExportWindow.Instance?.SetStatus("done: " + System.IO.Path.GetFileName(outPath));
            Plugin.Log.LogInfo($"export done, {frame} frames into {outPath}");
            ReplayExport.Reveal(outPath);

            _exporting = false;
        }

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
            Instance = null;
            StopAllCoroutines();
            StartCoroutine(ExitRoutine().WrapToIl2Cpp());
        }

        IEnumerator ExitRoutine()
        {
            ReplayKeyframeWindow.Instance?.Close();
            _audio.Release();
            _world?.Release();
            _speech?.Release();
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

            ReplayBankFreeze.Held = false;

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
