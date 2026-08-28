using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Features.TimePlacement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterFG.Customization.Pets
{
    // per-pet row thumbnail: a square, angled top-down shot of the live PetPreview bean with a real
    // transparent background. the bean's body shader forces alpha 0 so a single ARGB render drops the
    // whole body - instead render twice (black bg, white bg) and recover alpha from the difference,
    // the same trick LeaderboardMugshotScene documents. png lands next to the settings.
    internal static class PetThumb
    {
        const int Size = 256;
        const int Layer = 31; // LeaderboardMugshotScene.Layer - PetPreview already parks its clone here

        static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BettrFG", "Settings", "pets");

        static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        public static string PathFor(string id) => Path.Combine(Dir, id + ".png");

        public static void Invalidate(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _cache.Remove(id);
            try { var p = PathFor(id); if (File.Exists(p)) File.Delete(p); } catch { }
        }

        public static Texture2D Load(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_cache.TryGetValue(id, out var cached) && cached != null) return cached;

            string path = PathFor(id);
            if (!File.Exists(path)) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(path));
                tex.hideFlags = HideFlags.HideAndDontSave;
                _cache[id] = tex;
                return tex;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"pet thumb {id} wouldn't load: {ex.Message}"); return null; }
        }

        public static void Capture(string id)
        {
            if (string.IsNullOrEmpty(id) || PetPreview.Clone == null) return;

            var bounds = RendererBounds(PetPreview.Clone);
            if (bounds.size == Vector3.zero) return;

            GameObject host = null;
            RenderTexture rtA = null, rtB = null;
            var prevActive = RenderTexture.active;
            Light[] lights = null;
            try
            {
                host = new GameObject("BettrFG_PetThumbCam") { hideFlags = HideFlags.HideAndDontSave };
                var cam = LeaderboardMugshotScene.BuildCamera(host, out lights);
                cam.aspect = 1f;

                // angled, looking DOWN onto the front of the bean (yaw ~180 from behind so we see its
                // face, not its back) - not the flat side view FrameBody gives
                Vector3 lookDir = (Quaternion.Euler(42f, 202f, 0f) * Vector3.forward).normalized;
                float extent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                cam.transform.position = bounds.center - lookDir * 8f;
                cam.transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
                cam.orthographicSize = extent * 0.62f;

                rtA = NewRT(); rtB = NewRT();
                LeaderboardMugshotScene.PushLighting(lights);

                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.targetTexture = rtA; cam.Render();
                cam.backgroundColor = Color.white;
                cam.targetTexture = rtB; cam.Render();
                cam.targetTexture = null;

                LeaderboardMugshotScene.PopLighting(lights);

                byte[] png = Composite(rtA, rtB);
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(PathFor(id), png);
                _cache.Remove(id);
                Plugin.Log.LogInfo($"pet thumb saved for {id}, {png.Length / 1024}kb");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"pet thumb capture failed: {ex.Message}"); }
            finally
            {
                RenderTexture.active = prevActive;
                if (rtA != null) { rtA.Release(); UnityEngine.Object.Destroy(rtA); }
                if (rtB != null) { rtB.Release(); UnityEngine.Object.Destroy(rtB); }
                if (host != null) UnityEngine.Object.Destroy(host);
            }
        }

        static RenderTexture NewRT()
        {
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            return rt;
        }

        // alpha = how much the two backgrounds didn't bleed through; the black shot is premultiplied
        // by that alpha so divide it back out (same as BeanPortraits). clean cutout around the bean.
        static byte[] Composite(RenderTexture black, RenderTexture white)
        {
            var full = new Rect(0f, 0f, Size, Size);

            var a = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            RenderTexture.active = black;
            a.ReadPixels(full, 0, 0, false);

            var b = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            RenderTexture.active = white;
            b.ReadPixels(full, 0, 0, false);

            var pa = a.GetPixels();
            var pb = b.GetPixels();
            LeaderboardMugshotScene.AlphaFromAB(pa, pb);

            var outTex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            outTex.SetPixels(pa);
            outTex.Apply(false);
            byte[] png = outTex.EncodeToPNG();

            UnityEngine.Object.Destroy(a);
            UnityEngine.Object.Destroy(b);
            UnityEngine.Object.Destroy(outTex);
            return png;
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(false);
            Bounds b = default;
            bool any = false;
            foreach (var r in rends)
            {
                if (r == null || !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any ? b : default;
        }
    }
}
