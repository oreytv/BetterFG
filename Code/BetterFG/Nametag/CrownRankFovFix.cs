using System.Collections.Generic;
using FGClient;
using UnityEngine;

namespace BetterFG.Nametag
{
    public static class CrownRankFovFix
    {
        class Entry
        {
            public Transform root;
            public Transform container;
            public Vector3 basePos;
            public Vector3 baseScale;
            public Vector3 lastScale;
        }

        static readonly Dictionary<int, Entry> _tags = new Dictionary<int, Entry>();
        static readonly HashSet<int> _skip = new HashSet<int>();
        static Camera _levelCam;
        static Camera _uiCam;
        static int _crownLayer = 5;

        public static void Apply(PlayerInfoHUDBase hud)
        {
            var cam = Camera.main;
            if (cam == null) cam = hud._levelCamera;
            if (cam == null || cam.orthographic) return;

            var spawned = hud._spawnedInfoObjects;
            if (spawned == null || spawned.Count == 0) return;

            if (_levelCam != cam)
            {
                _levelCam = cam;
                _uiCam = null;
                foreach (var cand in cam.GetComponentsInChildren<Camera>(true))
                {
                    if (cand.GetInstanceID() == cam.GetInstanceID()) continue;
                    if ((cand.cullingMask & (1 << _crownLayer)) == 0) continue;
                    _uiCam = cand;
                    break;
                }
            }
            if (_uiCam == null || _uiCam.orthographic) return;

            float mainTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            if (mainTan <= 0.0001f) return;
            float k = Mathf.Tan(_uiCam.fieldOfView * 0.5f * Mathf.Deg2Rad) / mainTan;

            var view = cam.worldToCameraMatrix;
            var world = cam.cameraToWorldMatrix;

            for (int i = 0; i < spawned.Count; i++)
            {
                var display = spawned[i].playerInfo;
                if (display == null) continue;

                int id = display.GetInstanceID();
                if (_skip.Contains(id)) continue;

                if (!_tags.TryGetValue(id, out var e) || e.container == null)
                {
                    e = Build(display, id);
                    if (e == null) continue;
                }

                // .position/.localScale each box through runtime_invoke; the PositionAndRotation pair are
                // direct calls, which matters when this runs for every tag on screen every frame
                e.root.GetPositionAndRotation(out var rootPos, out _);
                e.container.GetPositionAndRotation(out _, out var rot);

                var v = view.MultiplyPoint3x4(rootPos);
                e.container.SetPositionAndRotation(world.MultiplyPoint3x4(new Vector3(v.x * k, v.y * k, v.z)), rot);

                var scale = new Vector3(e.baseScale.x * k, e.baseScale.y * k, e.baseScale.z);
                if (e.lastScale != scale) { e.container.localScale = scale; e.lastScale = scale; }
            }
        }

        static Entry Build(PlayerInfoDisplay display, int id)
        {
            var go = display.TryCast<PlayerInfoDisplayGameObject>();
            var helper = go != null ? go._crownRankPlayerTagLayoutHelper : null;
            var crown = helper != null ? helper.crownRankObject : null;

            if (go == null || (crown != null && crown.layer == display.gameObject.layer))
            {
                _skip.Add(id);
                return null;
            }
            if (crown == null || crown.transform.childCount == 0) return null;

            var container = crown.transform.GetChild(0);
            var e = new Entry
            {
                root = crown.transform,
                container = container,
                basePos = Vector3.zero,
                baseScale = Vector3.one,
            };
            _tags[id] = e;
            return e;
        }

        public static void Forget()
        {
            _tags.Clear();
            _skip.Clear();
            _levelCam = null;
            _uiCam = null;
        }
    }
}
