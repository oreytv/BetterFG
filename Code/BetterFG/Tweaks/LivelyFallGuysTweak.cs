using System;
using BetterFG.Core;
using FGClient;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class LivelyFallGuysTweak : BfgTweak
    {
        public LivelyFallGuysTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "lively_fall_guys";
        public override string TweakLabel => "Lively Fall Guys (blinking)";
        public override bool DefaultEnabled => false;

        public static LivelyFallGuysTweak Instance { get; private set; }
        void Awake() => Instance = this;

        public override void EnableTweak() => ReapplyAll();
        public override void DisableTweak() => ReapplyAll();

        public override void OnRoundStart() => ReapplyAll();
        public override void OnLevelEditorPlaytest() => ReapplyAll();
        public override void OnMainMenuEntered() => ReapplyAll();

        private const float BakeFps = 30f;
        private const int MaxBakeFrames = 512;

        private static int _bakedFrames;
        private static float _playbackRate;
        private static Vector3[] _lPos, _lScale, _rPos, _rScale;
        private static Quaternion[] _lRot, _rRot;

        private struct Blinker
        {
            public Transform eyeL;
            public Transform eyeR;
            public float cursor;
            public float speed;
        }

        private static Blinker[] _blinkers = new Blinker[64];
        private static int _count;

        public static void ReapplyAll()
        {
            for (int i = 0; i < _count; i++) { _blinkers[i].eyeL = null; _blinkers[i].eyeR = null; }
            _count = 0;

            if (Instance == null || !Instance.IsEnabled) return;
            if (AssetManager.Instance == null) return;
            if (!AssetManager.Instance.animClips.TryGetValue("bettrfg_anim_eyes", out var clip) || clip == null) return;

            foreach (var fgcc in UnityEngine.Object.FindObjectsOfType<FallGuysCharacterController>())
                Track(fgcc.gameObject, clip);

            var mm = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
            if (mm != null)
            {
                if (mm._menuFallGuy != null) Track(mm._menuFallGuy, clip);
                if (mm._lobbyFallGuy != null) Track(mm._lobbyFallGuy, clip);
            }
        }

        private static void Track(GameObject root, AnimationClip clip)
        {
            var skel = FindChildNamed(root.transform, "SKELETON");
            if (skel == null) return;
            var eyeL = FindChildNamed(skel, "Eye_L_jnt");
            var eyeR = FindChildNamed(skel, "Eye_R_jnt");
            if (eyeL == null || eyeR == null) return;

            if (_bakedFrames == 0)
            {
                Bake(clip, skel.parent != null ? skel.parent.gameObject : skel.gameObject, eyeL, eyeR);
                if (_bakedFrames == 0) return;
            }

            if (_count == _blinkers.Length) Array.Resize(ref _blinkers, _count * 2);
            _blinkers[_count].eyeL = eyeL;
            _blinkers[_count].eyeR = eyeR;
            _blinkers[_count].speed = UnityEngine.Random.Range(0.8f, 1.25f) * _playbackRate;
            _blinkers[_count].cursor = UnityEngine.Random.Range(0f, _bakedFrames);
            _count++;
        }

        private static void Bake(AnimationClip clip, GameObject host, Transform eyeL, Transform eyeR)
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

        void LateUpdate()
        {
            if (_count == 0) return;

            float dt = Time.deltaTime;
            int frames = _bakedFrames;

            for (int i = 0; i < _count; i++)
            {
                var eyeL = _blinkers[i].eyeL;
                var eyeR = _blinkers[i].eyeR;
                if (eyeL.m_CachedPtr == IntPtr.Zero || eyeR.m_CachedPtr == IntPtr.Zero)
                {
                    _count--;
                    _blinkers[i] = _blinkers[_count];
                    _blinkers[_count].eyeL = null;
                    _blinkers[_count].eyeR = null;
                    i--;
                    continue;
                }

                float c = _blinkers[i].cursor + dt * _blinkers[i].speed;
                if (c >= frames) c -= frames * Mathf.Floor(c / frames);
                _blinkers[i].cursor = c;

                int idx = (int)c;
                if (idx >= frames) idx = frames - 1;

                eyeL.SetLocalPositionAndRotation(_lPos[idx], _lRot[idx]);
                eyeL.localScale = _lScale[idx];
                eyeR.SetLocalPositionAndRotation(_rPos[idx], _rRot[idx]);
                eyeR.localScale = _rScale[idx];
            }
        }

        private static Transform FindChildNamed(Transform root, string name)
        {
            if (root == null) return null;
            var stack = new System.Collections.Generic.Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c.name == name) return c;
                    stack.Push(c);
                }
            }
            return null;
        }
    }
}
