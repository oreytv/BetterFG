using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Utilities
{
    public static class Bundles
    {
        private static readonly Dictionary<string, AssetBundle> _byKey = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        public static AssetBundle Get(string key)
            => !string.IsNullOrEmpty(key) && _byKey.TryGetValue(key, out var ab) && ab != null ? ab : null;

        public static bool TryGet(string key, out AssetBundle ab) { ab = Get(key); return ab != null; }

        public static void Register(string key, AssetBundle ab)
        {
            if (string.IsNullOrEmpty(key) || ab == null) return;
            _byKey[key] = ab;
        }

        public static IEnumerator LoadFile(string key, string path, Action<AssetBundle> done)
        {
            var cached = Get(key);
            if (cached != null) { done?.Invoke(cached); yield break; }

            var req = AssetBundle.LoadFromFileAsync(path);
            yield return req;
            var ab = req.assetBundle;
            if (ab != null && !string.IsNullOrEmpty(key)) _byKey[key] = ab;
            done?.Invoke(ab);
        }

        public static IEnumerator LoadMemory(string key, byte[] bytes, Action<AssetBundle> done)
        {
            var cached = Get(key);
            if (cached != null) { done?.Invoke(cached); yield break; }

            var req = AssetBundle.LoadFromMemoryAsync(bytes);
            yield return req;
            var ab = req.assetBundle;
            if (ab != null && !string.IsNullOrEmpty(key)) _byKey[key] = ab;
            done?.Invoke(ab);
        }

        public static AssetBundle LoadMemorySync(string key, byte[] bytes)
        {
            var cached = Get(key);
            if (cached != null) return cached;

            AssetBundle ab;
            try { ab = AssetBundle.LoadFromMemory(bytes); } catch { ab = null; }
            if (ab != null && !string.IsNullOrEmpty(key)) _byKey[key] = ab;
            return ab;
        }

        public static void Unload(string key, bool unloadAllObjects)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!_byKey.TryGetValue(key, out var ab)) return;
            try { if (ab != null) ab.Unload(unloadAllObjects); } catch { }
            _byKey.Remove(key);
        }
    }

    internal class EmbeddedResourceandUnity
    {
        static Assembly asm = Assembly.GetExecutingAssembly();

        public static Texture2D LoadTexture(string resourcePath)
        {
            using (Stream stream = asm.GetManifestResourceStream(resourcePath))
            {
                if (stream == null)
                {
                    Plugin.Log.LogWarning($"EmbeddedRes: no resource at '{resourcePath}'");
                    return null;
                }

                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);

                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.filterMode = FilterMode.Bilinear;
                // default Repeat wrap bleeds a 1px sliver of the opposite edge into bilinear-sampled UVs
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply();
                // keep it alive across scene unloads so cached references don't go dead between rounds
                tex.hideFlags = HideFlags.HideAndDontSave;
                return tex;
            }
        }

        public static string LoadText(string resourcePath)
        {
            using (Stream stream = asm.GetManifestResourceStream(resourcePath))
            {
                if (stream == null)
                {
                    Plugin.Log.LogWarning($"EmbeddedRes: no resource at '{resourcePath}'");
                    return null;
                }

                using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        public static Sprite LoadSprite(string resourcePath, float pixelsPerUnit = 100f)
        {
            Texture2D tex = LoadTexture(resourcePath);
            if (tex == null) return null;

            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit
            );
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        public static Sprite LoadSprite(string resourcePath, Rect rect, Vector2 pivot, float pixelsPerUnit = 100f)
        {
            Texture2D tex = LoadTexture(resourcePath);
            if (tex == null) return null;

            var sprite = Sprite.Create(tex, rect, pivot, pixelsPerUnit);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}