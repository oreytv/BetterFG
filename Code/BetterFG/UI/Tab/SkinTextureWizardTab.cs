using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tab
{
    public class SkinTextureWizardTab : BetterFGTab
    {
        public SkinTextureWizardTab(IntPtr ptr) : base(ptr) { }

        public int EditIndex = -1;

        public override string TabTitle => EditIndex >= 0 ? "Skin Texture - Edit" : "Skin Texture - New";

        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float SH => UIScale.SH;
        private static float LH => UIScale.LH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color LABEL = new Color(1f, 1f, 1f, 0.72f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color OK = new Color(0.55f, 0.85f, 0.55f, 1f);
        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_GREEN = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color ROW_IDLE = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color ROW_SEL = new Color(0.25f, 0.45f, 0.25f, 1f);

        private static float ROW_H => 24f * UIScale.S;

        private enum Step { Costume, Material, Png, Name }
        private static readonly string[] StepTitles =
        {
            "Choose a skin",
            "Choose the texture to change",
            "Choose the texture to change to",
            "Name it"
        };

        private Step _step = Step.Costume;
        private GameObject _costumeStep, _matStep, _pngStep, _nameStep;
        private Text _stepHeader, _status;
        private Button _backBtn, _nextBtn;

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

        private static Texture2D _bgTex;
        private static Texture2D _hoverTex;
        private GameObject _bgHoverGo;

        private static Texture2D LoadTex(string resource, ref Texture2D cache)
        {
            if (cache != null) return cache;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resource);
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                cache = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError("skin texture wizard: tex load fail: " + ex.Message); }
            return cache;
        }

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.tab.customskintexture.png", ref _bgTex);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = LoadTex("BetterFG.assets.ui.bg_hover.png", ref _hoverTex);
            if (hoverTex != null)
            {
                var hoverGo = new GameObject("BG_Hover");
                hoverGo.transform.SetParent(bgGo.transform, false);
                var hRt = hoverGo.AddComponent<RectTransform>();
                hRt.anchorMin = Vector2.zero;
                hRt.anchorMax = Vector2.one;
                hRt.offsetMin = hRt.offsetMax = Vector2.zero;
                hoverGo.AddComponent<RawImage>().texture = hoverTex;
                hoverGo.SetActive(false);
                _bgHoverGo = hoverGo;
            }
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        private float _tickTimer;
        void Update()
        {
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.1f) return;
            _tickTimer = 0f;
            WinDialogs.Tick();
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;

            _stepHeader = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, VPAD, w, LH), "", FS, LABEL);

            float bodyY = VPAD + LH + SH;
            float bodyH = TabHeight - bodyY - BTN_H - LH - VPAD - SH * 2f;

            _costumeStep = MakePanel(contentRoot, bodyY, bodyH);
            BuildCostumeStep(_costumeStep.GetComponent<RectTransform>(), w, bodyH);
            _matStep = MakePanel(contentRoot, bodyY, bodyH);
            BuildMaterialStep(_matStep.GetComponent<RectTransform>(), w, bodyH);
            _pngStep = MakePanel(contentRoot, bodyY, bodyH);
            BuildPngStep(_pngStep.GetComponent<RectTransform>(), w, bodyH);
            _nameStep = MakePanel(contentRoot, bodyY, bodyH);
            BuildNameStep(_nameStep.GetComponent<RectTransform>(), w, bodyH);

            float navY = bodyY + bodyH + SH;
            float bw = (w - PAD) / 2f;
            _backBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD, navY, bw, BTN_H),
                "< BACK", BTN_DARK, WHITE, FS_SM, new Action(OnBack));
            _nextBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD + bw + PAD * 0.5f, navY, bw, BTN_H),
                "NEXT >", BTN_BLUE, WHITE, FS_SM, new Action(OnNext));

            _status = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, navY + BTN_H + SH, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            if (EditIndex >= 0) LoadEditedEntry();
            RefreshStep();
        }

        private GameObject MakePanel(RectTransform parent, float y, float h)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, new Rect(0f, y, TabWidth, h));
            return go;
        }

        private void LoadEditedEntry()
        {
            var entries = SkinApplicationService.LoadEntries();
            if (EditIndex >= entries.Count) { EditIndex = -1; return; }

            var entry = entries[EditIndex];
            _costumeName = entry.costumeName;
            _matNames.AddRange(entry.matNames);
            _matIdx = entry.matIdx;
            _pngPath = entry.texPath;
            UGUIShip.SetInputText(_nameField, entry.entryName, false);
            LoadPngPreview();
            RebuildMatRows();
            _step = Step.Material;

            var costume = FindCostume(_costumeName);
            if (costume != null) StartCoroutine(CacheCostumeRoutine(costume).WrapToIl2Cpp());
            else SetStatus(_costumeName + " isn't loaded, search for it again to see its textures");
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

        private void OnBack()
        {
            if (_step == Step.Costume) { LeaveToList(); return; }
            _step = (Step)((int)_step - 1);
            RefreshStep();
        }

        private void OnNext()
        {
            if (_step == Step.Name) { Save(); return; }
            _step = (Step)((int)_step + 1);
            RefreshStep();
        }

        private void LeaveToList()
            => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.CreateTab("Skin Texture"));

        private bool CanAdvance()
        {
            switch (_step)
            {
                case Step.Costume: return _matNames.Count > 0;
                case Step.Material: return _matIdx >= 0 && _matIdx < _matNames.Count;
                case Step.Png: return !string.IsNullOrEmpty(_pngPath);
                default: return true;
            }
        }

        private void RefreshStep()
        {
            _costumeStep.SetActive(_step == Step.Costume);
            _matStep.SetActive(_step == Step.Material);
            _pngStep.SetActive(_step == Step.Png);
            _nameStep.SetActive(_step == Step.Name);

            _stepHeader.text = $"Step {(int)_step + 1} of 4  -  {StepTitles[(int)_step]}";

            bool last = _step == Step.Name;
            var nlbl = _nextBtn.GetComponentInChildren<Text>();
            if (nlbl != null) nlbl.text = last ? (EditIndex >= 0 ? "SAVE CHANGES" : "SAVE") : "NEXT >";

            bool can = CanAdvance();
            _nextBtn.interactable = can;
            if (nlbl != null) nlbl.color = can ? WHITE : HINT;

            var blbl = _backBtn.GetComponentInChildren<Text>();
            if (blbl != null) blbl.text = _step == Step.Costume ? "< CANCEL" : "< BACK";

            if (_step == Step.Name) RefreshSummary();
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

        private void RefreshSummary()
        {
            string mat = _matIdx >= 0 && _matIdx < _matNames.Count ? _matNames[_matIdx] : "?";
            _summaryLbl.text = $"skin: {_costumeName}\ntexture: {mat}\npng: {(string.IsNullOrEmpty(_pngPath) ? "?" : Path.GetFileName(_pngPath))}";
        }

        private void Save()
        {
            string name = _nameField.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { SetStatus("give it a name first"); return; }

            var entries = SkinApplicationService.LoadEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i == EditIndex) continue;
                if (entries[i].entryName == name) { SetStatus("you already have one called that"); return; }
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

            LeaveToList();
        }

        private void SetStatus(string msg)
        {
            if (_status != null) _status.text = msg;
        }
    }
}
