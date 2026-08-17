using System.Collections.Generic;
using BetterFG.Features.TimePlacement;
using UnityEngine;

namespace BetterFG.Features.CustomizeFallGuys
{
    internal static class EyePreview
    {
        public const int Width = 256;
        public const int Height = 192;

        static readonly Color Backdrop = new Color(0.09f, 0.1f, 0.13f, 1f);

        static RenderTexture _rt;
        static Camera _cam;
        static Light[] _lights;
        static GameObject _host;

        static GameObject _bean;
        static readonly List<Renderer> _rends = new List<Renderer>();
        static readonly List<int> _layers = new List<int>();

        public static Texture Ensure()
        {
            if (_rt == null) Build();
            return _rt;
        }

        public static void Invalidate()
        {
            _bean = null;
            _rends.Clear();
            _layers.Clear();
        }

        public static void SetBean(GameObject bean)
        {
            if (_bean == bean && _rends.Count > 0) return;
            _bean = bean;
            _rends.Clear();
            _layers.Clear();
            if (bean == null) return;

            foreach (var r in bean.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r != null && r.enabled && r.gameObject.activeInHierarchy) _rends.Add(r);
            foreach (var r in bean.GetComponentsInChildren<MeshRenderer>(true))
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.GetComponent<TMPro.TMP_Text>() == null)
                    _rends.Add(r);

            for (int i = 0; i < _rends.Count; i++)
            {
                _layers.Add(_rends[i].gameObject.layer);
                var smr = _rends[i].TryCast<SkinnedMeshRenderer>();
                if (smr != null) smr.updateWhenOffscreen = true;
            }
        }

        public static void Render()
        {
            if (_rends.Count == 0 || _bean == null) return;

            for (int i = 0; i < _rends.Count; i++)
            {
                if (_rends[i] != null && _rends[i].m_CachedPtr != System.IntPtr.Zero) continue;
                Invalidate();
                return;
            }

            if (_rt == null) Build();

            var body = _rends[0].bounds;
            for (int i = 0; i < _rends.Count; i++)
            {
                body.Encapsulate(_rends[i].bounds);
                _rends[i].gameObject.layer = LeaderboardMugshotScene.Layer;
            }

            var fwd = _bean.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            LeaderboardMugshotScene.FrameHead(_cam, body, fwd);

            var prevActive = RenderTexture.active;
            _cam.targetTexture = _rt;
            LeaderboardMugshotScene.PushLighting(_lights);
            _cam.Render();
            LeaderboardMugshotScene.PopLighting(_lights);
            _cam.targetTexture = null;
            RenderTexture.active = prevActive;

            for (int i = 0; i < _rends.Count; i++)
                _rends[i].gameObject.layer = _layers[i];
        }

        static void Build()
        {
            _host = new GameObject("BettrFG_EyePreviewCam");
            _host.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(_host);

            _cam = LeaderboardMugshotScene.BuildCamera(_host, out _lights);
            _cam.aspect = (float)Width / Height;
            _cam.backgroundColor = Backdrop;

            _rt = new RenderTexture(Width, Height, 16, RenderTextureFormat.RGB565);
            _rt.filterMode = FilterMode.Bilinear;
            _rt.Create();
        }
    }
}
