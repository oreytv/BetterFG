using System;
using System.Collections.Generic;
using System.IO;
using FG.Common;
using FG.Common.LevelEditor.Serialization;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using LevelEditor;
using UnityEngine;

namespace BetterFG.Features.CreativeGroups
{
    public static class SavedGroups
    {
        public sealed class Saved
        {
            public string Name;
            public string Json;
            public string Image;
            public Texture2D Preview;
            public bool PreviewTried;
        }

        private const int ShotSize = 256;
        private const int ShotLayer = 31;

        private static readonly List<Saved> _all = new List<Saved>();
        private static bool _scanned;

        private static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BettrFG", "Settings", "saved_groups");

        public static List<Saved> All()
        {
            if (_scanned) return _all;
            _scanned = true;
            _all.Clear();

            string dir = Dir;
            if (!Directory.Exists(dir)) return _all;

            foreach (string path in Directory.GetFiles(dir, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                _all.Add(new Saved
                {
                    Name = name,
                    Json = path,
                    Image = Path.ChangeExtension(path, ".png"),
                });
            }
            _all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Plugin.Log.LogInfo($"{_all.Count} saved group(s) in {dir}");
            return _all;
        }

        public static bool Exists(string name)
        {
            string clean = Sanitise(name);
            foreach (var g in All())
                if (string.Equals(g.Name, clean, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static Texture2D PreviewOf(Saved g)
        {
            if (g.PreviewTried) return g.Preview;
            g.PreviewTried = true;
            if (!File.Exists(g.Image)) return null;

            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.LoadImage(File.ReadAllBytes(g.Image));
            tex.wrapMode = TextureWrapMode.Clamp;
            g.Preview = tex;
            return tex;
        }

        public static int Save(string name, out string status)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null || sel.Count == 0) { status = "nothing selected"; return 0; }

            var objs = new List<LevelEditorPlaceableObject>();
            foreach (var o in sel) if (o != null) objs.Add(o);

            var schemas = new List<UGCObjectDataSchema>();
            var centre = Vector3.zero;
            int noSchema = 0;
            foreach (var o in objs)
            {
                var schema = LevelSaver.GetObjectSchema(o);
                if (schema == null || schema.Position == null || schema.Position.Length < 3) { noSchema++; continue; }
                schemas.Add(schema);
                centre += new Vector3(schema.Position[0], schema.Position[1], schema.Position[2]);
            }

            if (schemas.Count == 0)
            {
                status = "none of that could be written out";
                Plugin.Log.LogWarning($"tried to save a group of {objs.Count} object(s) and got no schemas back at all");
                return 0;
            }
            centre /= schemas.Count;

            var flat = new List<UGCObjectDataSchema>();
            foreach (var s in schemas) Walk(s, flat.Add);
            foreach (var node in flat)
            {
                Offset(node.Position, -centre);
                Offset(node.PainterPos, -centre);
                node.SnapStatus = new Il2CppSystem.Nullable<bool>(false);
                node.SnapTargetGuid = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
                node.SnappedWith = new Il2CppSystem.Collections.Generic.List<UGCObjectDataSchema.SnapTarget>();
                node.SnapExceptions = new Il2CppSystem.Collections.Generic.List<UGCObjectDataSchema.SnapTarget>();
            }

            var level = new UGCLevelDataSchema
            {
                Version = LevelIO.CurrentVersion.ToString(),
                LevelName = name,
                OtherObjects = new Il2CppReferenceArray<UGCObjectDataSchema>(schemas.Count),
            };
            for (int i = 0; i < schemas.Count; i++) level.OtherObjects[i] = schemas[i];

            string clean = Sanitise(name);
            string dir = Dir;
            Directory.CreateDirectory(dir);
            string json = Path.Combine(dir, clean + ".json");
            bool replacing = File.Exists(json);
            File.WriteAllText(json, UGCJsonSerializer.SerializeObject(level, true));

            var png = Snapshot(objs);
            string image = Path.Combine(dir, clean + ".png");
            if (png != null) File.WriteAllBytes(image, png);
            else if (File.Exists(image)) File.Delete(image);

            _scanned = false;
            status = $"{(replacing ? "replaced" : "saved")} {clean}, {schemas.Count} object(s)";
            Plugin.Log.LogInfo($"group '{clean}' {(replacing ? "overwritten" : "saved")} — {schemas.Count} object(s), "
                + $"{flat.Count} node(s) once children/links are counted, centred on {centre}"
                + (noSchema > 0 ? $", {noSchema} wouldn't serialise" : "")
                + (png == null ? ", no preview (nothing renderable in there)" : $", preview {png.Length / 1024}kb"));
            return schemas.Count;
        }

        public static void Delete(Saved g)
        {
            if (File.Exists(g.Json)) File.Delete(g.Json);
            if (File.Exists(g.Image)) File.Delete(g.Image);
            if (g.Preview != null) UnityEngine.Object.Destroy(g.Preview);
            _scanned = false;
            Plugin.Log.LogInfo($"binned the saved group {g.Name}");
        }

        public static int Place(Saved g, out string status, out LevelEditorPlaceableObject middle)
        {
            middle = null;
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) { status = "not in the editor"; return 0; }

            var level = UGCJsonSerializer.DeserializeLevelData(File.ReadAllText(g.Json));
            var schemas = level != null ? level.OtherObjects : null;
            if (schemas == null || schemas.Length == 0)
            {
                status = "that group file came back empty";
                Plugin.Log.LogWarning($"{g.Name}.json parsed but held no objects");
                return 0;
            }

            var reticle = mgr.GetReticleGameObject();
            var origin = reticle != null ? reticle.transform.position : Vector3.zero;

            var flat = new List<UGCObjectDataSchema>();
            for (int i = 0; i < schemas.Length; i++) Walk(schemas[i], flat.Add);

            Remap(flat);
            foreach (var node in flat)
            {
                Offset(node.Position, origin);
                Offset(node.PainterPos, origin);
            }

            var placed = new List<LevelEditorPlaceableObject>();
            int registered = 0, noLepo = 0;
            for (int i = 0; i < schemas.Length; i++)
            {
                var go = LevelLoader.LoadObject(schemas[i], false);
                if (go == null) { noLepo++; continue; }

                var lepo = go.GetComponent<LevelEditorPlaceableObject>();
                if (lepo == null) { noLepo++; continue; }

                if (!LevelIO.IsObjectRegistered(lepo))
                {
                    mgr.RegisterObject(lepo, true, false, false);
                    registered++;
                }
                placed.Add(lepo);
            }

            if (placed.Count == 0)
            {
                status = "nothing came out of that group";
                Plugin.Log.LogWarning($"{g.Name}: {schemas.Length} schema(s) in, zero objects out");
                return 0;
            }

            var centre = Vector3.zero;
            foreach (var p in placed) centre += p.Position;
            centre /= placed.Count;
            float nearest = float.MaxValue;
            foreach (var p in placed)
            {
                float d = (p.Position - centre).sqrMagnitude;
                if (d >= nearest) continue;
                nearest = d;
                middle = p;
            }

            int group = CreativeGroups.CreateGroup(g.Name, placed);
            status = $"{placed.Count} object(s) placed";
            Plugin.Log.LogInfo($"'{g.Name}' dropped at {origin} — {placed.Count} of {schemas.Length} object(s), group {group}, "
                + (registered == 0 ? "the loader registered them all itself" : $"{registered} needed registering by hand")
                + (noLepo > 0 ? $", {noLepo} never turned into a placeable" : ""));
            return placed.Count;
        }

        private static void Walk(UGCObjectDataSchema schema, Action<UGCObjectDataSchema> fn)
        {
            if (schema == null) return;
            fn(schema);
            WalkAll(schema.Children, fn);
            WalkAll(schema.Receivers, fn);
            WalkAll(schema.Triggers, fn);
            WalkAll(schema.WallsObjs, fn);
            WalkAll(schema.WaypointObjects, fn);

            var comps = schema.Components;
            if (comps == null) return;
            for (int i = 0; i < comps.Count; i++) Walk(comps[i], fn);
        }

        private static void WalkAll(Il2CppReferenceArray<UGCObjectDataSchema> arr, Action<UGCObjectDataSchema> fn)
        {
            if (arr == null) return;
            for (int i = 0; i < arr.Length; i++) Walk(arr[i], fn);
        }

        private static void Offset(Il2CppStructArray<float> pos, Vector3 by)
        {
            if (pos == null || pos.Length < 3 || pos.Length % 3 != 0) return;
            for (int i = 0; i < pos.Length; i += 3)
            {
                pos[i] += by.x;
                pos[i + 1] += by.y;
                pos[i + 2] += by.z;
            }
        }

        private static void Remap(List<UGCObjectDataSchema> flat)
        {
            var map = new Dictionary<string, Il2CppSystem.Guid>();
            foreach (var node in flat)
            {
                if (!Read(() => node.GUID, out var had)) continue;
                string key = had.ToString();
                if (!map.ContainsKey(key)) map[key] = Il2CppSystem.Guid.NewGuid();
            }

            int dangling = 0;
            foreach (var node in flat)
            {
                node.GUID = Swap(() => node.GUID, map, ref dangling);
                node.SnapTargetGuid = Swap(() => node.SnapTargetGuid, map, ref dangling);
                node.OtherGuid = Swap(() => node.OtherGuid, map, ref dangling);
                node.PillarAGuid = Swap(() => node.PillarAGuid, map, ref dangling);
                node.PillarBGuid = Swap(() => node.PillarBGuid, map, ref dangling);
            }

            Plugin.Log.LogInfo($"{map.Count} guid(s) reissued across {flat.Count} node(s) for this drop"
                + (dangling > 0 ? $", {dangling} reference(s) pointed outside the group and got cleared" : ""));
        }

        private static bool Read(Func<Il2CppSystem.Nullable<Il2CppSystem.Guid>> get, out Il2CppSystem.Guid guid)
        {
            guid = default;
            try
            {
                var n = get();
                if (n == null || !n.HasValue) return false;
                guid = n.Value;
                return true;
            }
            catch { return false; }
        }

        private static Il2CppSystem.Nullable<Il2CppSystem.Guid> Swap(
            Func<Il2CppSystem.Nullable<Il2CppSystem.Guid>> get, Dictionary<string, Il2CppSystem.Guid> map, ref int dangling)
        {
            var none = new Il2CppSystem.Nullable<Il2CppSystem.Guid>();
            if (!Read(get, out var had)) return none;
            if (map.TryGetValue(had.ToString(), out var fresh))
                return new Il2CppSystem.Nullable<Il2CppSystem.Guid>(fresh);
            dangling++;
            return none;
        }

        private static string Sanitise(string name)
        {
            var sb = new System.Text.StringBuilder(32);
            var bad = Path.GetInvalidFileNameChars();
            foreach (char c in name ?? "")
            {
                if (char.IsControl(c) || Array.IndexOf(bad, c) >= 0) continue;
                sb.Append(c);
                if (sb.Length >= 40) break;
            }
            string clean = sb.ToString().Trim(' ', '.');
            return clean.Length == 0 ? "Group" : clean;
        }

        private static byte[] Snapshot(List<LevelEditorPlaceableObject> objs)
        {
            Bounds bounds = default;
            bool any = false;
            foreach (var o in objs)
            {
                var rends = o.GetComponentsInChildren<Renderer>(false);
                for (int i = 0; rends != null && i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null || !r.enabled) continue;
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
            }
            if (!any) return null;

            var stage = new GameObject("BettrFG_GroupShot");
            stage.transform.position = new Vector3(0f, 9000f, 0f);

            int meshes = 0;
            foreach (var o in objs)
            {
                var rends = o.GetComponentsInChildren<MeshRenderer>(false);
                for (int i = 0; rends != null && i < rends.Length; i++)
                {
                    var r = rends[i];
                    if (r == null || !r.enabled) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;

                    var go = new GameObject("m");
                    go.layer = ShotLayer;
                    go.transform.SetParent(stage.transform, false);
                    var src = r.transform;
                    go.transform.SetPositionAndRotation(stage.transform.position + (src.position - bounds.center), src.rotation);
                    go.transform.localScale = src.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
                    meshes++;
                }
            }

            if (meshes == 0)
            {
                UnityEngine.Object.Destroy(stage);
                Plugin.Log.LogWarning("group preview found renderer bounds but no mesh to copy, skipping the shot");
                return null;
            }

            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);

            var lightGo = new GameObject("BettrFG_GroupShotLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.cullingMask = 1 << ShotLayer;
            lightGo.transform.rotation = Quaternion.Euler(42f, 145f, 0f);

            var camGo = new GameObject("BettrFG_GroupShotCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = radius * 1.08f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.1f, 1f);
            cam.cullingMask = 1 << ShotLayer;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = radius * 12f + 200f;
            var back = new Vector3(-0.62f, 0.55f, -0.92f).normalized;
            camGo.transform.position = stage.transform.position + back * (radius * 5f);
            camGo.transform.LookAt(stage.transform.position);

            var rt = new RenderTexture(ShotSize, ShotSize, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(ShotSize, ShotSize, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0f, 0f, ShotSize, ShotSize), 0, 0, false);
            tex.Apply(false);
            RenderTexture.active = was;

            cam.targetTexture = null;
            var png = tex.EncodeToPNG();

            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(tex);
            UnityEngine.Object.Destroy(camGo);
            UnityEngine.Object.Destroy(lightGo);
            UnityEngine.Object.Destroy(stage);

            Plugin.Log.LogInfo($"group preview: {meshes} mesh(es), {radius:0.#} unit radius, {ShotSize}x{ShotSize}");
            return png;
        }
    }
}
