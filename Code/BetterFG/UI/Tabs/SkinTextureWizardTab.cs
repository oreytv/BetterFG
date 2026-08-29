using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public partial class SkinTextureWizardTab : WizardTab
    {
        public SkinTextureWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => EditIndex >= 0 ? "Skin Texture - Edit" : "Skin Texture - New";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";

        // null = the shared global catalog (SkinApplicationService); non-null = some other owner's
        // own list (a pet's PetData.skinTexEntries) - lets this exact wizard be reused for both
        // without a parallel copy, same shape CustomizationTab.PetPickTarget already uses
        public List<SkinTexEntry> TargetEntries;
        // a factory, not a live Tab - SwitchSlotTab destroys `this`, so any tab instance handed in
        // now would be dead by the time MakeListTarget calls it later; build fresh, same shape
        // CustomizationTab.PetPickTarget already uses
        public Func<Tab> OwnerListTab;

        private static readonly Color OK = new Color(0.55f, 0.85f, 0.55f, 1f);
        private static readonly Color CHECK = new Color(0.55f, 0.9f, 0.55f, 1f);

        private enum WizardStep { Costume, Material, Png, PropsPrompt, Name }
        protected override string[] StepTitles => new[]
        {
            "Choose a skin",
            "Choose the texture to change",
            "Choose the texture to change to",
            "Material properties (optional)",
            "Name it"
        };

        private static readonly (string id, string label)[] CATEGORIES = new[]
        {
            (SkinTexCategory.Upper, "Upper"),
            (SkinTexCategory.Lower, "Lower"),
            (SkinTexCategory.Pattern, "Pattern"),
            (SkinTexCategory.Colour, "Colour"),
            (SkinTexCategory.Faceplate, "Faceplate"),
        };

        private string _category = SkinTexCategory.Upper;
        private Text _categoryLbl;

        private InputField _searchField;
        private RectTransform _resultContent;
        private readonly List<ItemDefinitionSO> _results = new List<ItemDefinitionSO>();
        private readonly List<Button> _resultRows = new List<Button>();
        private int _selectedResult = -1;
        private bool _caching;

        private readonly List<Material> _mats = new List<Material>();
        private readonly List<string> _matNames = new List<string>();
        private string _costumeName = "";

        private RectTransform _matContent;
        private int _matIdx = -1;
        private readonly Dictionary<string, string> _overridePaths = new Dictionary<string, string>();

        private string _pngPath = "";
        private Texture2D _pngTex;
        private RawImage _pngPreview;
        private Text _pngPathLbl;

        private readonly Dictionary<string, MatPropOverride> _matProps = new Dictionary<string, MatPropOverride>();

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
                case WizardStep.PropsPrompt: BuildPropsPromptStep(root, w, bodyH); break;
                case WizardStep.Name: BuildNameStep(root, w, bodyH); break;
            }
        }

        List<SkinTexEntry> LoadTargetEntries() => TargetEntries ?? SkinApplicationService.LoadEntries();
        void SaveTargetEntries(List<SkinTexEntry> entries)
        {
            if (TargetEntries != null) return; // caller (e.g. PetSkinTextureTab) owns saving its own list
            SkinApplicationService.SaveEntries(entries);
        }

        protected override int LoadEditedEntry()
        {
            var entries = LoadTargetEntries();
            if (EditIndex >= entries.Count) { EditIndex = -1; return -1; }

            var entry = entries[EditIndex];
            _category = string.IsNullOrEmpty(entry.category) ? SkinTexCategory.Upper : entry.category;
            _costumeName = entry.costumeName;
            _matNames.AddRange(entry.matNames);
            _overridePaths.Clear();
            foreach (var ov in entry.overrides)
            {
                if (string.IsNullOrEmpty(ov.texName)) continue;
                if (!string.IsNullOrEmpty(ov.texPath)) _overridePaths[ov.texName] = ov.texPath;
            }
            _matProps.Clear();
            foreach (var po in entry.matProps)
                _matProps[po.matName + "|" + po.prop] = po;
            UGUIShip.SetInputText(_nameField, entry.entryName, false);
            RefreshCategoryUi();
            RebuildMatRows();

            if (SkinTexCategory.IsOptionField(_category))
                return (int)WizardStep.PropsPrompt;

            var option = FindOption(_category, _costumeName);
            if (option != null) StartCoroutine(CacheOptionRoutine(option).WrapToIl2Cpp());
            else SetStatus(_costumeName + " isn't loaded, search for it again to see its textures");

            return (int)WizardStep.Material;
        }

        private static ItemDefinitionSO FindOption(string category, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = TypeFor(category);
            if (t == null) return null;
            var raw = Resources.FindObjectsOfTypeAll(t);
            if (raw == null) return null;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == null) continue;
                ItemDefinitionSO opt;
                try { opt = raw[i].Cast<ItemDefinitionSO>(); } catch { continue; }
                if (opt != null && opt.name == name) return opt;
            }
            return null;
        }

        private static Il2CppSystem.Type TypeFor(string category)
        {
            switch (category)
            {
                case SkinTexCategory.Upper:
                case SkinTexCategory.Lower: return Il2CppType.Of<CostumeOption>();
                case SkinTexCategory.Pattern: return Il2CppType.Of<SkinPatternOption>();
                case SkinTexCategory.Colour: return Il2CppType.Of<ColourOption>();
                case SkinTexCategory.Faceplate: return Il2CppType.Of<FaceplateOption>();
            }
            return null;
        }

        protected override Tab MakeListTarget() => OwnerListTab != null ? OwnerListTab() : BetterFGTabRegistry.CreateTab("Skin Texture");

        public SkinTextureMaterialPropsTab ResumeSource;
        protected override bool SkipLoadEditedEntry => ResumeSource != null;

        protected override void ResumeWip()
        {
            var src = ResumeSource;
            if (src == null) return;
            ResumeSource = null;

            EditIndex = src.EditIndex;
            TargetEntries = src.TargetEntries;
            OwnerListTab = src.OwnerListTab;
            _category = string.IsNullOrEmpty(src.Category) ? SkinTexCategory.Upper : src.Category;
            _costumeName = src.CostumeName;
            _mats.Clear(); _mats.AddRange(src.Mats);
            _matNames.Clear(); _matNames.AddRange(src.MatNames);
            _overridePaths.Clear();
            foreach (var kv in src.OverridePaths) _overridePaths[kv.Key] = kv.Value;
            _matProps.Clear();
            foreach (var kv in src.MatProps) _matProps[kv.Key] = kv.Value;
            _matIdx = src.MatIdx;
            if (!string.IsNullOrEmpty(src.EntryName)) UGUIShip.SetInputText(_nameField, src.EntryName, false);

            RefreshCategoryUi();
            RebuildMatRows();
            if (_matIdx >= 0 && _matIdx < _matNames.Count)
            {
                _pngPath = _overridePaths.TryGetValue(_matNames[_matIdx], out var existing) ? existing : "";
                LoadPngPreview();
            }
            Step = (int)WizardStep.PropsPrompt;
        }

        protected override bool CanAdvance(int step)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.Costume:
                    if (SkinTexCategory.IsOptionField(_category))
                        return !string.IsNullOrEmpty(_costumeName);
                    return _matNames.Count > 0;
                default: return true;
            }
        }

        protected override int NextStepFrom(int step)
        {
            if (SkinTexCategory.IsOptionField(_category))
            {
                if (step == (int)WizardStep.Costume) return (int)WizardStep.PropsPrompt;
                if (step == (int)WizardStep.Material || step == (int)WizardStep.Png) return (int)WizardStep.PropsPrompt;
            }
            return step + 1;
        }

        protected override int PrevStepFrom(int step)
        {
            if (SkinTexCategory.IsOptionField(_category))
            {
                if (step == (int)WizardStep.PropsPrompt) return (int)WizardStep.Costume;
                if (step == (int)WizardStep.Name) return (int)WizardStep.PropsPrompt;
            }
            return step - 1;
        }

        private void BuildCostumeStep(RectTransform root, float w, float bodyH)
        {
            float cy = SH;

            float catH = BTN_H;
            _categoryLbl = UGUIShip.CreateCarousel(root.transform, new Rect(PAD, cy, w, catH),
                CategoryLabels(), CategoryIndex(_category),
                d => SelectCategory(CATEGORIES[(CategoryIndex(_category) + d + CATEGORIES.Length) % CATEGORIES.Length].id),
                BTN_DARK, FS_SM);
            cy += catH + SH;

            float fetchW = 60f * UIScale.S;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                "Search for the item you want to change", FS_SM, LABEL);
            cy += LH + SH;

            _searchField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w - fetchW - PAD, BTN_H),
                "search by name", Color.black, WHITE, FS_SM);
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

        private void SelectCategory(string cat)
        {
            if (_category == cat) return;
            _category = cat;
            _results.Clear();
            _selectedResult = -1;
            _mats.Clear();
            _matNames.Clear();
            _costumeName = "";
            _matIdx = -1;
            _pngPath = "";
            RefreshCategoryUi();
            RebuildResultRows();
            RebuildMatRows();
            RefreshStep();
            SetStatus("category: " + cat);
        }

        private static string[] CategoryLabels()
        {
            var names = new string[CATEGORIES.Length];
            for (int i = 0; i < CATEGORIES.Length; i++) names[i] = CATEGORIES[i].label;
            return names;
        }

        private static int CategoryIndex(string id)
        {
            for (int i = 0; i < CATEGORIES.Length; i++)
                if (CATEGORIES[i].id == id) return i;
            return 0;
        }

        private void RefreshCategoryUi()
        {
            if (_categoryLbl != null)
                _categoryLbl.text = CATEGORIES[CategoryIndex(_category)].label;
        }

        private void OnFetch()
        {
            string filter = _searchField.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(filter)) { SetStatus("type a name first"); return; }
            StartCoroutine(FetchRoutine(filter).WrapToIl2Cpp());
        }

        private IEnumerator FetchRoutine(string filter)
        {
            _results.Clear();
            _selectedResult = -1;
            RebuildResultRows();
            SetStatus("searching...");

            for (int i = 0; i < 2; i++) yield return null;

            var type = TypeFor(_category);
            if (type == null) { SetStatus("unknown category"); yield break; }

            Il2CppReferenceArray<UnityEngine.Object> raw = null;
            try { raw = Resources.FindObjectsOfTypeAll(type); }
            catch (Exception ex) { SetStatus("search failed: " + ex.Message); yield break; }
            if (raw == null || raw.Length == 0) { SetStatus("no " + _category + " loaded yet"); yield break; }

            for (int i = 0; i < raw.Length && _results.Count < 80; i++)
            {
                if (raw[i] == null) continue;
                ItemDefinitionSO opt;
                try { opt = raw[i].Cast<ItemDefinitionSO>(); } catch { continue; }
                if (opt == null) continue;

                if (_category == SkinTexCategory.Upper || _category == SkinTexCategory.Lower)
                {
                    CostumeOption co = null;
                    try { co = opt.TryCast<CostumeOption>(); } catch { }
                    if (co == null) continue;
                    var t = co.CostumeType;
                    bool wantTop = _category == SkinTexCategory.Upper;
                    if (wantTop && t == CostumeType.Bottom) continue;
                    if (!wantTop && t == CostumeType.Top) continue;
                }

                if (SkinApplicationService.GetOptionDisplayName(opt).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _results.Add(opt);
            }

            SetStatus(_results.Count == 0 ? "nothing matched " + filter : _results.Count + " match(es), pick one");
            RebuildResultRows();
        }


        private void RebuildResultRows()
        {
            _resultRows.Clear();
            for (int i = _resultContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_resultContent.GetChild(i).gameObject);

            if (_results.Count == 0 && !string.IsNullOrEmpty(_costumeName))
            {
                UGUIShip.CreateLabel(_resultContent, new Rect(6f, 0f, TabWidth, ROW_H),
                    "using " + _costumeName + " - search to pick a different one", FS_SM, OK);
                return;
            }

            for (int i = 0; i < _results.Count; i++)
            {
                int idx = i;
                var btn = UGUIShip.CreateButton(_resultContent, new Rect(0f, 0f, 0f, ROW_H), "",
                    ROW_IDLE, WHITE, FS_SM, new Action(() => SelectOption(idx)));
                btn.transition = Selectable.Transition.None;
                var trigger = btn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (trigger != null) GameObject.Destroy(trigger);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                Texture2D iconTex = SkinApplicationService.ResolveOptionIconTexture(_category, _results[i].name);
                float iconSz = (ROW_H - 4f) * 1.4f;
                float textX = 5f;
                if (iconTex != null)
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(btn.transform, false);
                    var iconRt = iconGo.AddComponent<RectTransform>();
                    iconRt.anchorMin = new Vector2(0f, 0.5f);
                    iconRt.anchorMax = new Vector2(0f, 0.5f);
                    iconRt.pivot = new Vector2(0f, 0.5f);
                    iconRt.anchoredPosition = new Vector2(3f + iconSz * 0.2f, iconSz * 0.2f);
                    iconRt.sizeDelta = new Vector2(iconSz, iconSz);
                    var raw = iconGo.AddComponent<RawImage>();
                    raw.texture = iconTex;
                    raw.raycastTarget = false;
                    textX = 3f + iconSz * 1.2f + 4f;
                }

                UGUIShip.CreateLabel(btn.transform, new Rect(textX, 0f, TabWidth - PAD * 2f - textX - 8f, ROW_H),
                    SkinApplicationService.GetOptionDisplayName(_results[i]), FS_SM, WHITE, TextAnchor.MiddleLeft);
                _resultRows.Add(btn);
            }
        }

        private void SelectOption(int idx)
        {
            if (_caching) return;
            _selectedResult = idx;
            for (int i = 0; i < _resultRows.Count; i++)
            {
                var img = _resultRows[i].GetComponent<Image>();
                if (img != null) img.color = i == idx ? ROW_SEL : ROW_IDLE;
            }
            var opt = _results[idx];
            if (SkinTexCategory.IsOptionField(_category))
            {
                _costumeName = opt.name ?? "";
                _mats.Clear();
                _matNames.Clear();
                SetStatus(SkinApplicationService.GetOptionDisplayName(opt) + " picked - hit next for its options");
                RefreshStep();
            }
            else
            {
                StartCoroutine(CacheOptionRoutine(opt).WrapToIl2Cpp());
            }
        }

        private IEnumerator CacheOptionRoutine(ItemDefinitionSO option)
        {
            _caching = true;
            SetStatus("loading " + SkinApplicationService.GetOptionDisplayName(option) + "...");

            _mats.Clear();
            _matNames.Clear();

            if (_category == SkinTexCategory.Pattern)
            {
                SkinPatternOption sp = null;
                try { sp = option.TryCast<SkinPatternOption>(); } catch { }
                if (sp == null) { _caching = false; SetStatus("that's not a pattern"); yield break; }
                try { sp.LoadBlocking(); } catch { }
                yield return null;

                Texture tex = null;
                try { tex = sp.PatternTexture; } catch { }
                string tn = tex != null ? tex.name : sp.name;
                _matNames.Add(string.IsNullOrEmpty(tn) ? sp.name : tn);
                _mats.Add(null);

                _caching = false;
                try { _costumeName = sp.name ?? ""; } catch { _costumeName = ""; }
                RebuildMatRows();
                RefreshStep();
                SetStatus($"{_costumeName} pattern ready");
                yield break;
            }

            GameObject instance = null;
            bool done = false;
            Exception err = null;

            try
            {
                CostumeOption co = null;
                try { co = option.TryCast<CostumeOption>(); } catch { }
                if (co == null) { _caching = false; SetStatus("that's not a costume"); yield break; }
                var op = co.costumePrefabReference.InstantiateAsync();
                StartCoroutine(WaitForAsyncOp(op,
                    r => { instance = r; done = true; },
                    e => { err = e; done = true; }).WrapToIl2Cpp());
            }
            catch (Exception e) { _caching = false; SetStatus("couldn't load: " + e.Message); yield break; }

            float elapsed = 0f;
            while (!done && elapsed < 8f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _caching = false;

            if (!done || instance == null)
            {
                SetStatus(err != null ? "couldn't load: " + err.Message : $"gave up after {elapsed:0.0}s");
                yield break;
            }

            instance.SetActive(false);
            CollectMatsRecursive(instance.transform, _mats, _matNames);
            GameObject.Destroy(instance);

            try { _costumeName = option.name ?? ""; } catch { _costumeName = ""; }
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

                        bool any = false;
                        foreach (var prop in SkinApplicationService.GetTextureProps(m))
                        {
                            var tex = m.GetTexture(prop);
                            if (tex == null || string.IsNullOrEmpty(tex.name) || names.Contains(tex.name)) continue;
                            mats.Add(m);
                            names.Add(tex.name);
                            any = true;
                        }
                        if (!any && !string.IsNullOrEmpty(m.name) && !names.Contains(m.name))
                        {
                            mats.Add(m);
                            names.Add(m.name);
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

            if (SkinTexCategory.IsOptionField(_category))
            {
                UGUIShip.CreateLabel(_matContent, new Rect(6f, 0f, TabWidth, ROW_H),
                    _category + " has no textures - skip ahead to material properties", FS_SM, HINT);
                return;
            }

            if (_matNames.Count == 0)
            {
                UGUIShip.CreateLabel(_matContent, new Rect(6f, 0f, TabWidth, ROW_H),
                    "go back and pick something first", FS_SM, HINT);
                return;
            }

            float rowW = TabWidth - PAD * 2f - 8f;
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

                bool set = _overridePaths.ContainsKey(_matNames[idx]);
                float checkW = 18f * UIScale.S;
                float textX = ROW_H + 3f;
                UGUIShip.CreateLabel(btn.transform, new Rect(textX, 0f, rowW - textX - checkW, ROW_H),
                    _matNames[idx], FS_SM, WHITE, TextAnchor.MiddleLeft);

                var checkLbl = UGUIShip.CreateLabel(btn.transform, new Rect(0f, 0f, checkW, ROW_H),
                    set ? "✓" : "", FS_SM, CHECK, TextAnchor.MiddleCenter);
                var checkRt = checkLbl.GetComponent<RectTransform>();
                checkRt.anchorMin = new Vector2(1f, 0.5f);
                checkRt.anchorMax = new Vector2(1f, 0.5f);
                checkRt.pivot = new Vector2(1f, 0.5f);
                checkRt.anchoredPosition = new Vector2(-4f, 0f);
                checkRt.sizeDelta = new Vector2(checkW, ROW_H);
            }
        }

        private void SelectMat(int idx)
        {
            _matIdx = idx;
            _pngPath = _overridePaths.TryGetValue(_matNames[idx], out var existing) ? existing : "";
            LoadPngPreview();
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
            cy += BTN_H + SH;

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

        private void BuildPropsPromptStep(RectTransform root, float w, float bodyH)
        {
            float cy = bodyH * 0.5f - (LH + SH + BTN_H) * 0.5f;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH),
                SkinTexCategory.IsOptionField(_category)
                    ? "Open the property editor for this " + _category
                    : "Want to tweak this skin's material properties too?",
                FS_SM, LABEL, TextAnchor.MiddleCenter);
            cy += LH + SH;

            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, w, BTN_H),
                SkinTexCategory.IsOptionField(_category) ? "Edit properties >" : "Tweak properties >",
                BTN_BLUE, WHITE, FS_SM, new Action(OpenProps));
        }

        private void OpenProps()
        {
            if (SkinTexCategory.IsOptionField(_category))
            {
                if (string.IsNullOrEmpty(_costumeName)) { SetStatus("go back and pick a " + _category + " first"); return; }
            }
            else if (_matNames.Count == 0) { SetStatus("go back and pick a skin first"); return; }

            var props = BetterFGTabRegistry.NewTab<SkinTextureMaterialPropsTab>();
            props.EditIndex = EditIndex;
            props.TargetEntries = TargetEntries;
            props.OwnerListTab = OwnerListTab;
            props.Category = _category;
            props.CostumeName = _costumeName;
            props.Mats.AddRange(_mats);
            props.MatNames.AddRange(_matNames);
            foreach (var kv in _overridePaths) props.OverridePaths[kv.Key] = kv.Value;
            foreach (var kv in _matProps) props.MatProps[kv.Key] = kv.Value;
            props.MatIdx = _matIdx;
            props.EntryName = _nameField != null ? _nameField.text : "";

            BetterFGUIMan.Instance?.SwitchSlotTab(this, props);
        }

        private void OnBrowsePng()
        {
            WinDialogs.PickPng("Select PNG Texture", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                _pngPath = path;
                if (_matIdx >= 0 && _matIdx < _matNames.Count) _overridePaths[_matNames[_matIdx]] = path;
                LoadPngPreview();
                RefreshStep();
                SetStatus(Path.GetFileName(path) + " ready");
            });
        }

        private void LoadPngPreview()
        {
            if (_pngPathLbl != null)
                _pngPathLbl.text = string.IsNullOrEmpty(_pngPath) ? "no file picked" : Path.GetFileName(_pngPath);
            if (_pngPreview == null) return;
            if (string.IsNullOrEmpty(_pngPath) || !File.Exists(_pngPath))
            {
                _pngPreview.texture = null;
                _pngPreview.color = Color.clear;
                return;
            }

            try
            {
                if (_pngTex == null) _pngTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _pngTex.LoadImage(File.ReadAllBytes(_pngPath));
                _pngTex.Apply();
                _pngPreview.texture = _pngTex;
                _pngPreview.color = WHITE;
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
                "my override", Color.black, WHITE, FS_SM);
            cy += BTN_H + SH * 2f;

            _summaryLbl = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, LH * 4f), "", FS_SM, HINT);
            _summaryLbl.alignment = TextAnchor.UpperLeft;
        }

        protected override void RefreshSummary()
        {
            string slots = _overridePaths.Count == 0
                ? "?"
                : string.Join(", ", _overridePaths.Keys);
            _summaryLbl.text = $"category: {_category}\nitem: {_costumeName}\ntextures changed: {_overridePaths.Count}\n{slots}\nmaterial properties changed: {_matProps.Count}";
        }

        protected override bool Save()
        {
            string name = _nameField.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { SetStatus("give it a name first"); return false; }

            var entries = LoadTargetEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i == EditIndex) continue;
                if (entries[i].entryName == name) { SetStatus("you already have one called that"); return false; }
            }

            bool editing = EditIndex >= 0 && EditIndex < entries.Count;
            var entry = editing ? entries[EditIndex] : new SkinTexEntry { enabled = true };

            entry.entryName = name;
            entry.category = _category;
            entry.costumeName = _costumeName;
            entry.matNames.Clear();
            entry.matNames.AddRange(_matNames);
            entry.overrides.Clear();
            foreach (var kv in _overridePaths)
                entry.overrides.Add(new SkinTexOverride { texName = kv.Key, texPath = kv.Value });
            entry.matProps.Clear();
            entry.matProps.AddRange(_matProps.Values);

            if (!editing) entries.Add(entry);

            SaveTargetEntries(entries);
            Plugin.Log.LogInfo($"skin {_category} {(editing ? "updated" : "added")}: {name} -> {entry.costumeName} ({entry.overrides.Count} texture(s), {entry.matProps.Count} propertie(s))");
            return true;
        }

        // reapplying the global catalog only makes sense for the global catalog - a pet's own
        // entries apply whenever it's (re)spawned (PetBeanBuilder), not against the local player
        protected override void OnLeave()
        {
            if (TargetEntries == null) SkinApplicationService.ReapplyAllEnabledFromSettings();
        }
    }
}
