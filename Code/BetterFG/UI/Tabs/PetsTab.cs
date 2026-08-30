using System;
using System.Collections.Generic;
using BetterFG.Customization.Pets;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class PetsTab : Tab
    {
        public PetsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Pets";
        protected override string TitleId => "ui.pets";
        protected override string BgResource => "BetterFG.assets.ui.tab.petstab.png";

        static readonly Color BTN_DARK = UGUIShip.BTN_DARK;
        static readonly Color BTN_EQUIP = new Color(0.25f, 0.45f, 0.25f, 1f);
        static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;
        static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        static readonly Color WHITE = UGUIShip.WHITE;

        static float ROW_H => 30f * UIScale.S;

        RectTransform _content;
        RawImage _previewImg;
        Text _previewHint;
        string _previewedPetId;
        string _selectedId;

        struct RowRefs
        {
            public Button Row;
            public Button Equip;
            public Text EquipLbl;
            public Text Name;
            public string Id;
        }
        readonly List<RowRefs> _rows = new List<RowRefs>();

        void Update() { if (IsOpen) PetPreview.Render(); }

        protected override void BuildContent(RectTransform contentRoot)
        {
            BuildAboveTabPreview();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            float listH = TabHeight - y - VPAD;
            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, listH));
            _content = scroll.content;
            var vlg = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 2f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RefreshList();
        }

        public override void OnOpened() => RefreshList();

        public override void OnClosed()
        {
            if (_previewImg != null) _previewImg.transform.parent.gameObject.SetActive(false);
            PetPreview.Invalidate();
            _previewedPetId = null;
        }

        void BuildAboveTabPreview()
        {
            if (Root == null) return;
            _previewImg = PetPreviewPanel.Build(Root, TabWidth, TabHeight, TITLE_H, SH, UIScale.S);
            _previewHint = UGUIShip.CreateLabel(_previewImg.transform.parent, new Rect(0f, 0f, PetPreviewPanel.Width * UIScale.S, PetPreviewPanel.Height * UIScale.S),
                "ui.select_a_pet", FS_SM, HINT, TextAnchor.MiddleCenter);
            _previewImg.transform.parent.gameObject.SetActive(false);
        }

        PetData PreviewPet()
        {
            var pets = PetService.Instance?.Pets;
            if (pets == null) return null;
            if (!string.IsNullOrEmpty(_selectedId))
            {
                var sel = pets.Find(p => p.id == _selectedId);
                if (sel != null) return sel;
            }
            foreach (var p in PetService.Instance.EquippedPets()) return p;
            return null;
        }

        void RefreshPreview()
        {
            var pet = PreviewPet();
            var frame = _previewImg != null ? _previewImg.transform.parent.gameObject : null;

            if (pet == null)
            {
                _previewedPetId = null;
                PetPreview.Invalidate();
                if (_previewImg != null) _previewImg.texture = null;
                if (frame != null) frame.SetActive(false);
                return;
            }

            if (frame != null) frame.SetActive(IsOpen);
            if (_previewHint != null) _previewHint.gameObject.SetActive(false);
            if (pet.id == _previewedPetId) return;
            _previewedPetId = pet.id;
            PetPreview.Rebuild(this, pet);
            if (_previewImg != null) _previewImg.texture = PetPreview.Ensure();
        }

        void RefreshList()
        {
            RefreshPreview();
            if (_content == null) return;

            for (int i = _content.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_content.GetChild(i).gameObject);
            _rows.Clear();

            var pets = PetService.Instance?.Pets ?? new List<PetData>();
            float rowW = TabWidth - PAD * 2f - 8f;

            for (int i = 0; i < pets.Count; i++)
            {
                int idx = i;
                var pet = pets[i];
                string petId = pet.id;

                var rowGo = new GameObject("PetRow_" + i);
                rowGo.transform.SetParent(_content, false);
                rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;
                rowGo.AddComponent<Image>().color = WHITE;

                var rowBtn = rowGo.AddComponent<Button>();
                var nav = rowBtn.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.None;
                rowBtn.navigation = nav;
                rowBtn.onClick.AddListener(new Action(() => { AudioService.PlayButtonClick(); SelectRow(petId); }));

                float thumbSz = (ROW_H - 6f) * 1.4f;
                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(rowBtn.transform, false);
                var thumbRt = thumbGo.AddComponent<RectTransform>();
                UGUIShip.SetPixelRect(thumbRt, new Rect(3f + thumbSz * 0.2f, 3f - thumbSz * 0.2f, thumbSz, thumbSz));
                var raw = thumbGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                var thumb = PetThumb.Load(petId);
                if (thumb != null) raw.texture = thumb;
                else raw.color = new Color(0f, 0f, 0f, 0.4f);

                float editW = 30f * UIScale.S, equipW = 30f * UIScale.S, removeW = 22f * UIScale.S;
                float nameX = 3f + thumbSz * 1.2f + 6f;
                float nameW = rowW - editW - equipW - removeW - nameX - 10f;

                var nameLbl = UGUIShip.CreateLabel(rowBtn.transform,
                    new Rect(nameX, 0f, nameW, ROW_H), pet.name, FS_SM, WHITE, TextAnchor.MiddleLeft);

                UGUIShip.CreateRowEndButton(rowBtn.transform, -(removeW + equipW + editW + 4f), editW, ROW_H,
                    "ui.edit_2", BTN_DARK, () => OpenWizard(idx));

                var equipBtn = UGUIShip.CreateRowEndButton(rowBtn.transform, -(removeW + equipW + 2f), equipW, ROW_H,
                    "ui.on_2", BTN_EQUIP, () => { PetService.Instance?.ToggleEquipped(petId); RefreshList(); });

                UGUIShip.CreateRowEndButton(rowBtn.transform, -2f, removeW, ROW_H, "x", BTN_REMOVE,
                    () => { PetService.Instance?.RemovePet(petId); if (_selectedId == petId) _selectedId = null; RefreshList(); });

                _rows.Add(new RowRefs
                {
                    Row = rowBtn,
                    Equip = equipBtn,
                    EquipLbl = equipBtn.GetComponentInChildren<Text>(),
                    Name = nameLbl,
                    Id = petId,
                });
            }

            PaintRows();

            var addBtn = UGUIShip.CreateButton(_content, new Rect(0f, 0f, rowW, ROW_H),
                "ui.create_pet", BTN_ADD, WHITE, FS, new Action(() => OpenWizard(-1)));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = ROW_H;
            addLe.flexibleWidth = 1f;

            if (pets.Count == 0)
                UGUIShip.CreateLabel(_content, new Rect(6f, 0f, TabWidth, ROW_H),
                    "ui.no_pets_yet_create_one_below", FS_SM, HINT);
        }

        void PaintRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                bool equipped = PetService.Instance?.IsEquipped(row.Id) ?? false;

                UGUIShip.PaintListRow(row.Row, i, row.Id == _selectedId);
                UGUIShip.SetButtonColor(row.Equip, equipped ? BTN_EQUIP : BTN_DARK);
                if (row.EquipLbl != null) UGUIShip.RelabelText(row.EquipLbl, equipped ? "ui.on_2" : "ui.off_2");
                if (row.Name != null) row.Name.color = equipped ? WHITE : DIM;
            }
        }

        void SelectRow(string id)
        {
            _selectedId = _selectedId == id ? null : id;
            PaintRows();
            RefreshPreview();
        }

        void OpenWizard(int editIdx)
        {
            var wizard = BetterFGTabRegistry.NewTab<PetWizardTab>();
            wizard.EditIndex = editIdx;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, wizard);
        }
    }
}
