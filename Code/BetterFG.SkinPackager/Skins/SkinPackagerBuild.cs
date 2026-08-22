using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using EditorMessageType = UnityEditor.MessageType;

namespace BetterFG.Editor
{
    public partial class SkinPackagerWindow
    {
        private const string TEMP_ASSET_DIR = "Assets/_BettrFGPack";

        private bool _packed;

        private void DrawBuildStep()
        {
            Rect area = GUILayoutUtility.GetRect(10f, 60f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            float coverH = area.height;
            float coverW = coverH * COVER_W / COVER_H;
            float maxW = area.width * 0.42f;
            if (coverW > maxW) { coverW = maxW; coverH = coverW * COVER_H / COVER_W; }

            var coverRect = new Rect(area.x, area.y, coverW, coverH);
            EditorGUI.DrawRect(coverRect, new Color(0.1f, 0.1f, 0.11f));
            if (_coverPreview != null) GUI.DrawTexture(coverRect, _coverPreview, ScaleMode.ScaleAndCrop);
            else GUI.Label(coverRect, "No cover image", _small);

            float ix = coverRect.xMax + 16f;
            float iw = Mathf.Max(area.xMax - ix, 60f);
            float y = area.y;

            GUI.Label(new Rect(ix, y, iw, 22f), string.IsNullOrWhiteSpace(_name) ? "Untitled" : _name, _h2);
            y += 24f;
            GUI.Label(new Rect(ix, y, iw, 16f), "by " + (string.IsNullOrWhiteSpace(_author) ? "unknown" : _author), _label);
            y += 20f;

            if (!string.IsNullOrWhiteSpace(_description))
            {
                float dh = Mathf.Min(_wrap.CalcHeight(new GUIContent(_description), iw), Mathf.Max(area.yMax - y - 22f, 0f));
                GUI.Label(new Rect(ix, y, iw, dh), _description, _wrap);
                y += dh + 6f;
            }

            string group = string.IsNullOrWhiteSpace(_group) ? "Unsorted" : _group;
            GUI.Label(new Rect(ix, y, iw, 16f), $"{KIND_LABELS[(int)_kind]}   ·   {group}   ·   {BundleName}", _label);

            string dest = ComputeDestDir();
            GUILayout.Space(12);
            GUILayout.Label(dest, _label);
            GUILayout.Space(8);

            if (_packed && Directory.Exists(_outputDir))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Open folder", _button, GUILayout.Height(34f)))
                    EditorUtility.RevealInFinder(_outputDir);
                if (GUILayout.Button("Pack another", _button, GUILayout.Height(34f)))
                {
                    _packed = false;
                    ResetForNew();
                    GoTo(Step.Mode);
                }
                GUILayout.EndHorizontal();
                return;
            }

            GUI.enabled = (_sourceObject != null || _keepBundle) && !string.IsNullOrWhiteSpace(_name) && !string.IsNullOrEmpty(dest);
            GUI.backgroundColor = new Color(0.35f, 0.62f, 0.38f);
            if (GUILayout.Button("Build and Pack", _bigButton, GUILayout.Height(34f))) TryBuildAndPack();
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
        }

        private static string SanitizeBundleName(string raw)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim().ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.ToString();
        }

        private static string KindFolder(SkinKind k)
        {
            switch (k)
            {
                case SkinKind.Accessory: return "Accessories";
                case SkinKind.Item: return "Items";
                case SkinKind.Plinth: return "Plinths";
                default: return "Costumes";
            }
        }

        private static string KindStr(SkinKind k)
        {
            switch (k)
            {
                case SkinKind.Accessory: return "accessory";
                case SkinKind.Item: return "item";
                case SkinKind.Plinth: return "plinth";
                default: return "costume";
            }
        }

        private string ComputeDestDir()
        {
            if (string.IsNullOrWhiteSpace(_repoRoot) || string.IsNullOrEmpty(BundleName)) return "";
            return Path.Combine(_repoRoot, KindFolder(_kind), BundleName);
        }

        private void TryBuildAndPack()
        {
            _statusMsg = "";
            _packed = false;

            string dest = ComputeDestDir();

            if (_keepBundle)
            {
                try
                {
                    string src = Path.Combine(_loadedDir, _loadedBundleFile);
                    if (!File.Exists(src)) { Err($"the packed bundle went missing ({src})"); return; }

                    Directory.CreateDirectory(dest);
                    _outputDir = dest;

                    string dst = Path.Combine(dest, _loadedBundleFile);
                    if (!string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
                        File.Copy(src, dst, overwrite: true);

                    WriteInfoJson();
                    WriteCover();

                    if (!string.IsNullOrEmpty(_loadedDir) && !string.Equals(_loadedDir, dest, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_loadedDir))
                    {
                        Directory.Delete(_loadedDir, true);
                        Debug.Log($"repacked under a new name, removed the old skin folder {_loadedDir}");
                    }
                    _loadedDir = dest;

                    RunCatalogBat();
                    WriteNewCatalog(_repoRoot);

                    _packed = true;
                    RefreshGroups();
                    Ok($"metadata rewritten for {_name}, bundle untouched");
                    Debug.Log($"repacked {_name} without rebuilding, kept {_loadedBundleFile}");
                }
                catch (Exception ex)
                {
                    Err($"pack failed: {ex.Message}");
                    Debug.LogException(ex);
                }
                return;
            }
            string bundleTempDir = Path.Combine(Path.GetTempPath(), "BettrFGPack_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string prefabPath = TEMP_ASSET_DIR + "/" + _sourceObject.name + ".prefab";
            GameObject clone = null;

            try
            {
                EditorUtility.DisplayProgressBar("Packing skin", "making a throwaway prefab", 0.15f);

                if (!AssetDatabase.IsValidFolder(TEMP_ASSET_DIR))
                    AssetDatabase.CreateFolder("Assets", "_BettrFGPack");

                clone = Instantiate(_sourceObject);
                clone.name = _sourceObject.name;
                var saved = PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
                DestroyImmediate(clone);
                clone = null;

                if (saved == null) { Err("couldn't save the temp prefab, check the console"); return; }

                EditorUtility.DisplayProgressBar("Packing skin", "building the assetbundle", 0.45f);

                Directory.CreateDirectory(bundleTempDir);

                var builds = new[]
                {
                    new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { prefabPath }, addressableNames = new[] { BundleName } }
                };

                var manifest = BuildPipeline.BuildAssetBundles(bundleTempDir, builds, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
                if (manifest == null) { Err("assetbundle build failed, check the console"); return; }

                string built = Path.Combine(bundleTempDir, BundleName);
                if (!File.Exists(built)) { Err($"bundle didn't land where expected ({built})"); return; }

                EditorUtility.DisplayProgressBar("Packing skin", "writing into the repo", 0.75f);

                Directory.CreateDirectory(dest);
                _outputDir = dest;
                File.Copy(built, Path.Combine(dest, BundleName), overwrite: true);

                WriteInfoJson();
                WriteCover();

                if (!string.IsNullOrEmpty(_loadedDir) && !string.Equals(_loadedDir, dest, StringComparison.OrdinalIgnoreCase) && Directory.Exists(_loadedDir))
                {
                    Directory.Delete(_loadedDir, true);
                    Debug.Log($"rebuilt under a new name, removed the old skin folder {_loadedDir}");
                }
                _loadedDir = dest;

                EditorUtility.DisplayProgressBar("Packing skin", "regenerating the catalog", 0.92f);
                RunCatalogBat();
                WriteNewCatalog(_repoRoot);

                _packed = true;
                RefreshGroups();
                Ok($"packed {_name} -> {dest}");
                Debug.Log($"skin packed: {_name} ({KindStr(_kind)}) as bundle {BundleName} into {dest}");
            }
            catch (Exception ex)
            {
                Err($"pack failed: {ex.Message}");
                Debug.LogException(ex);
            }
            finally
            {
                if (clone != null) DestroyImmediate(clone);
                EditorUtility.ClearProgressBar();

                if (AssetDatabase.IsValidFolder(TEMP_ASSET_DIR))
                    AssetDatabase.DeleteAsset(TEMP_ASSET_DIR);
                if (Directory.Exists(bundleTempDir))
                    Directory.Delete(bundleTempDir, true);

                AssetDatabase.Refresh();
            }
        }

        private void RunCatalogBat()
        {
            string bat = Path.Combine(_repoRoot, "generate_catalog.bat");
            if (!File.Exists(bat)) return;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = bat,
                WorkingDirectory = _repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var p = System.Diagnostics.Process.Start(psi))
                p.WaitForExit();
        }

        private static void WriteNewCatalog(string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot)) return;

            var entries = new List<string>();
            foreach (string category in new[] { "Costumes", "Accessories", "Items", "Plinths", "Emotes" })
            {
                string root = Path.Combine(repoRoot, category);
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string info = Path.Combine(dir, "info.json");
                    if (!File.Exists(info)) continue;
                    string body = File.ReadAllText(info).Trim();
                    int open = body.IndexOf('{');
                    int close = body.LastIndexOf('}');
                    if (open < 0 || close <= open) continue;
                    string inner = body.Substring(open + 1, close - open - 1).Trim().TrimEnd(',').Trim();
                    string path = category + "/" + new DirectoryInfo(dir).Name;
                    string pathField = "\"path\": \"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                    string obj = inner.Length == 0 ? "{ " + pathField + " }" : "{ " + pathField + ", " + inner + " }";
                    entries.Add(obj);
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\n');
                sb.Append(entries[i]);
            }
            if (entries.Count > 0) sb.Append('\n');
            sb.Append(']');
            File.WriteAllText(Path.Combine(repoRoot, "catalog2.json"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        private void WriteInfoJson()
        {
            var bones = new List<(string, Vector3)>();
            foreach (var r in _boneRows)
            {
                string boneName = r.bone != null ? r.bone.name : r.boneName;
                Vector3 localPos = r.bone != null ? r.bone.localPosition : r.localPos;
                if (!string.IsNullOrWhiteSpace(boneName))
                    bones.Add((boneName, localPos));
            }

            SkinInfoJson.Write(_outputDir, BundleName, _name, _author, _description, _group, KindStr(_kind),
                keepBase: _keepBase,
                skinScale: Mathf.Approximately(_skinScale, 1f) ? 0f : _skinScale,
                itemScale: _itemScale,
                leftBoneName: _leftTransform != null ? _leftTransform.name : null,
                rightBoneName: _rightTransform != null ? _rightTransform.name : null,
                leftPos: _leftTransform != null ? _leftTransform.localPosition : Vector3.zero,
                leftRot: _leftTransform != null ? _leftTransform.localEulerAngles : Vector3.zero,
                rightPos: _rightTransform != null ? _rightTransform.localPosition : Vector3.zero,
                rightRot: _rightTransform != null ? _rightTransform.localEulerAngles : Vector3.zero,
                boneOffsets: bones);
        }

        private void WriteCover()
        {
            if (string.IsNullOrEmpty(_coverPath) || !File.Exists(_coverPath)) return;

            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(File.ReadAllBytes(_coverPath))) { DestroyImmediate(src); return; }

            float srcAspect = (float)src.width / src.height;
            float dstAspect = (float)COVER_W / COVER_H;

            int cropX, cropY, cropW, cropH;
            if (srcAspect > dstAspect)
            {
                cropH = src.height;
                cropW = Mathf.RoundToInt(src.height * dstAspect);
                cropX = (src.width - cropW) / 2;
                cropY = 0;
            }
            else
            {
                cropW = src.width;
                cropH = Mathf.RoundToInt(src.width / dstAspect);
                cropX = 0;
                cropY = (src.height - cropH) / 2;
            }

            Color[] cropped = src.GetPixels(cropX, cropY, cropW, cropH);
            DestroyImmediate(src);

            var tmp = new Texture2D(cropW, cropH, TextureFormat.RGB24, false);
            tmp.SetPixels(cropped);
            tmp.Apply();

            var rt = RenderTexture.GetTemporary(COVER_W, COVER_H, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(tmp, rt);
            DestroyImmediate(tmp);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var final = new Texture2D(COVER_W, COVER_H, TextureFormat.RGB24, false);
            final.ReadPixels(new Rect(0, 0, COVER_W, COVER_H), 0, 0);
            final.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            File.WriteAllBytes(Path.Combine(_outputDir, "cover.jpg"), final.EncodeToJPG(92));
            DestroyImmediate(final);
        }

        private GameObject ResolvePreviewObject()
        {
            if (_sourceObject != null) return _sourceObject;
            if (!_keepBundle || _bundlePreviewObject != null) return _bundlePreviewObject;

            string path = Path.Combine(_loadedDir, _loadedBundleFile);
            if (!File.Exists(path)) return null;

            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Debug.LogWarning($"couldn't open {_loadedBundleFile} for the preview - another copy of it is probably already loaded");
                return null;
            }

            var objs = bundle.LoadAllAssets<GameObject>();
            if (objs.Length > 0) _bundlePreviewObject = objs[0];
            else Debug.LogWarning($"{_loadedBundleFile} has no GameObject in it, nothing to preview");

            bundle.Unload(false);
            return _bundlePreviewObject;
        }

        private bool LoadSkin(string dir)
        {
            string infoPath = Path.Combine(dir, "info.json");
            if (!File.Exists(infoPath)) { Err("no info.json in that folder"); return false; }

            try
            {
                string json = File.ReadAllText(infoPath);

                _loadedBundleFile = SkinInfoJson.ReadStr(json, "file");
                _loadedDir = dir;
                _keepBundle = !string.IsNullOrEmpty(_loadedBundleFile) && File.Exists(Path.Combine(dir, _loadedBundleFile));
                _sourceObject = null;
                _bundlePreviewObject = null;

                _name = SkinInfoJson.ReadStr(json, "name");
                _author = SkinInfoJson.ReadStr(json, "author");
                _description = SkinInfoJson.ReadStr(json, "description");
                _group = SkinInfoJson.ReadStr(json, "group");

                switch (SkinInfoJson.ReadStr(json, "type"))
                {
                    case "accessory": _kind = SkinKind.Accessory; break;
                    case "item": _kind = SkinKind.Item; break;
                    case "plinth": _kind = SkinKind.Plinth; break;
                    default: _kind = SkinKind.Costume; break;
                }

                if (_kind == SkinKind.Costume)
                {
                    _keepBase = SkinInfoJson.ReadBool(json, "keepBase");
                    float s = SkinInfoJson.ReadFloat(json, "skinScale");
                    _skinScale = s > 0f ? s : 1f;
                    _boneRows = SkinInfoJson.ReadBoneOffsets(json);
                    for (int i = 0; i < _boneRows.Count; i++)
                    {
                        var row = _boneRows[i];
                        row.bone = FindSceneTransform(row.boneName);
                        _boneRows[i] = row;
                    }
                }
                else
                {
                    _boneRows.Clear();
                    _skinScale = 1f;
                }

                if (_kind == SkinKind.Item)
                {
                    _itemScale = SkinInfoJson.ReadFloat(json, "scale", 1f);
                    _leftTransform = FindSceneTransform(SkinInfoJson.ReadItemBoneName(json, "left"));
                    _rightTransform = FindSceneTransform(SkinInfoJson.ReadItemBoneName(json, "right"));
                }
                else
                {
                    _leftTransform = null;
                    _rightTransform = null;
                }

                _outputDir = dir;

                var grandparent = Directory.GetParent(dir)?.Parent;
                if (grandparent != null && File.Exists(Path.Combine(grandparent.FullName, "generate_catalog.bat")))
                {
                    _repoRoot = grandparent.FullName;
                    RefreshGroups();
                }

                string coverPath = Path.Combine(dir, "cover.jpg");
                if (!File.Exists(coverPath)) coverPath = Path.Combine(dir, "cover.png");
                if (File.Exists(coverPath))
                {
                    _coverPath = coverPath;
                    LoadCoverPreview(coverPath);
                }

                Ok($"loaded {_name} from {Path.GetFileName(dir)}");
                return true;
            }
            catch (Exception ex)
            {
                Err($"load failed: {ex.Message}");
                return false;
            }
        }

        private static Transform FindSceneTransform(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName)) return null;

            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !EditorUtility.IsPersistent(all[i]) && string.Equals(all[i].name, boneName, StringComparison.Ordinal))
                    return all[i];

            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && !EditorUtility.IsPersistent(all[i]) && string.Equals(all[i].name, boneName, StringComparison.OrdinalIgnoreCase))
                    return all[i];

            return null;
        }
    }
}
