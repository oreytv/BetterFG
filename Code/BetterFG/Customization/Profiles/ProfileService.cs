using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BetterFG.Customization.Player;
using BetterFG.Services;
using FallGuysLib.Players;
using UnityEngine;

namespace BetterFG.Customization.Profiles
{
    public static class ProfileService
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BettrFG", "Settings", "profiles");

        private const string EnabledKey = "profile.enabled.";

        public static List<string> List()
        {
            var names = new List<string>();
            if (!Directory.Exists(Dir)) return names;
            foreach (var f in Directory.GetFiles(Dir, "*.bfgprofile"))
                names.Add(Path.GetFileNameWithoutExtension(f));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static string LocalPlayerName()
        {
            try { return PlayerUtils.CleanPlayerName(FGClient.GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? ""); }
            catch { return ""; }
        }

        public static bool IsEnabled(string name) => SettingsService.Get(EnabledKey + name, "true") == "true";
        public static void SetEnabled(string name, bool on) => SettingsService.Set(EnabledKey + name, on ? "true" : "false");

        public static void Delete(string name)
        {
            try
            {
                string path = Path.Combine(Dir, Sanitize(name) + ".bfgprofile");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) { Plugin.Log.LogError("delete: " + ex.Message); }
        }

        public static void SaveCurrentToPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return;
            var p = BfgProfile.FromLocal();
            p.StampLocalIdentity();
            p.name = Sanitize(Path.GetFileNameWithoutExtension(fullPath));
            Embed(p);
            try
            {
                File.WriteAllText(fullPath, p.ToJson());
                var fi = new FileInfo(fullPath);
                Plugin.Log.LogInfo($"exported {p.name} -> {fullPath} ({fi.Length / 1024}kb)");
                if (fi.Length > 2 * 1024 * 1024)
                    Plugin.Log.LogWarning($"that profile is {fi.Length / 1024 / 1024}mb — a big nameplate backing or skin texture will make it slow to load on everyone else's machine");
            }
            catch (Exception ex) { Plugin.Log.LogError("export: " + ex.Message); }
        }

        private static void Embed(BfgProfile p)
        {
            foreach (var s in p.skins)
            {
                if (string.IsNullOrEmpty(s.file)) continue;
                bool local = s.source == "local" || string.IsNullOrEmpty(s.repoUrl);
                if (!local || string.IsNullOrEmpty(s.localPath) || !File.Exists(s.localPath)) continue;
                try { s.bundleB64 = Convert.ToBase64String(File.ReadAllBytes(s.localPath)); }
                catch (Exception ex) { Plugin.Log.LogWarning("embed " + s.file + ": " + ex.Message); }
            }
            p.skins.RemoveAll(s => SkinTypeParser.FromString(s.type) == SkinType.Plinth);

            EmbedPlinth(p);

            if (p.Get("nametag.icon.mode") == "custom")
                p.iconB64 = ReadB64(p.Get("nametag.icon.path"));
            if (p.Get("nametag.backing.enabled") == "true")
                p.backingB64 = EmbedBacking(p);

            foreach (var e in SkinApplicationService.ReadTexEntries(p.Get))
            {
                if (!e.enabled) continue;
                foreach (var ov in e.overrides)
                {
                    if (string.IsNullOrEmpty(ov.texPath) || !File.Exists(ov.texPath)) continue;
                    string b64 = ReadB64(ov.texPath);
                    if (b64 != null) p.textures.Add(new TexEmbed { fileName = Path.GetFileName(ov.texPath), b64 = b64 });
                }
            }

            foreach (var e in Social.EmoticonSettingsService.Load())
                if (e.enabled) p.socials.Add(EmbedSocial(false, e.slot, "", e.imagePath, e.soundPaths));
            foreach (var e in Social.PhraseSettingsService.Load())
                if (e.enabled) p.socials.Add(EmbedSocial(true, e.slot, e.phraseText, e.imagePath, e.soundPaths));

            ScrubLocalPaths(p);
        }

        private static void ScrubLocalPaths(BfgProfile p)
        {
            foreach (var key in new List<string>(p.settings.Keys))
            {
                if (!key.EndsWith("path", StringComparison.OrdinalIgnoreCase) &&
                    !key.EndsWith("paths", StringComparison.OrdinalIgnoreCase)) continue;
                string val = p.settings[key];
                if (string.IsNullOrEmpty(val)) continue;
                string[] parts = val.Split(',');
                for (int i = 0; i < parts.Length; i++)
                    if (parts[i].IndexOfAny(new[] { '\\', '/' }) >= 0) parts[i] = Path.GetFileName(parts[i]);
                p.settings[key] = string.Join(",", parts);
            }
        }

        private static string EmbedBacking(BfgProfile p)
        {
            string path = p.Get("nametag.backing.path");
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            if (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase)) return ReadB64(path);

            byte[] raw;
            try { raw = File.ReadAllBytes(path); }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't read the backing: {ex.Message}"); return null; }

            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(raw) || src.width < 2 || src.height < 2)
            {
                UnityEngine.Object.Destroy(src);
                return Convert.ToBase64String(raw);
            }

            int srcW = src.width, srcH = src.height;
            var canvas = BetterFG.Nametag.NametagIconApplicator.BackingCanvasSize();
            float W = canvas.x, H = canvas.y;
            float scale = Mathf.Max(0.01f, p.GetFloat("nametag.backing.scale", 1f));
            float offX = p.GetFloat("nametag.backing.offset.x", 0f);
            float offY = p.GetFloat("nametag.backing.offset.y", 0f);

            float drawH = H * scale;
            float drawW = drawH * ((float)srcW / srcH);
            float drawX = (W - drawW) * 0.5f + offX * W;
            float drawY = (H - drawH) * 0.5f + offY * H;

            float u0 = Mathf.Clamp01(-drawX / drawW), u1 = Mathf.Clamp01((W - drawX) / drawW);
            float v0 = Mathf.Clamp01(-drawY / drawH), v1 = Mathf.Clamp01((H - drawY) / drawH);
            if (u1 - u0 < 0.001f || v1 - v0 < 0.001f)
            {
                UnityEngine.Object.Destroy(src);
                Plugin.Log.LogWarning("backing is positioned entirely off the nameplate, exporting it whole");
                return Convert.ToBase64String(raw);
            }

            float visW = (u1 - u0) * drawW, visH = (v1 - v0) * drawH;
            int tw = Mathf.Clamp(Mathf.CeilToInt(visW * 2f), 1, Mathf.CeilToInt((u1 - u0) * srcW));
            int th = Mathf.Clamp(Mathf.CeilToInt(visH * 2f), 1, Mathf.CeilToInt((v1 - v0) * srcH));

            byte[] png = null;
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D cropped = null;
            try
            {
                Graphics.Blit(src, rt, new Vector2(u1 - u0, v1 - v0), new Vector2(u0, v0));
                RenderTexture.active = rt;
                cropped = new Texture2D(tw, th, TextureFormat.RGBA32, false);
                cropped.ReadPixels(new Rect(0f, 0f, tw, th), 0, 0);
                cropped.Apply();
                png = cropped.EncodeToPNG();
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"backing crop didn't take: {ex.Message}"); }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (cropped != null) UnityEngine.Object.Destroy(cropped);
                UnityEngine.Object.Destroy(src);
            }

            if (png == null || png.Length >= raw.Length) return Convert.ToBase64String(raw);

            float ndW = (u1 - u0) * drawW, ndH = (v1 - v0) * drawH;
            float ndX = drawX + u0 * drawW, ndY = drawY + v0 * drawH;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            p.settings["nametag.backing.scale"] = (ndH / H).ToString(ci);
            p.settings["nametag.backing.offset.x"] = ((ndX - (W - ndW) * 0.5f) / W).ToString(ci);
            p.settings["nametag.backing.offset.y"] = ((ndY - (H - ndH) * 0.5f) / H).ToString(ci);
            p.settings["nametag.backing.path"] = Path.GetFileName(path);
            p.nametag = p.BuildNametag();

            Plugin.Log.LogInfo($"backing cropped {srcW}x{srcH} -> {tw}x{th}, {raw.Length / 1024}kb down to {png.Length / 1024}kb");
            return Convert.ToBase64String(png);
        }

        private static string ReadB64(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try { return Convert.ToBase64String(File.ReadAllBytes(path)); }
            catch (Exception ex) { Plugin.Log.LogWarning($"couldn't embed {Path.GetFileName(path)}: {ex.Message}"); return null; }
        }

        private static SocialEmbed EmbedSocial(bool phrase, int slot, string text, string imagePath, string[] soundPaths)
        {
            var s = new SocialEmbed { phrase = phrase, slot = slot, text = text ?? "" };
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                s.imageB64 = ReadB64(imagePath);
                s.imageExt = Path.GetExtension(imagePath);
            }
            for (int i = 0; i < 3; i++)
            {
                string sp = soundPaths != null && i < soundPaths.Length ? soundPaths[i] : "";
                if (string.IsNullOrEmpty(sp) || !File.Exists(sp)) continue;
                s.soundB64[i] = ReadB64(sp);
                s.soundExt[i] = Path.GetExtension(sp);
            }
            return s;
        }

        private static void EmbedPlinth(BfgProfile p)
        {
            string[] files = p.Get("skin.multi.files").Split(',');
            string[] types = p.Get("skin.multi.types").Split(',');
            string[] sources = p.Get("skin.multi.sources").Split(',');
            string[] paths = p.Get("skin.multi.paths").Split(',');
            string[] repos = p.Get("skin.multi.repos").Split(',');
            string[] folders = p.Get("skin.multi.folders").Split(',');

            for (int i = 0; i < files.Length; i++)
            {
                if (SkinTypeParser.FromString(i < types.Length ? types[i].Trim() : "") != SkinType.Plinth) continue;

                var pe = new PlinthEmbed
                {
                    file = files[i].Trim(),
                    repoUrl = i < repos.Length ? repos[i].Trim() : "",
                    folder = i < folders.Length ? folders[i].Trim() : "",
                };
                string path = i < paths.Length ? paths[i].Trim() : "";
                pe.source = i < sources.Length ? sources[i].Trim() : "";

                if (pe.source == "game") { p.plinth = pe; return; }

                if (pe.source == "local" && !string.IsNullOrEmpty(path))
                    pe.bundleB64 = ReadB64(Path.Combine(path, pe.file));

                if (string.IsNullOrEmpty(pe.repoUrl) && string.IsNullOrEmpty(pe.bundleB64))
                {
                    Plugin.Log.LogWarning($"plinth '{pe.file}' has no repo url and no local bytes, leaving it out rather than shipping a dead link");
                    return;
                }

                p.plinth = pe;
                return;
            }
        }

        public static string Import(string srcPath)
        {
            if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath)) return null;
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);

                string incoming = PlayerUtils.CleanPlayerName(BfgProfile.PeekUsername(srcPath) ?? "");
                if (!string.IsNullOrEmpty(incoming))
                    foreach (var existing in List())
                    {
                        string have = PlayerUtils.CleanPlayerName(
                            BfgProfile.PeekUsername(Path.Combine(Dir, existing + ".bfgprofile")) ?? "");
                        if (have.Equals(incoming, StringComparison.OrdinalIgnoreCase)) Delete(existing);
                    }

                string name = Sanitize(Path.GetFileNameWithoutExtension(srcPath));
                File.Copy(srcPath, Path.Combine(Dir, name + ".bfgprofile"), true);
                SetEnabled(name, true);
                Plugin.Log.LogInfo($"imported '{name}' for {incoming}");
                return name;
            }
            catch (Exception ex) { Plugin.Log.LogError("import: " + ex.Message); return null; }
        }

        private static readonly Dictionary<string, BfgProfile> _byKey
            = new Dictionary<string, BfgProfile>(StringComparer.OrdinalIgnoreCase);

        private static List<BfgProfile> _cached;
        private static string _cacheSig;

        public static List<BfgProfile> GetRemoteProfiles()
        {
            var sb = new StringBuilder();
            foreach (var name in List())
            {
                var fi = new FileInfo(Path.Combine(Dir, name + ".bfgprofile"));
                sb.Append(name).Append(fi.Length).Append(fi.LastWriteTimeUtc.Ticks)
                  .Append(IsEnabled(name) ? '1' : '0').Append('|');
            }
            string sig = sb.ToString();
            if (_cached != null && sig == _cacheSig) return _cached;
            _cacheSig = sig;

            _byKey.Clear();
            Social.RemoteSocialDisplay.Clear();
            var list = new List<BfgProfile>();

            // more than one file can carry the same player's username - a leftover export, or a
            // manual copy into the folder that skipped Import()'s own dedup-by-username cleanup.
            // pick only the most recently written file per username, or an alphabetically-later
            // stale file would silently shadow the current one in _byKey below (stale loadout wins,
            // and it looks like a data bug rather than a duplicate file sitting on disk).
            var newestForUser = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var newestTime = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in List())
            {
                if (!IsEnabled(name)) continue;
                string path = Path.Combine(Dir, name + ".bfgprofile");
                string user = PlayerUtils.CleanPlayerName(BfgProfile.PeekUsername(path) ?? "");
                if (string.IsNullOrEmpty(user)) continue;
                var mtime = File.GetLastWriteTimeUtc(path);
                if (!newestTime.TryGetValue(user, out var have) || mtime > have)
                {
                    newestTime[user] = mtime;
                    newestForUser[user] = name;
                }
            }

            foreach (var name in List())
            {
                if (!IsEnabled(name)) continue;

                BfgProfile p;
                try { p = BfgProfile.FromJson(File.ReadAllText(Path.Combine(Dir, name + ".bfgprofile"))); }
                catch (Exception ex) { Plugin.Log.LogError($"load {name}: {ex.Message}"); continue; }
                if (p == null || string.IsNullOrEmpty(p.username)) continue;

                string cleanUser = PlayerUtils.CleanPlayerName(p.username);
                if (newestForUser.TryGetValue(cleanUser, out var keep) && !keep.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogInfo($"skipping stale profile '{name}', a newer one for the same player ('{keep}') is loaded instead");
                    continue;
                }

                p.name = name;
                p.requireKeyMatch = true;
                Unpack(p);

                Social.RemoteSocialDisplay.Set(p.CleanName, UnpackSocials(p));

                foreach (var k in p.AliasKeys()) _byKey[k] = p;

                list.Add(p);
            }

            _cached = list;
            return list;
        }

        public static string LoadedKeys()
        {
            if (_byKey.Count == 0) return "(none loaded)";
            var sb = new StringBuilder();
            foreach (var k in _byKey.Keys)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append('\'').Append(k).Append('\'');
            }
            return sb.ToString();
        }

        public static BfgProfile GetRemoteProfileForName(string cleanName)
        {
            if (string.IsNullOrEmpty(cleanName)) return null;
            return _byKey.TryGetValue(PlayerUtils.CleanPlayerName(cleanName), out var p) ? p : null;
        }

        private static void Unpack(BfgProfile p)
        {
            string bundles = Path.Combine(Dir, "_bundles");
            foreach (var s in p.skins)
            {
                if (string.IsNullOrEmpty(s.bundleB64)) continue;
                string path = Path.Combine(bundles, s.file);
                if (WriteB64(bundles, path, s.bundleB64))
                {
                    s.localPath = path;
                    s.source = "local";
                }
                s.bundleB64 = null;
            }

            string icons = Path.Combine(Dir, "_icons");
            if (p.nametag != null)
            {
                if (p.Get("nametag.icon.mode") == "custom" && !string.IsNullOrEmpty(p.iconB64))
                {
                    string ext = Path.GetExtension(p.Get("nametag.icon.path"));
                    if (string.IsNullOrEmpty(ext)) ext = ".png";
                    string path = Path.Combine(icons, Sanitize(p.name) + "_icon" + ext);
                    if (WriteB64(icons, path, p.iconB64)) p.nametag.iconPath = path;
                }
                if (p.nametag.backingEnabled && !string.IsNullOrEmpty(p.backingB64))
                {
                    string path = Path.Combine(icons, Sanitize(p.name) + "_backing.png");
                    if (WriteB64(icons, path, p.backingB64)) p.nametag.backingPath = path;
                }
            }
            p.iconB64 = null;
            p.backingB64 = null;

            if (p.textures.Count > 0)
            {
                string texDir = Path.Combine(Dir, "_tex_" + Sanitize(p.name));
                foreach (var t in p.textures)
                    WriteB64(texDir, Path.Combine(texDir, t.fileName), t.b64);
                p.texDir = texDir;
                p.textures.Clear();
            }
        }

        private static bool WriteB64(string dir, string path, string b64)
        {
            if (string.IsNullOrEmpty(b64)) return false;
            try
            {
                var bytes = Convert.FromBase64String(b64);
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length == bytes.Length) return true;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"unpack {Path.GetFileName(path)}: {ex.Message}"); return false; }
        }

        private static readonly HashSet<string> _socialUnpacked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static List<Social.RemoteSocialDisplay.Entry> UnpackSocials(BfgProfile p)
        {
            if (p.socials == null || p.socials.Count == 0) return null;
            string dir = Path.Combine(Dir, "_social", Sanitize(p.name));
            bool write = !_socialUnpacked.Contains(p.name);
            if (write)
            {
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
                catch (Exception ex) { Plugin.Log.LogWarning("social unpack: " + ex.Message); return null; }
                _socialUnpacked.Add(p.name);
            }

            var list = new List<Social.RemoteSocialDisplay.Entry>();
            for (int i = 0; i < p.socials.Count; i++)
            {
                var s = p.socials[i];
                var entry = new Social.RemoteSocialDisplay.Entry
                {
                    phrase = s.phrase,
                    slot = s.slot,
                    text = s.text ?? "",
                    soundPaths = new string[3]
                };
                if (!string.IsNullOrEmpty(s.imageB64))
                {
                    string path = Path.Combine(dir, i + (string.IsNullOrEmpty(s.imageExt) ? ".png" : s.imageExt));
                    if (write) WriteB64(dir, path, s.imageB64);
                    entry.imagePath = path;
                }
                for (int j = 0; j < 3; j++)
                {
                    if (string.IsNullOrEmpty(s.soundB64[j])) continue;
                    string path = Path.Combine(dir, i + "_" + j + (string.IsNullOrEmpty(s.soundExt[j]) ? ".wav" : s.soundExt[j]));
                    if (write) WriteB64(dir, path, s.soundB64[j]);
                    entry.soundPaths[j] = path;
                }
                list.Add(entry);
            }
            return list;
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
