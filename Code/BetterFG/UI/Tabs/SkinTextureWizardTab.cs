using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tabs
{
    public class SkinTextureWizardTab : WizardTab
    {
        public SkinTextureWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => EditIndex >= 0 ? "Skin Texture - Edit" : "Skin Texture - New";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";

        private static readonly Color OK = new Color(0.55f, 0.85f, 0.55f, 1f);

        private enum WizardStep { Costume, Material, Png, Name }
        protected override string[] StepTitles => new[]
        {
            "Choose a skin",
            "Choose the texture to change",
            "Choose the texture to change to",
            "Name it"
        };

        private InputField _searchField;
        private RectTransform _resultContent;
        private readonly List<CostumeOption> _results = new List<CostumeOption>();
        private readonly List<Button> _resultRows = new List<Button>();
        private int _selectedResult = -1;
        private bool _caching;

        private readonly List<Material> _mats = new List<Material>();
        private readonly List<string> _matNames = new List<string>();
        private string _costumeName = "";

        private RectTransform _matContent;
        private int _matIdx = -1;

        private string _pngPath = "";
        private Texture2D _pngTex;
        private RawImage _pngPreview;
        private Text _pngPathLbl;

        private InputField _nameField;
        private Text _summaryLbl;

        private float _tickTimer;
        void Update()
        {
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.1f) return;
            _tickTimer = 0f;
            WinDialogs.Tick();
        }

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.Costume: BuildCostumeStep(root, w, bodyH); break;
                case WizardStep.Material: BuildMaterialStep(root, w, bodyH); break;
                case WizardStep.Png: BuildPngStep(root, w, bodyH); break;
                case WizardStep.Name: BuildNameStep(root, w, bodyH); break;
            }
        }

        protected override int LoadEditedEntry()
        {
            var entries = SkinApplicationService.LoadEntries();
            if (EditIndex >= entries.Count) { EditIndex = -1; return -1; }

            var entry = entries[EditIndex];
            _costumeName = entry.costumeName;
            _matNames.AddRange(entry.matNames);
            _matIdx = entry.matIdx;
            _pngPath = entry.texPath;
            UGUIShip.SetInputText(_nameField, entry.entryName, false);
            LoadPngPreview();
            RebuildMatRows();

            var costume = FindCostume(_costumeName);
            if (costume != null) StartCoroutine(CacheCostumeRoutine(costume).WrapToIl2Cpp());
            else SetStatus(_costumeName + " isn't loaded, search for it again to see its textures");

            return (int)WizardStep.Material;
        }

        private static CostumeOption FindCostume(string costumeName)
        {
            if (string.IsNullOrEmpty(costumeName)) return null;
            var raw = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<CostumeOption>());
            if (raw == null) return null;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == null) continue;
                CostumeOption opt;
                try { opt = raw[i].Cast<CostumeOption>(); } catch { continue; }
                if (opt.name == costumeName) return opt;
            }
            return null;
        }

        protected override Tab MakeListTarget() => BetterFGTabRegistry.CreateTab("Skin Texture");

        protected override bool CanAdvance(int step)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.Costume: return _matNames.Count > 0;
                case WizardStep.Material: return _matIdx >= 0 && _matIdx < _matNames.Count;
                case WizardStep.Png: return !string.IsNullOrEmpty(_pngPath);
                default: return true;
            }
        }

        private void BuildCostumeStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            float fetchW = 60f * UIScale.S;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "Search for the skin you want to retexture", FS_SM, LABEL);
            cy += LH + SH;

            _searchField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w - fetchW - PAD, BTN_H),
                "search costumes by name", Color.black, WHITE, FS_SM);
            _searchField.onEndEdit.AddListener(new Action<string>(v => OnFetch()));
            UGUIShip.CreateButton(root.transform, new Rect(PAD + w - fetchW, cy, fetchW, BTN_H),
                "SEARCH", BTN_BLUE, WHITE, FS_SM, new Action(OnFetch));
            cy += BTN_H + SH;

            var scroll = UGUIShip.CreateScrollView(root.transform, new Rect(PAD, cy, w, bodyH - cy - SH));
            _resultContent = scroll.content;
            var layout = _resultContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            _resultContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void OnFetch()
        {
            string filter = _searchField.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(filter)) { SetStatus("type a skin name first"); return; }
            StartCoroutine(FetchRoutine(filter).WrapToIl2Cpp());
        }

        private IEnumerator FetchRoutine(string filter)
        {
            _results.Clear();
            _selectedResult = -1;
            RebuildResultRows();
            SetStatus("searching...");

            for (int i = 0; i < 2; i++) yield return null;

            try
            {
                var raw = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<CostumeOption>());
                if (raw == null || raw.Length == 0) { SetStatus("no costumes loaded yet"); yield break; }

                for (int i = 0; i < raw.Length && _results.Count < 60; i++)
                {
                    if (raw[i] == null) continue;
                    CostumeOption opt;
                    try { opt = raw[i].Cast<CostumeOption>(); } catch { continue; }

                    if (GetDisplayName(opt).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        _results.Add(opt);
                }
            }
            catch (Exception ex) { SetStatus("search failed: " + ex.Message); yield break; }

            SetStatus(_results.Count == 0 ? "nothing matched " + filter : _results.Count + " match(es), pick one");
            RebuildResultRows();
        }

        private static string GetDisplayName(CostumeOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "";
        }

        private void RebuildResultRows()
        {
            _resultRows.Clear();
            for (int i = _resultContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_resultContent.GetChild(i).gameObject);

            if (_results.Count == 0 && !string.IsNullOrEmpty(_costumeName))
            {
                UGUIShip.CreateLabel(_resultContent, new Rect(6f, 0f, TabWidth, ROW_H),
                    "using " + _costumeName + " - search to pick a different skin", FS_SM, OK);
                return;
            }

            for (int i = 0; i < _results.Count; i++)
            {
                int idx = i;
                var btn = UGUIShip.CreateButton(_resultContent, new Rect(0f, 0f, 0f, ROW_H), "",
                    ROW_IDLE, WHITE, FS_SM, new Action(() => SelectCostume(idx)));
                btn.transition = Selectable.Transition.None;
                var trigger = btn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (trigger != null) GameObject.Destroy(trigger);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                Sprite icon = null;
                try { icon = ((ItemDefinitionSO)_results[i])?.MenuDisplaySprite; } catch { }
                float iconSz = (ROW_H - 4f) * 1.4f;
                float textX = 5f;
                if (icon != null)
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(btn.transform, false);
                    var iconRt = iconGo.AddComponent<RectTransform>();
                    iconRt.anchorMin = new Vector2(0f, 0.5f);
                    iconRt.anchorMax = new Vector2(0f, 0.5f);
                    iconRt.pivot = new Vector2(0f, 0.5f);
                    iconRt.anchoredPosition = new Vector2(3f + iconSz * 0.2f, iconSz * 0.2f);
                    iconRt.sizeDelta = new Vector2(iconSz, iconSz);
                    var img = iconGo.AddComponent<Image>();
                    img.sprite = icon;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    textX = 3f + iconSz * 1.2f + 4f;
                }

                UGUIShip.CreateLabel(btn.transform, new Rect(textX, 0f, TabWidth - PAD * 2f - textX - 8f, ROW_H),
                    GetDisplayName(_results[i]), FS_SM, WHITE, TextAnchor.MiddleLeft);
                _resultRows.Add(btn);
            }
        }

        private void SelectCostume(int idx)
        {
            if (_caching) return;
            _selectedResult = idx;
            for (int i = 0; i < _resultRows.Count; i++)
            {
                var img = _resultRows[i].GetComponent<Image>();
                if (img != null) img.color = i == idx ? ROW_SEL : ROW_IDLE;
            }
            StartCoroutine(CacheCostumeRoutine(_results[idx]).WrapToIl2Cpp());
        }

        private IEnumerator CacheCostumeRoutine(CostumeOption option)
        {
            _caching = true;
            SetStatus("loading " + GetDisplayName(option) + "...");

            GameObject instance = null;
            bool done = false;
            Exception err = null;

            try
            {
                var op = option.costumePrefabReference.InstantiateAsync();
                StartCoroutine(WaitForAsyncOp(op,
                    r => { instance = r; done = true; },
                    e => { err = e; done = true; }).WrapToIl2Cpp());
            }
            catch (Exception e) { _caching = false; SetStatus("couldn't load that skin: " + e.Message); yield break; }

            float elapsed = 0f;
            while (!done && elapsed < 8f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _caching = false;

            if (!done || instance == null)
            {
                SetStatus(err != null ? "couldn't load that skin: " + err.Message : $"gave up after {elapsed:0.0}s");
                yield break;
            }

            string keepMat = _matIdx >= 0 && _matIdx < _matNames.Count ? _matNames[_matIdx] : null;

            instance.SetActive(false);
            _mats.Clear();
            _matNames.Clear();
            CollectMatsRecursive(instance.transform, _mats, _matNames);
            GameObject.Destroy(instance);

            try { _costumeName = option.name ?? ""; } catch { _costumeName = ""; }
            _matIdx = keepMat != null ? _matNames.IndexOf(keepMat) : -1;
            RebuildMatRows();
            RefreshStep();

            SetStatus($"{_costumeName} has {_matNames.Count} texture(s), hit next");
        }

        private static void CollectMatsRecursive(Transform t, List<Material> mats, List<string> names)
        {
            var r = t.GetComponent<Renderer>();
            if (r != null)
            {
                var sharedMats = r.sharedMaterials;
                if (sharedMats != null)
                {
                    foreach (var m in sharedMats)
                    {
                        if (m == null) continue;
                        var mainTex = m.mainTexture;
                        string texName = mainTex != null ? mainTex.name : m.name;
                        if (!string.IsNullOrEmpty(texName) && !names.Contains(texName))
                        {
                            mats.Add(m);
                            names.Add(texName);
                        }
                    }
                }
            }
            for (int i = 0; i < t.childCount; i++)
                CollectMatsRecursive(t.GetChild(i), mats, names);
        }

        private IEnumerator WaitForAsyncOp(
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> op,
            Action<GameObject> onDone, Action<Exception> onFail)
        {
            yield return op;
            try
            {
                if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                    onDone?.Invoke(op.Result);
                else
                    onFail?.Invoke(new Exception("op failed: " + op.OperationException?.Message));
            }
            catch (Exception e) { onFail?.Invoke(e); }
        }

        private void BuildMaterialStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "Pick the texture on the skin you want to replace", FS_SM, LABEL);
            cy += LH + SH;

            float listH = bodyH - cy - BTN_H - SH * 2f;
            var scroll = UGUIShip.CreateScrollView(root.transform, new Rect(PAD, cy, w, listH));
            _matContent = scroll.content;
            var layout = _matContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            _matContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            cy += listH + SH;

            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, w, BTN_H),
                "Download the selected texture as a PNG", BTN_DARK, WHITE, FS_SM, new Action(OnDownloadSelected));
        }

        private void RebuildMatRows()
        {
            if (_matContent == null) return;
            for (int i = _matContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_matContent.GetChild(i).gameObject);

            if (_matNames.Count == 0)
            {
                UGUIShip.CreateLabel(_matContent, new Rect(6f, 0f, TabWidth, ROW_H),
                    "go back and pick a skin first", FS_SM, HINT);
                return;
            }

            for (int i = 0; i < _matNames.Count; i++)
            {
                int idx = i;
                var btn = UGUIShip.CreateButton(_matContent, new Rect(0f, 0f, 0f, ROW_H), "",
                    idx == _matIdx ? ROW_SEL : ROW_IDLE, WHITE, FS_SM, new Action(() => SelectMat(idx)));
                btn.transition = Selectable.Transition.None;
                var trigger = btn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (trigger != null) GameObject.Destroy(trigger);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(btn.transform, false);
                var thumbRt = thumbGo.AddComponent<RectTransform>();
                UGUIShip.SetPixelRect(thumbRt, new Rect(3f, 2f, ROW_H - 4f, ROW_H - 4f));
                var raw = thumbGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                var tex = SkinApplicationService.ResolveSourceTexture(_mats, idx, _matNames[idx]);
                if (tex != null) raw.texture = tex;
                else raw.color = new Color(0f, 0f, 0f, 0.4f);

                float textX = ROW_H + 3f;
                UGUIShip.CreateLabel(btn.transform, new Rect(textX, 0f, TabWidth - PAD * 2f - textX - 8f, ROW_H),
                    _matNames[idx], FS_SM, WHITE, TextAnchor.MiddleLeft);
            }
        }

        private void SelectMat(int idx)
        {
            _matIdx = idx;
            RebuildMatRows();
            RefreshStep();
            SetStatus(_matNames[idx] + " selected");
        }

        private void OnDownloadSelected()
        {
            if (_matIdx < 0 || _matIdx >= _matNames.Count) { SetStatus("pick a texture first"); return; }

            var src = SkinApplicationService.ResolveSourceTexture(_mats, _matIdx, _matNames[_matIdx]);
            if (src == null) { SetStatus("that texture isn't loaded right now"); return; }

            string name = string.IsNullOrEmpty(src.name) ? "skin_texture" : src.name;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');

            WinDialogs.SaveFile("Save Skin Texture", "png", name + ".png", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                if (SkinApplicationService.SaveTexturePng(src, path, out string err))
                    SetStatus("saved " + Path.GetFileName(path));
                else
                    SetStatus("save failed: " + err);
            });
        }

        private void BuildPngStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "Pick the PNG to use instead", FS_SM, LABEL);
            cy += LH + SH;

            float browseW = 80f * UIScale.S;
            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, browseW, BTN_H),
                "BROWSE", BTN_BLUE, WHITE, FS_SM, new Action(OnBrowsePng));
            _pngPathLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD + browseW + PAD, cy, w - browseW - PAD, BTN_H),
                "no file picked", FS_SM, HINT, TextAnchor.MiddleLeft);
            cy += BTN_H + SH * 2f;

            float previewSz = Mathf.Min(bodyH - cy - SH, w);
            var frameGo = new GameObject("PngPreview");
            frameGo.transform.SetParent(root.transform, false);
            var frameRt = frameGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(frameRt, new Rect(PAD + (w - previewSz) * 0.5f, cy, previewSz, previewSz));
            frameGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);

            var rawGo = new GameObject("Raw");
            rawGo.transform.SetParent(frameGo.transform, false);
            var rawRt = rawGo.AddComponent<RectTransform>();
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = rawRt.offsetMax = Vector2.zero;
            _pngPreview = rawGo.AddComponent<RawImage>();
            _pngPreview.raycastTarget = false;
            _pngPreview.color = Color.clear;
        }

        private void OnBrowsePng()
        {
            WinDialogs.PickPng("Select PNG Texture", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                _pngPath = path;
                LoadPngPreview();
                RefreshStep();
                SetStatus(Path.GetFileName(path) + " ready");
            });
        }

        private void LoadPngPreview()
        {
            if (_pngPathLbl != null)
                _pngPathLbl.text = string.IsNullOrEmpty(_pngPath) ? "no file picked" : Path.GetFileName(_pngPath);
            if (string.IsNullOrEmpty(_pngPath) || !File.Exists(_pngPath)) return;

            try
            {
                if (_pngTex == null) _pngTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _pngTex.LoadImage(File.ReadAllBytes(_pngPath));
                _pngTex.Apply();
                if (_pngPreview != null)
                {
                    _pngPreview.texture = _pngTex;
                    _pngPreview.color = WHITE;
                }
            }
            catch (Exception e) { SetStatus("couldn't read that png: " + e.Message); }
        }

        private void BuildNameStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "What should this override be called?", FS_SM, LABEL);
            cy += LH + SH;

            _nameField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w, BTN_H),
                "my texture override", Color.black, WHITE, FS_SM);
            cy += BTN_H + SH * 2f;

            _summaryLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH * 4f), "", FS_SM, HINT);
            _summaryLbl.alignment = TextAnchor.UpperLeft;
        }

        protected override void RefreshSummary()
        {
            string mat = _matIdx >= 0 && _matIdx < _matNames.Count ? _matNames[_matIdx] : "?";
            _summaryLbl.text = $"skin: {_costumeName}\ntexture: {mat}\npng: {(string.IsNullOrEmpty(_pngPath) ? "?" : Path.GetFileName(_pngPath))}";
        }

        protected override bool Save()
        {
            string name = _nameField.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { SetStatus("give it a name first"); return false; }

            var entries = SkinApplicationService.LoadEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i == EditIndex) continue;
                if (entries[i].entryName == name) { SetStatus("you already have one called that"); return false; }
            }

            bool editing = EditIndex >= 0 && EditIndex < entries.Count;
            var entry = editing ? entries[EditIndex] : new SkinTexEntry { enabled = true };

            entry.entryName = name;
            entry.texPath = _pngPath;
            entry.matIdx = _matIdx;
            entry.costumeName = _costumeName;
            entry.matNames.Clear();
            entry.matNames.AddRange(_matNames);
            entry.mats.Clear();
            entry.mats.AddRange(_mats);

            if (!editing) entries.Add(entry);

            SkinApplicationService.SaveEntries(entries);
            SkinApplicationService.ReapplyAllEnabled(entries, null);
            Plugin.Log.LogInfo($"skin texture {(editing ? "updated" : "added")}: {name} -> {entry.costumeName}/{(_matIdx >= 0 && _matIdx < _matNames.Count ? _matNames[_matIdx] : "?")}");
            return true;
        }
    }
}
