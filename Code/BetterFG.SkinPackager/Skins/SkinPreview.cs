using UnityEditor;
using UnityEngine;

namespace BetterFG.Editor
{
    public class SkinPreview
    {
        private const string BEAN_PREFAB = "Assets/assets/reference/animationbean/Animation Bean.prefab";
        private static readonly Color BG = new Color(0.13f, 0.135f, 0.15f);

        private PreviewRenderUtility _util;
        private GameObject _instance;
        private GameObject _baseInstance;
        private GameObject _source;
        private bool _showBase;
        private Bounds _bounds;

        public Vector2 Orbit = new Vector2(150f, 8f);
        public float Zoom = 1f;

        public void SetSource(GameObject source)
        {
            if (ReferenceEquals(_source, source) && _instance != null) return;
            _source = source;
            Rebuild();
        }

        public void SetShowBase(bool show)
        {
            if (_showBase == show) return;
            _showBase = show;
            Rebuild();
        }

        public void Rebuild()
        {
            if (_instance != null) { Object.DestroyImmediate(_instance); _instance = null; }
            if (_baseInstance != null) { Object.DestroyImmediate(_baseInstance); _baseInstance = null; }
            if (_source == null) return;

            _util = _util ?? new PreviewRenderUtility();

            _instance = Spawn(_source);

            if (_showBase)
            {
                var bean = AssetDatabase.LoadAssetAtPath<GameObject>(BEAN_PREFAB);
                if (bean == null)
                {
                    var guids = AssetDatabase.FindAssets("\"Animation Bean\" t:Prefab");
                    if (guids.Length > 0)
                        bean = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
                if (bean != null) _baseInstance = Spawn(bean);
                else Debug.LogWarning("no Animation Bean prefab in the project, so the base bean can't show in the preview. BettrFG > References > Animation Dummy imports it");
            }

            _bounds = Measure(_instance);
            if (_baseInstance != null) _bounds.Encapsulate(Measure(_baseInstance));
        }

        private GameObject Spawn(GameObject src)
        {
            var go = Object.Instantiate(src);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.forceMatrixRecalculationPerRender = true;
                smr.updateWhenOffscreen = true;
            }

            _util.AddSingleGO(go);
            return go;
        }

        private static Bounds Measure(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        public void Draw(Rect rect, float scale)
        {
            HandleInput(rect);

            if (Event.current.type != EventType.Repaint) return;

            EditorGUI.DrawRect(rect, BG);
            if (_source == null || _instance == null) return;

            if (scale <= 0f) scale = 1f;
            _instance.transform.localScale = Vector3.one * scale;
            if (_baseInstance != null) _baseInstance.transform.localScale = Vector3.one * scale;

            _util.BeginPreview(rect, GUIStyle.none);

            var cam = _util.camera;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BG;
            cam.fieldOfView = 32f;

            float radius = Mathf.Max(_bounds.extents.magnitude, 0.1f);
            float dist = radius * 3.1f / Mathf.Max(Zoom, 0.05f);
            var pivot = _bounds.center;
            var rot = Quaternion.Euler(Orbit.y, Orbit.x, 0f);

            cam.transform.rotation = rot;
            cam.transform.position = pivot + rot * new Vector3(0f, 0f, -dist);
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = dist + radius * Mathf.Max(scale, 1f) * 6f + 1f;

            _util.ambientColor = new Color(0.32f, 0.33f, 0.36f);
            _util.lights[0].intensity = 1.15f;
            _util.lights[0].transform.rotation = Quaternion.Euler(38f, 40f, 0f);
            _util.lights[0].color = Color.white;
            _util.lights[1].intensity = 0.55f;
            _util.lights[1].transform.rotation = Quaternion.Euler(12f, -120f, 0f);
            _util.lights[1].color = new Color(0.75f, 0.8f, 1f);

            cam.Render();
            GUI.DrawTexture(rect, _util.EndPreview(), ScaleMode.StretchToFill, false);
        }

        private void HandleInput(Rect rect)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDrag && e.button == 0)
            {
                Orbit.x += e.delta.x * 0.6f;
                Orbit.y = Mathf.Clamp(Orbit.y + e.delta.y * 0.4f, -85f, 85f);
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel)
            {
                Zoom = Mathf.Clamp(Zoom * (1f - e.delta.y * 0.04f), 0.2f, 6f);
                e.Use();
            }
        }

        public void Cleanup()
        {
            if (_instance != null) { Object.DestroyImmediate(_instance); _instance = null; }
            if (_baseInstance != null) { Object.DestroyImmediate(_baseInstance); _baseInstance = null; }
            if (_util != null) { _util.Cleanup(); _util = null; }
            _source = null;
        }
    }
}
