using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Pets;
using BetterFG.Customization.Player;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tabs
{
    // picks a pet's Upper/Lower/Pattern/Faceplate look - the exact same category-carousel + search +
    // fetch + icon-list mechanic as SkinTextureWizardTab's "Choose a skin" step, just writing into a
    // pet snapshot's four fields (one at a time, switch category to fill the others) instead of one
    // skin-tex entry.
    public class PetLookPickerTab : SwitchTab
    {
        public PetLookPickerTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Choose Pet Look";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";
        protected override string SwitchLabel => "< Back";

        public PetData Snapshot;
        public int EditIndexCarry = -1;

        static readonly Color WHITE = Color.white;
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.4f);
        static readonly Color OK = new Color(0.55f, 0.85f, 0.55f, 1f);
        static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        static readonly Color ROW_IDLE = new Color(0.12f, 0.12f, 0.12f, 1f);

        const string Upper = "upper", Lower = "lower", Pattern = "pattern", Faceplate = "faceplate", Colour = "colour";
        static readonly (string id, string label)[] CATEGORIES =
        {
            (Upper, "Upper"), (Lower, "Lower"), (Pattern, "Pattern"), (Faceplate, "Faceplate"), (Colour, "Colour"),
        };

        string _category = Upper;
        Text _categoryLbl;
        Text _currentLbl;
        Text _clearLbl;
        Text _statusLbl;
        InputField _searchField;
        RectTransform _resultContent;
        readonly List<ItemDefinitionSO> _results = new List<ItemDefinitionSO>();

        RawImage _previewImg;

        // same shape as PetWizardTab's own above-tab preview, so the pet stays visible while
        // you're picking its look here too, not just back on the wizard
        void Update() { if (IsOpen) PetPreview.Render(); }

        void BuildAboveTabPreview()
        {
            if (Root == null) return;
            _previewImg = PetPreviewPanel.Build(Root, TabWidth, TabHeight, TITLE_H, SH, UIScale.S);
            _previewImg.transform.parent.gameObject.SetActive(false);
        }

        void RefreshPreview()
        {
            PetPreview.Rebuild(this, Snapshot);
            if (_previewImg != null) _previewImg.texture = PetPreview.Ensure();
        }

        // the preview frame lives outside _contentArea (see PetPreviewPanel), so closing the tab
        // doesn't hide it on its own
        public override void OnOpened()
        {
            base.OnOpened();
            if (_previewImg != null) _previewImg.transform.parent.gameObject.SetActive(true);
        }

        public override void OnClosed()
        {
            base.OnClosed();
            if (_previewImg != null) _previewImg.transform.parent.gameObject.SetActive(false);
            PetPreview.Invalidate();
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            BuildAboveTabPreview();
            RefreshPreview();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            float clearH = BTN_H, catH = BTN_H;

            var clearBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, w, clearH),
                ClearText(), BTN_REMOVE, WHITE, FS_SM, new Action(ClearCurrent));
            _clearLbl = clearBtn.GetComponentInChildren<Text>();
            y += clearH + SH;

            _currentLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), CurrentText(), FS_SM, OK);
            y += LH + SH;

            float fetchW = 60f * UIScale.S;
            _searchField = UGUIShip.CreateInputField(contentRoot, new Rect(PAD, y, w - fetchW - PAD, BTN_H),
                "search by name", Color.black, WHITE, FS_SM);
            _searchField.onEndEdit.AddListener(new Action<string>(v => OnFetch()));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - fetchW, y, fetchW, BTN_H),
                "SEARCH", BTN_BLUE, WHITE, FS_SM, new Action(OnFetch));
            y += BTN_H + SH;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "search to see options", FS_SM, HINT);
            y += LH + SH;

            float catY = TabHeight - VPAD - catH;
            float listH = catY - SH - y;
            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, listH));
            _resultContent = scroll.content;
            var layout = _resultContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            _resultContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _categoryLbl = UGUIShip.CreateCarousel(contentRoot, new Rect(PAD, catY, w, catH),
                CategoryLabels(), CategoryIndex(_category),
                d => SelectCategory(CATEGORIES[(CategoryIndex(_category) + d + CATEGORIES.Length) % CATEGORIES.Length].id),
                BTN_DARK, (int)FS_SM);

            OnFetch();
        }

        string[] CategoryLabels()
        {
            var names = new string[CATEGORIES.Length];
            for (int i = 0; i < CATEGORIES.Length; i++) names[i] = CATEGORIES[i].label;
            return names;
        }

        static int CategoryIndex(string id)
        {
            for (int i = 0; i < CATEGORIES.Length; i++)
                if (CATEGORIES[i].id == id) return i;
            return 0;
        }

        string ClearText() => "Clear " + CATEGORIES[CategoryIndex(_category)].label;

        void SelectCategory(string cat)
        {
            _category = cat;
            _results.Clear();
            RebuildResultRows();
            if (_categoryLbl != null) _categoryLbl.text = CATEGORIES[CategoryIndex(_category)].label;
            if (_clearLbl != null) _clearLbl.text = ClearText();
            if (_currentLbl != null) _currentLbl.text = CurrentText();
            OnFetch();
        }

        string CurrentText()
        {
            string val = _category == Upper ? Snapshot.costumeTop
                : _category == Lower ? Snapshot.costumeBottom
                : _category == Pattern ? Snapshot.pattern
                : _category == Colour ? Snapshot.colour
                : Snapshot.faceplate;
            return "current: " + (string.IsNullOrEmpty(val) ? "None" : SkinApplicationService.ResolveOptionDisplayName(val));
        }

        void ClearCurrent()
        {
            SetCurrent("");
            if (_currentLbl != null) _currentLbl.text = CurrentText();
        }

        void SetCurrent(string name)
        {
            switch (_category)
            {
                case Upper: Snapshot.costumeTop = name; break;
                case Lower: Snapshot.costumeBottom = name; break;
                case Pattern: Snapshot.pattern = name; break;
                case Faceplate: Snapshot.faceplate = name; break;
                case Colour: Snapshot.colour = name; break;
            }
            RefreshPreview();
        }

        static Il2CppSystem.Type TypeFor(string category)
        {
            switch (category)
            {
                case Upper:
                case Lower: return Il2CppType.Of<CostumeOption>();
                case Pattern: return Il2CppType.Of<SkinPatternOption>();
                case Faceplate: return Il2CppType.Of<FaceplateOption>();
                case Colour: return Il2CppType.Of<ColourOption>();
            }
            return null;
        }

        void OnFetch()
        {
            string filter = _searchField != null ? (_searchField.text?.Trim() ?? "") : "";
            StartCoroutine(FetchRoutine(filter).WrapToIl2Cpp());
        }

        IEnumerator FetchRoutine(string filter)
        {
            _results.Clear();
            RebuildResultRows();
            SetStatus("searching...");

            for (int i = 0; i < 2; i++) yield return null;

            var type = TypeFor(_category);
            if (type == null) yield break;

            Il2CppReferenceArray<UnityEngine.Object> raw = null;
            try { raw = Resources.FindObjectsOfTypeAll(type); }
            catch (Exception ex) { SetStatus("search failed: " + ex.Message); yield break; }
            if (raw == null || raw.Length == 0) { SetStatus("no " + _category + " loaded yet - open the game's own costume screen once, then try again"); yield break; }

            for (int i = 0; i < raw.Length && _results.Count < 80; i++)
            {
                if (raw[i] == null) continue;
                ItemDefinitionSO opt;
                try { opt = raw[i].Cast<ItemDefinitionSO>(); } catch { continue; }
                if (opt == null) continue;

                if (_category == Upper || _category == Lower)
                {
                    CostumeOption co = null;
                    try { co = opt.TryCast<CostumeOption>(); } catch { }
                    if (co == null) continue;
                    bool wantTop = _category == Upper;
                    if (wantTop && co.CostumeType == CostumeType.Bottom) continue;
                    if (!wantTop && co.CostumeType == CostumeType.Top) continue;
                }

                // team colours render uncoloured (white) on a teamless pet - hide them from the list
                if (_category == Colour)
                {
                    ColourOption co = null;
                    try { co = opt.TryCast<ColourOption>(); } catch { }
                    if (co == null || co.TryCast<TeamColourOption>() != null) continue;
                }

                if (string.IsNullOrEmpty(filter) || SkinApplicationService.GetOptionDisplayName(opt).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    _results.Add(opt);
            }

            SetStatus(_results.Count == 0 ? "nothing matched" : _results.Count + " match(es), pick one");
            RebuildResultRows();
        }

        void RebuildResultRows()
        {
            if (_resultContent == null) return;
            for (int i = _resultContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_resultContent.GetChild(i).gameObject);

            for (int i = 0; i < _results.Count; i++)
            {
                var opt = _results[i];
                var btn = UGUIShip.CreateButton(_resultContent, new Rect(0f, 0f, 0f, ROW_H), "",
                    ROW_IDLE, WHITE, FS_SM, new Action(() => Pick(opt)));
                btn.transition = Selectable.Transition.None;
                var trigger = btn.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (trigger != null) GameObject.Destroy(trigger);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                Texture2D iconTex = SkinApplicationService.ResolveOptionIconTexture(_category, opt.name);
                float iconSz = (ROW_H - 6f) * 1.4f;
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
                    SkinApplicationService.GetOptionDisplayName(opt), FS_SM, WHITE, TextAnchor.MiddleLeft);
            }
        }

        static float ROW_H => 30f * UIScale.S;

        void Pick(ItemDefinitionSO opt)
        {
            SetCurrent(opt.name ?? "");
            if (_currentLbl != null) _currentLbl.text = CurrentText();
            SetStatus(SkinApplicationService.GetOptionDisplayName(opt) + " set as " + CATEGORIES[CategoryIndex(_category)].label);
        }

        void SetStatus(string msg) { if (_statusLbl != null) _statusLbl.text = msg; }

        protected override Tab MakeSwitchTarget() => BuildWizard();
        public override Tab MakeFallbackTab() => BuildWizard();

        Tab BuildWizard()
        {
            var wizard = BetterFGTabRegistry.NewTab<PetWizardTab>();
            wizard.EditIndex = EditIndexCarry;
            wizard.ResumeFromLook = this;
            return wizard;
        }
    }
}
