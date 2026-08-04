using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;

namespace BetterFG.Customization.Player
{
    // one user-created texture override entry
    public class SkinTexEntry
    {
        public string entryName;
        public string texPath;
        public byte[] texData;
        public int matIdx;
        public bool enabled;
        public string costumeName;

        public List<Material> mats = new List<Material>();
        public List<string> matNames = new List<string>();
    }

    public partial class SkinApplicationService
    {
        // keyed by bean instance id — original materials per renderer before we touched them
        private Dictionary<int, List<CustomTexOriginal>> customTexOriginals = new Dictionary<int, List<CustomTexOriginal>>();
        private HashSet<int> customTexPollingBeans = new HashSet<int>();
        // beans we've already made our one real custom-tex attempt on (their GEO was fully built).
        // the game fires BindMeshToFallguy once per mesh, constantly (idle anim / LOD), and every
        // fire re-arms the reapply poll. without this, a bean whose costume DOESN'T contain the
        // target material never lands in customTexOriginals, so it re-polls forever -> the steady
        // background freeze after a texture is applied. cleared on revert so a settings/costume
        // change re-attempts cleanly.
        private HashSet<int> customTexAttemptedBeans = new HashSet<int>();
        private static readonly string[] customTexProps = { "_MainTex", "_BaseMap", "_BaseTexture", "_MainTex2" };

        private struct CustomTexOriginal
        {
            public Renderer renderer;
            public int matIdx;
            public string prop;
            public Texture texture;
            public string textureName;
        }

        // process-wide cache of decoded custom textures. each bean push used to re-read the file
        // and re-decode the PNG/JPG on the main thread, which stacks freezes during state changes
        // (round load, qual, reward — each pushes the local bean and OnBeansFound iterates every
        // saved entry). keyed on path + write timestamp so editing the file invalidates.
        private static readonly Dictionary<string, (long stamp, Texture2D tex)> _customTexCache =
            new Dictionary<string, (long, Texture2D)>(StringComparer.OrdinalIgnoreCase);

        const string KEY_ENTRY_COUNT = "skintex.entryCount";
        private static string EK(int i, string f) => $"skintex.entry.{i}.{f}";

        public static int EntryCount
            => int.TryParse(SettingsService.Get(KEY_ENTRY_COUNT, "0"), out int c) ? c : 0;

        public static List<SkinTexEntry> LoadEntries()
        {
            var entries = new List<SkinTexEntry>();
            int count = EntryCount;
            for (int i = 0; i < count; i++)
            {
                var e = new SkinTexEntry
                {
                    entryName = SettingsService.Get(EK(i, "name"), "entry " + i),
                    texPath = SettingsService.Get(EK(i, "texPath"), ""),
                    matIdx = 0,
                    enabled = SettingsService.Get(EK(i, "enabled"), "1") == "1",
                    costumeName = SettingsService.Get(EK(i, "costume"), "")
                };
                if (int.TryParse(SettingsService.Get(EK(i, "matIdx"), "0"), out int mi))
                    e.matIdx = mi;

                // matNames come back pipe-joined so match building works without recaching the costume
                foreach (var n in SettingsService.Get(EK(i, "matNames"), "").Split('|'))
                    if (!string.IsNullOrEmpty(n)) e.matNames.Add(n);

                entries.Add(e);
            }
            return entries;
        }

        public static void SaveEntries(List<SkinTexEntry> entries)
        {
            SettingsService.Set(KEY_ENTRY_COUNT, entries.Count.ToString());
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                SettingsService.Set(EK(i, "name"), e.entryName);
                SettingsService.Set(EK(i, "texPath"), e.texPath);
                SettingsService.Set(EK(i, "matIdx"), e.matIdx.ToString());
                SettingsService.Set(EK(i, "enabled"), e.enabled ? "1" : "0");
                SettingsService.Set(EK(i, "costume"), e.costumeName);
                SettingsService.Set(EK(i, "matNames"), string.Join("|", e.matNames));
            }
        }

        // decode every enabled entry's texture up front (plugin load) so the first per-bean
        // auto-reapply is a cache hit instead of a file read + png decode on that frame
        public static void PrewarmCustomTexCache()
        {
            foreach (var entry in LoadEntries())
                if (entry.enabled) GetCachedCustomTex(entry);
        }

        public static Texture2D GetCachedCustomTex(SkinTexEntry entry)
        {
            if (entry.texData != null && entry.texData.Length > 0)
                return DecodeCustomTex("replay:" + entry.entryName, entry.texData.Length, entry.texData);
            return GetCachedCustomTex(entry.texPath);
        }

        private static Texture2D GetCachedCustomTex(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            long stamp;
            try { stamp = System.IO.File.GetLastWriteTimeUtc(path).Ticks; } catch { return null; }
            if (_customTexCache.TryGetValue(path, out var hit) && hit.stamp == stamp && hit.tex != null)
                return hit.tex;
            byte[] data;
            try { data = System.IO.File.ReadAllBytes(path); }
            catch (Exception ex) { Plugin.Log.LogWarning($"read {path}: {ex.Message}"); return null; }
            return DecodeCustomTex(path, stamp, data);
        }

        private static Texture2D DecodeCustomTex(string key, long stamp, byte[] data)
        {
            if (_customTexCache.TryGetValue(key, out var hit) && hit.stamp == stamp && hit.tex != null)
                return hit.tex;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _customTexCache[key] = (stamp, tex);
            return tex;
        }

        public static Texture GetMaterialTexture(Material mat)
        {
            if (mat == null) return null;
            foreach (var prop in customTexProps)
            {
                if (!mat.HasProperty(prop)) continue;
                var t = mat.GetTexture(prop);
                if (t != null) return t;
            }
            return null;
        }

        public static Texture ResolveSourceTexture(List<Material> mats, int idx, string matName)
        {
            if (mats != null && idx >= 0 && idx < mats.Count)
            {
                var t = GetMaterialTexture(mats[idx]);
                if (t != null) return t;
            }

            if (Instance == null || string.IsNullOrEmpty(matName)) return null;
            var bean = BeanMonitorService.LocalPlayerBean;
            if (bean == null) return null;

            if (Instance.customTexOriginals.TryGetValue(bean.GetInstanceID(), out var originals))
                foreach (var o in originals)
                    if (o.textureName == matName) return o.texture;

            var geo = FindBeanGEO(bean);
            if (geo == null) return null;
            foreach (var r in geo.GetComponentsInChildren<Renderer>(true))
            {
                var shared = r.sharedMaterials;
                if (shared == null) continue;
                foreach (var m in shared)
                {
                    var t = GetMaterialTexture(m);
                    if (t == null) continue;
                    if (t.name == matName || CleanMatName(m.name) == matName) return t;
                }
            }
            return null;
        }

        public static bool SaveTexturePng(Texture src, string path, out string error)
        {
            error = null;
            if (src == null) { error = "no source texture"; return false; }

            int w = Mathf.Max(1, src.width);
            int h = Mathf.Max(1, src.height);
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                readable.Apply();
                System.IO.File.WriteAllBytes(path, readable.EncodeToPNG());
                Plugin.Log.LogInfo($"dumped {src.name} ({w}x{h}) to {path}");
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }

        public static HashSet<string> BuildMatchNames(SkinTexEntry entry)
        {
            var matchNames = new HashSet<string>();
            if (entry.matNames.Count > 0 && entry.matIdx >= 0 && entry.matIdx < entry.matNames.Count)
            {
                var name = entry.matNames[entry.matIdx];
                if (!string.IsNullOrEmpty(name)) matchNames.Add(name);
            }

            if (matchNames.Count > 0) return matchNames;

            if (entry.mats.Count > 0 && entry.matIdx >= 0 && entry.matIdx < entry.mats.Count)
            {
                var mat = entry.mats[entry.matIdx];
                if (mat != null)
                {
                    if (!string.IsNullOrEmpty(mat.name)) matchNames.Add(CleanMatName(mat.name));
                    foreach (var prop in customTexProps)
                    {
                        if (!mat.HasProperty(prop)) continue;
                        var t = mat.GetTexture(prop);
                        if (t != null && !string.IsNullOrEmpty(t.name)) { matchNames.Add(t.name); break; }
                    }
                }
            }
            return matchNames;
        }

        public static List<GameObject> GatherBeans()
        {
            var beans = new List<GameObject>();
            if (BeanMonitorService.LocalPlayerBean != null)
                beans.Add(BeanMonitorService.LocalPlayerBean);
            foreach (var b in BeanMonitorService.GetTrackedBeans())
                if (b != null && !beans.Contains(b)) beans.Add(b);
            return beans;
        }

        public static int ApplyEntryToBean(SkinTexEntry entry, GameObject bean)
        {
            if (Instance == null || bean == null) return 0;
            var tex = GetCachedCustomTex(entry);
            if (tex == null) return 0;
            return Instance.ApplyCustomTexture(bean, entry.matIdx, tex, BuildMatchNames(entry));
        }

        public static void ApplyEntriesToBean(List<SkinTexEntry> entries, GameObject bean)
        {
            foreach (var entry in entries)
                if (entry.enabled) ApplyEntryToBean(entry, bean);
        }

        public static int ApplyEntry(SkinTexEntry entry)
        {
            int total = 0;
            foreach (var bean in GatherBeans())
                total += ApplyEntryToBean(entry, bean);
            return total;
        }

        public static void RevertAllBeans()
        {
            if (Instance == null) return;
            foreach (var bean in GatherBeans())
                Instance.RevertCustomTexture(bean);
        }

        public static void ReapplyAllEnabledFromSettings()
            => ReapplyAllEnabled(LoadEntries(), null);

        // wipe everything first, then put back whatever is still switched on
        public static void ReapplyAllEnabled(List<SkinTexEntry> entries, Action<string> status)
        {
            if (Instance == null) return;
            RevertAllBeans();

            foreach (var entry in entries)
            {
                if (!entry.enabled) continue;
                int n = ApplyEntry(entry);
                status?.Invoke(n > 0 ? $"applied {entry.entryName}" : "nothing matched");
            }
        }

        public int TryAutoReapplyCustomTextureForBean(GameObject bean)
        {
            if (bean == null) return 0;
            int beanId = bean.GetInstanceID();
            if (customTexOriginals.ContainsKey(beanId)) return 0;

            int total = 0;
            foreach (var entry in LoadEntries())
            {
                if (!entry.enabled) continue;

                // no match name = skip, don't blast everything
                var matchNames = BuildMatchNames(entry);
                if (matchNames.Count == 0) continue;

                var tex = GetCachedCustomTex(entry);
                if (tex != null) total += ApplyCustomTexture(bean, entry.matIdx, tex, matchNames);
            }
            return total;
        }

        public void PollAndReapplyCustomTextureForBean(GameObject bean)
        {
            if (bean == null) return;
            // nothing saved to reapply -> don't spin a poll coroutine that scans the bean's renderers
            // every 0.5s for nothing. the game rebinds costume meshes constantly (animation/LOD) and
            // every rebind re-arms this via the BindMeshToFallguy postfix, so without this gate a
            // game-cosmetics-only loadout still pays a steady background poll.
            if (EntryCount <= 0) return;
            int beanId = bean.GetInstanceID();
            if (customTexOriginals.ContainsKey(beanId) || customTexPollingBeans.Contains(beanId) || customTexAttemptedBeans.Contains(beanId)) return;
            customTexPollingBeans.Add(beanId);
            StartCoroutine(PollReapplyCoroutine(bean).WrapToIl2Cpp());
        }

        private IEnumerator PollReapplyCoroutine(GameObject bean)
        {
            float elapsed = 0f;
            int beanId = bean != null ? bean.GetInstanceID() : 0;
            while (elapsed < 10f)
            {
                if (bean == null) break;
                if (customTexOriginals.ContainsKey(beanId)) break;

                var geo = FindBeanGEO(bean);
                if (geo != null && geo.GetComponentsInChildren<Renderer>(true).Length > 0)
                {
                    // bean is fully built — this is our one real attempt. whether or not a texture
                    // matched, a material that isn't on this bean won't appear by polling longer.
                    // mark attempted so the constant BindMeshToFallguy postfix doesn't re-arm us.
                    customTexAttemptedBeans.Add(beanId);
                    TryAutoReapplyCustomTextureForBean(bean);
                    break;
                }
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            if (bean != null && !customTexOriginals.ContainsKey(beanId))
                Plugin.Log.LogWarning($"PollReapply no texture matched for {bean.name}");
            if (beanId != 0)
                customTexPollingBeans.Remove(beanId);
        }

        private void RevertLocalCustomTextures()
        {
            var local = BeanMonitorService.LocalPlayerBean;
            if (local != null) RevertCustomTexture(local);

            foreach (var bean in BeanMonitorService.GetTrackedBeans())
            {
                if (bean == null || bean == local) continue;
                if (IsRemoteInRoundBean(bean)) continue;
                RevertCustomTexture(bean);
            }
        }

        // scans bean GEO so normal costumes, custom skins, and additive cosmetics all count
        public int ApplyCustomTexture(GameObject bean, int matSlotIdx, Texture2D tex, HashSet<string> matchTexNames)
        {
            if (bean == null || tex == null) return 0;

            var geo = FindBeanGEO(bean);
            if (geo == null) return 0;

            int texSlot = 0;
            return ApplyTextureToGameObject(geo.gameObject, matSlotIdx, tex, matchTexNames, bean.GetInstanceID(), ref texSlot);
        }

        private int ApplyTextureToGameObject(GameObject costumeObj, int matSlotIdx, Texture2D tex, HashSet<string> matchTexNames, int beanId, ref int texSlot)
        {
            int count = 0;
            var renderers = costumeObj.GetComponentsInChildren<Renderer>(true);
            bool alreadyHadOriginals = customTexOriginals.TryGetValue(beanId, out var originalList);
            if (originalList == null)
                originalList = new List<CustomTexOriginal>();

            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.materials;
                if (mats == null) continue;
                bool touched = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;

                    bool hadTextureSlot = false;

                    foreach (var prop in customTexProps)
                    {
                        try
                        {
                            if (!m.HasProperty(prop)) continue;

                            var originalTex = FindCustomTexOriginal(originalList, r, i, prop, out string savedName);
                            if (originalTex == null)
                                originalTex = m.GetTexture(prop);
                            if (originalTex == null) continue;

                            hadTextureSlot = true;
                            string originalTexName = savedName ?? originalTex.name ?? "";
                            string matName = CleanMatName(m.name);
                            bool hasNameFilter = matchTexNames != null && matchTexNames.Count > 0;
                            bool nameMatch = hasNameFilter && (matchTexNames.Contains(originalTexName) || matchTexNames.Contains(matName));
                            if (hasNameFilter ? !nameMatch : texSlot != matSlotIdx) continue;

                            RememberCustomTexOriginal(originalList, r, i, prop, originalTex, originalTexName);

                            if (!string.IsNullOrEmpty(originalTexName))
                                tex.name = originalTexName;
                            m.SetTexture(prop, tex);
                            touched = true;
                            break;
                        }
                        catch { }
                    }

                    if (hadTextureSlot) texSlot++;
                }

                if (touched)
                {
                    r.materials = mats;
                    count++;
                }
            }
            if (count > 0 && !alreadyHadOriginals)
                customTexOriginals[beanId] = originalList;
            return count;
        }

        private static string CleanMatName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.EndsWith(" (Instance)") ? name.Substring(0, name.Length - 11) : name;
        }

        private static Texture FindCustomTexOriginal(List<CustomTexOriginal> originals, Renderer renderer, int matIdx, string prop, out string textureName)
        {
            textureName = null;
            if (originals == null) return null;

            foreach (var o in originals)
                if (o.renderer == renderer && o.matIdx == matIdx && o.prop == prop)
                {
                    textureName = o.textureName;
                    return o.texture;
                }

            return null;
        }

        private static void RememberCustomTexOriginal(List<CustomTexOriginal> originals, Renderer renderer, int matIdx, string prop, Texture texture, string textureName)
        {
            if (originals == null || renderer == null || texture == null) return;

            foreach (var o in originals)
                if (o.renderer == renderer && o.matIdx == matIdx && o.prop == prop) return;

            originals.Add(new CustomTexOriginal
            {
                renderer = renderer,
                matIdx = matIdx,
                prop = prop,
                texture = texture,
                textureName = textureName
            });
        }

        public void RevertCustomTexture(GameObject bean)
        {
            if (bean == null) return;
            int beanId = bean.GetInstanceID();
            // always re-enable a fresh attempt next time the bean rebinds — even for beans that
            // never matched (those aren't in customTexOriginals but DID get marked attempted)
            customTexAttemptedBeans.Remove(beanId);
            if (!customTexOriginals.TryGetValue(beanId, out var originals)) return;

            foreach (var o in originals)
            {
                var r = o.renderer;
                if (r == null) continue;
                try
                {
                    var mats = r.materials;
                    if (mats == null || o.matIdx < 0 || o.matIdx >= mats.Length) continue;
                    var m = mats[o.matIdx];
                    if (m == null || !m.HasProperty(o.prop)) continue;
                    m.SetTexture(o.prop, o.texture);
                    r.materials = mats;
                }
                catch { }
            }

            customTexOriginals.Remove(beanId);
        }
    }
}
