using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Utilities;
using FG.Common;
using FGClient;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace BetterFG.Features.CustomizeFallGuys
{
    public static class FeatureCustomizeFallGuys
    {
        public static readonly BfgFeature feature = new BfgFeature(
            "customizefallguys",
            "Customize Fall Guys",
            false,
            new List<FeatureSetting>
            {
                new FeatureSetting { id = "blink", label = "Blinking", defaultOn = true },
                new FeatureSetting { id = "look", label = "Look left and right", defaultOn = true },
                new FeatureSetting { id = "squint", label = "Light squinting", defaultOn = false },
                new FeatureSetting { id = "localonly", label = "Only my Fall Guy", defaultOn = false },
            },
            onOpen: OnToggled,
            onClosed: OnToggled,
            onSettingChanged: OnSettingChanged,
            ranges: new List<FeatureRange>
            {
                new FeatureRange { id = "eyedist", label = "Eye distance", min = -0.1f, max = 0.12f, step = 0.01f, defaultValue = 0f, hint = "Pushes the eyes apart, or together. At -0.1 they nearly meet in the middle of the face." },
                new FeatureRange { id = "eyeheight", label = "Eye height", min = -0.1f, max = 0.1f, step = 0.01f, defaultValue = 0f, hint = "Slides both eyes up or down the face." },
                new FeatureRange { id = "eyerot", label = "Eye rotation", min = -45f, max = 45f, step = 1f, defaultValue = 0f, hint = "Tilts the two eyes in opposite directions, in degrees. 0 is stock." },
                new FeatureRange { id = "eyescale", label = "Eye scale", min = 0.1f, max = 4f, step = 0.05f, defaultValue = 1f, hint = "Multiplies eye size. 1 is stock." },
                new FeatureRange { id = "eyeyscale", label = "Eye Y scale", min = 0.1f, max = 4f, step = 0.05f, defaultValue = 1f, hint = "Height only, on top of eye scale. Low squashes the eyes into slits, high stretches them tall." },
            },
            onRangeChanged: OnRangeChanged,
            choices: new List<FeatureChoice>
            {
                new FeatureChoice
                {
                    id = "eyemat",
                    label = "Eye material",
                    optionIds = new List<string> { "none", "crownjam" },
                    optionLabels = new List<string> { "None", "Crown Jam" },
                    defaultId = "none",
                    hint = "Draws a custom eye pass over the bean. Its tint follows that bean's own primary colour.",
                },
            },
            onChoiceChanged: OnChoiceChanged);

        const float BakeFps = 30f;
        const int MaxBakeFrames = 512;

        const float LookOffset = 0.03f;

        internal static bool On;

        static bool _blink, _look, _squint, _localOnly, _work;
        static float _dist, _height, _rot, _scale, _yscale;
        static string _eyeMat;
        static GameObject _previewPanel, _previewBean;

        static void ReadSettings()
        {
            _blink = feature.Get("blink");
            _look = feature.Get("look");
            _squint = feature.Get("squint");
            _localOnly = feature.Get("localonly");
            _dist = feature.GetRange("eyedist");
            _height = feature.GetRange("eyeheight");
            _rot = feature.GetRange("eyerot");
            _scale = feature.GetRange("eyescale");
            _yscale = feature.GetRange("eyeyscale");
            On = feature.enabled;
            _eyeMat = On ? feature.GetChoice("eyemat") : "none";
            _work = On && (_blink || _look || _squint
                || _eyeMat != "none"
                || !Mathf.Approximately(_dist, 0f)
                || !Mathf.Approximately(_height, 0f)
                || !Mathf.Approximately(_rot, 0f)
                || !Mathf.Approximately(_scale, 1f)
                || !Mathf.Approximately(_yscale, 1f));
        }

        struct Eyes
        {
            public Transform l;
            public Transform r;
            public float blinkCursor;
            public float blinkSpeed;
            public float lookFrom;
            public float lookTo;
            public float lookT;
            public float lookRate;
            public float lookHold;
            public float squintPhase;
            public GameObject eyeGo;
            public Vector3 lastScaleL;
            public Vector3 lastScaleR;
        }

        static Eyes[] _eyes = new Eyes[64];
        static int _count;
        static readonly List<GameObject> _pending = new List<GameObject>();

        static bool _restCaptured;
        static Vector3 _restPosL, _restPosR, _restScaleL, _restScaleR;
        static Quaternion _restRotL, _restRotR;

        static int _bakedFrames;
        static float _playbackRate;
        static Vector3[] _lPos, _lScale, _rPos, _rScale;
        static Quaternion[] _lRot, _rRot;

        static void OnToggled() => Refresh(true);

        static void OnSettingChanged(string id, bool on) => Refresh(true);

        static void OnRangeChanged(string id, float value) => Refresh(true);

        static void OnChoiceChanged(string id, string optionId) => Refresh(true);

        public static Texture PreviewTexture => EyePreview.Ensure();

        public static void SetPreviewPanel(GameObject panel)
        {
            _previewPanel = panel;
            Refresh();
        }

        public static void Refresh(bool rebuild = false, float delay = 0f)
        {
            if (delay > 0f)
            {
                FallGuyEyeDriver.Instance.StartCoroutine(RefreshLater(rebuild, delay).WrapToIl2Cpp());
                return;
            }

            if (rebuild) RestoreAll();
            ReadSettings();

            var local = BeanMonitorService.LocalPlayerBean;
            var mm = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
            _previewBean = local != null && local.activeInHierarchy ? local
                : mm == null ? null
                : mm._menuFallGuy != null ? mm._menuFallGuy : mm._lobbyFallGuy;

            EyePreview.Invalidate();
            if (!_work) return;

            if (_localOnly)
            {
                Apply(local);
                if (mm != null)
                {
                    Apply(mm._menuFallGuy);
                    Apply(mm._lobbyFallGuy);
                }
                return;
            }

            var all = Resources.FindObjectsOfTypeAll(Il2CppType.Of<FallguyCustomisationHandler>());
            for (int i = 0; all != null && i < all.Length; i++)
            {
                var handler = all[i].TryCast<FallguyCustomisationHandler>();
                if (handler == null) continue;
                var go = handler.gameObject;
                if (go.scene.IsValid()) Apply(go);
            }
        }

        public static void Apply(GameObject bean)
        {
            if (bean == null || !_work) return;
            for (int i = 0; i < _pending.Count; i++)
                if (_pending[i] == bean) return;
            _pending.Add(bean);
        }

        static IEnumerator RefreshLater(bool rebuild, float delay)
        {
            yield return new WaitForSeconds(delay);
            Refresh(rebuild);
        }

        static void RestoreAll()
        {
            for (int i = 0; i < _count; i++)
            {
                var l = _eyes[i].l;
                var r = _eyes[i].r;
                if (_restCaptured && l.m_CachedPtr != IntPtr.Zero && r.m_CachedPtr != IntPtr.Zero)
                {
                    l.SetLocalPositionAndRotation(_restPosL, _restRotL);
                    l.localScale = _restScaleL;
                    r.SetLocalPositionAndRotation(_restPosR, _restRotR);
                    r.localScale = _restScaleR;
                }
                EyeGeometry.Detach(_eyes[i].eyeGo);
                _eyes[i].eyeGo = null;
                _eyes[i].l = null;
                _eyes[i].r = null;
            }
            _count = 0;
            _pending.Clear();
        }

        static GameObject AttachEyes(GameObject bean)
        {
            if (_eyeMat == "none") return null;

            var shared = AssetManager.GetMaterial("bettrfg_mat_eyes_" + _eyeMat);
            if (shared == null)
            {
                Plugin.Log.LogWarning($"eye material '{_eyeMat}' isn't in any loaded bundle, leaving that bean's face alone");
                return null;
            }

            var tint = Color.white;
            var cpm = FallGuysLib.Players.PlayerUtils.GetClientPlayerManager();
            var byId = cpm?._playerIdIndex;
            if (byId != null)
            {
                foreach (var kv in byId)
                {
                    var npdc = kv.Value;
                    if (npdc == null || npdc.fgcc == null || npdc.fgcc.gameObject != bean) continue;
                    var opt = cpm.GetPlayerCustomisationSelection(kv.Key)?.ColourOption;
                    if (opt != null) tint = opt.primaryColour;
                    return EyeGeometry.Attach(bean, shared, tint);
                }
            }

            var menuOpt = UnityEngine.Object.FindObjectOfType<MainMenuManager>()?._playerProfile?.CustomisationSelections?.ColourOption;
            if (menuOpt != null) tint = menuOpt.primaryColour;
            return EyeGeometry.Attach(bean, shared, tint);
        }

        static void Track(GameObject root)
        {
            if (root == null) return;

            var eyeL = GameObjectHelper.FindBoneOnBean(root, "Eye_L_jnt");
            var eyeR = GameObjectHelper.FindBoneOnBean(root, "Eye_R_jnt");
            if (eyeL == null || eyeR == null) return;

            for (int i = 0; i < _count; i++)
            {
                if (_eyes[i].l.m_CachedPtr != eyeL.m_CachedPtr) continue;
                if (_eyeMat == "none" || _eyes[i].eyeGo != null) return;
                _eyes[i].eyeGo = AttachEyes(root);
                return;
            }

            if (!_restCaptured)
            {
                eyeL.GetLocalPositionAndRotation(out _restPosL, out _restRotL);
                eyeR.GetLocalPositionAndRotation(out _restPosR, out _restRotR);
                _restScaleL = eyeL.localScale;
                _restScaleR = eyeR.localScale;
                _restCaptured = true;
                Plugin.Log.LogInfo($"eye rest pose off the rig: L {_restPosL} / R {_restPosR}, everything gets composed from those");
            }

            if (_bakedFrames == 0 && AssetManager.Instance != null
                && AssetManager.Instance.animClips.TryGetValue("bettrfg_anim_eyes", out var clip) && clip != null)
            {
                var skel = eyeL;
                while (skel != null && skel.name != "SKELETON") skel = skel.parent;
                if (skel != null && skel.parent != null) Bake(clip, skel.parent.gameObject, eyeL, eyeR);
            }

            if (_count == _eyes.Length) Array.Resize(ref _eyes, _count * 2);
            _eyes[_count].eyeGo = AttachEyes(root);
            _eyes[_count].l = eyeL;
            _eyes[_count].r = eyeR;
            _eyes[_count].blinkSpeed = UnityEngine.Random.Range(0.8f, 1.25f) * _playbackRate;
            _eyes[_count].blinkCursor = UnityEngine.Random.Range(0f, Mathf.Max(1, _bakedFrames));
            _eyes[_count].lookFrom = 0f;
            _eyes[_count].lookTo = 0f;
            _eyes[_count].lookT = 1f;
            _eyes[_count].lookRate = 6f;
            _eyes[_count].lookHold = UnityEngine.Random.Range(0.2f, 2f);
            _eyes[_count].squintPhase = UnityEngine.Random.Range(0f, 6.28f);
            _eyes[_count].lastScaleL = _restScaleL;
            _eyes[_count].lastScaleR = _restScaleR;
            _count++;
        }

        static void Bake(AnimationClip clip, GameObject host, Transform eyeL, Transform eyeR)
        {
            float len = clip.length;
            if (len <= 0f) return;

            int frames = Mathf.Clamp(Mathf.CeilToInt(len * BakeFps), 2, MaxBakeFrames);
            _lPos = new Vector3[frames]; _lRot = new Quaternion[frames]; _lScale = new Vector3[frames];
            _rPos = new Vector3[frames]; _rRot = new Quaternion[frames]; _rScale = new Vector3[frames];

            var t = host.transform;
            t.GetLocalPositionAndRotation(out var hostPos, out var hostRot);
            var hostScale = t.localScale;

            for (int i = 0; i < frames; i++)
            {
                clip.SampleAnimation(host, len * i / frames);
                eyeL.GetLocalPositionAndRotation(out _lPos[i], out _lRot[i]);
                eyeR.GetLocalPositionAndRotation(out _rPos[i], out _rRot[i]);
                _lScale[i] = eyeL.localScale;
                _rScale[i] = eyeR.localScale;
            }

            t.SetLocalPositionAndRotation(hostPos, hostRot);
            t.localScale = hostScale;

            _bakedFrames = frames;
            _playbackRate = frames / len;
            Plugin.Log.LogInfo($"blink clip baked once, {frames} frames off {len:0.00}s, no more per-bean SampleAnimation");
        }

        public static void Tick()
        {
            if (_pending.Count > 0)
            {
                int last = _pending.Count - 1;
                var next = _pending[last];
                _pending.RemoveAt(last);
                if (next != null && _work)
                {
                    Track(next);
                    EyePreview.Invalidate();
                }
            }

            ApplyEyes();
        }

        public static void TickPreview()
        {
            if (!feature.enabled) return;
            var ui = BetterFG.UI.BetterFGUIMan.Instance;
            if (ui == null || !ui.IsVisible) return;
            if (_previewPanel == null || !_previewPanel.activeInHierarchy || _previewBean == null) return;
            EyePreview.SetBean(_previewBean);
            EyePreview.Render();
        }

        static void ApplyEyes()
        {
            if (_count == 0 || !_work) return;

            bool blink = _blink && _bakedFrames > 0;
            float dt = Time.deltaTime;
            bool hasRot = !Mathf.Approximately(_rot, 0f);
            var rotL = hasRot ? Quaternion.Euler(0f, 0f, _rot) : Quaternion.identity;
            var rotR = hasRot ? Quaternion.Euler(0f, 0f, -_rot) : Quaternion.identity;

            for (int i = 0; i < _count; i++)
            {
                var l = _eyes[i].l;
                var r = _eyes[i].r;
                if (l.m_CachedPtr == IntPtr.Zero || r.m_CachedPtr == IntPtr.Zero)
                {
                    EyeGeometry.Detach(_eyes[i].eyeGo);
                    _eyes[i].eyeGo = null;
                    EyePreview.Invalidate();
                    _count--;
                    _eyes[i] = _eyes[_count];
                    _eyes[_count].l = null;
                    _eyes[_count].r = null;
                    i--;
                    continue;
                }

                Vector3 pl = _restPosL, pr = _restPosR, sl = _restScaleL, sr = _restScaleR;
                Quaternion ql = _restRotL, qr = _restRotR;

                if (blink)
                {
                    float c = _eyes[i].blinkCursor + dt * _eyes[i].blinkSpeed;
                    _eyes[i].blinkCursor = c;

                    int idx = (int)Mathf.PingPong(c, _bakedFrames - 1);
                    pl = _lPos[idx]; ql = _lRot[idx]; sl = _lScale[idx];
                    pr = _rPos[idx]; qr = _rRot[idx]; sr = _rScale[idx];
                }

                float glance = _look ? StepLook(ref _eyes[i], dt) * LookOffset : 0f;
                float squint = _squint
                    ? 1f - 0.2f * (0.55f + 0.45f * Mathf.Sin(Time.time * 0.7f + _eyes[i].squintPhase))
                    : 1f;

                pl.x += glance - _dist;
                pr.x += glance + _dist;
                pl.y += _height;
                pr.y += _height;

                float yl = sl.y * _scale * _yscale * squint;
                float yr = sr.y * _scale * _yscale * squint;

                l.SetLocalPositionAndRotation(pl, hasRot ? ql * rotL : ql);
                r.SetLocalPositionAndRotation(pr, hasRot ? qr * rotR : qr);

                float ease = 1f - Mathf.Exp(-dt * 25f);
                var targetL = new Vector3(sl.x * _scale, yl, sl.z * _scale);
                var scaleL = Vector3.Lerp(_eyes[i].lastScaleL, targetL, ease);
                if (scaleL != _eyes[i].lastScaleL) { l.localScale = scaleL; _eyes[i].lastScaleL = scaleL; }

                var targetR = new Vector3(sr.x * _scale, yr, sr.z * _scale);
                var scaleR = Vector3.Lerp(_eyes[i].lastScaleR, targetR, ease);
                if (scaleR != _eyes[i].lastScaleR) { r.localScale = scaleR; _eyes[i].lastScaleR = scaleR; }
            }
        }

        static float StepLook(ref Eyes e, float dt)
        {
            if (e.lookT < 1f)
            {
                e.lookT = Mathf.Min(1f, e.lookT + dt * e.lookRate);
                if (e.lookT >= 1f) e.lookHold = UnityEngine.Random.Range(0.5f, 2.8f);
                return Mathf.Lerp(e.lookFrom, e.lookTo, Mathf.SmoothStep(0f, 1f, e.lookT));
            }

            float held = e.lookTo;
            e.lookHold -= dt;
            if (e.lookHold <= 0f)
            {
                e.lookFrom = held;
                e.lookTo = UnityEngine.Random.Range(-1f, 1f);
                e.lookT = 0f;
                e.lookRate = 1f / UnityEngine.Random.Range(0.12f, 0.24f);
            }
            return held;
        }
    }

    public class FallGuyEyeDriver : MonoBehaviour
    {
        public FallGuyEyeDriver(IntPtr ptr) : base(ptr) { }

        public static FallGuyEyeDriver Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            StartCoroutine(PreviewLoop().WrapToIl2Cpp());
        }

        void LateUpdate()
        {
            if (!FeatureCustomizeFallGuys.On) return;
            FeatureCustomizeFallGuys.Tick();
        }

        static IEnumerator PreviewLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;
                FeatureCustomizeFallGuys.TickPreview();
            }
        }
    }
}
