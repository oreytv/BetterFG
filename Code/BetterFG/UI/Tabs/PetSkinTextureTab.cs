using System;
using System.Collections.Generic;
using BetterFG.Customization.Pets;
using BetterFG.Customization.Player;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tabs
{
    // pet-scoped clone of CustomSkinTextureTab's list UI - reads/writes the pet's OWN
    // PetData.skinTexEntries, never the local player's global SkinApplicationService catalog.
    // no "Apply Selected"/"Revert All" here: a pet's textures apply automatically whenever it's
    // (re)spawned (PetBeanBuilder), there's no live local-player bean to preview onto.
    public class PetSkinTextureTab : SwitchTab
    {
        public PetSkinTextureTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Pet Skin Textures";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";
        protected override string SwitchLabel => "< Back";

        public PetData Snapshot;
        public int EditIndexCarry = -1;

        static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        static readonly Color BTN_APPLY = new Color(0.25f, 0.45f, 0.25f, 1f);
        static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        static readonly Color WHITE = Color.white;

        static float ROW_H => 30f * UIScale.S;

        RawImage _previewImg;
        RectTransform _entryContent;

        void Update() { if (IsOpen) PetPreview.Render(); }

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
            _previewImg = PetPreviewPanel.Build(Root, TabWidth, TabHeight, TITLE_H, SH, UIScale.S);
            _previewImg.transform.parent.gameObject.SetActive(false);
            PetPreview.Rebuild(this, Snapshot);
            _previewImg.texture = PetPreview.Ensure();

            float w = TabWidth - PAD * 2f;
            float y = VPAD + TITLE_H;

            float listH = TabHeight - y - BTN_H - VPAD - 4f;
            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, listH));
            _entryContent = scroll.content;
            var vlg = _entryContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 2f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            _entryContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RefreshEntryList();
        }

        void RefreshEntryList()
        {
            if (_entryContent == null) return;
            for (int i = _entryContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_entryContent.GetChild(i).gameObject);

            float rowW = TabWidth - PAD * 2f - 8f;
            var entries = Snapshot.skinTexEntries;

            if (entries.Count == 0)
                UGUIShip.CreateLabel(_entryContent, new Rect(6f, 0f, TabWidth, ROW_H),
                    "no textures on this pet yet", FS_SM, HINT);

            for (int i = 0; i < entries.Count; i++)
            {
                int idx = i;
                var entry = entries[i];

                var rowGo = new GameObject("ERow_" + i);
                rowGo.transform.SetParent(_entryContent, false);
                rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;
                rowGo.AddComponent<Image>().color = entry.enabled ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(0.08f, 0.08f, 0.08f, 1f);

                float thumbSz = (ROW_H - 6f) * 1.4f;
                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(rowGo.transform, false);
                var thumbRt = thumbGo.AddComponent<RectTransform>();
                UGUIShip.SetPixelRect(thumbRt, new Rect(3f + thumbSz * 0.2f, 3f - thumbSz * 0.2f, thumbSz, thumbSz));
                var raw = thumbGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                Texture thumb = SkinApplicationService.ResolveOptionIconTexture(entry.category, entry.costumeName);
                if (thumb == null) thumb = SkinApplicationService.GetCachedCustomTex(entry);
                if (thumb != null) raw.texture = thumb;
                else raw.color = new Color(0f, 0f, 0f, 0.4f);

                float editW = 30f * UIScale.S, toggleW = 30f * UIScale.S, removeW = 22f * UIScale.S;
                float nameX = 3f + thumbSz * 1.2f + 6f;
                float nameW = rowW - editW - toggleW - removeW - nameX - 10f;

                UGUIShip.CreateLabel(rowGo.transform, new Rect(nameX, 0f, nameW, ROW_H), entry.entryName,
                    FS_SM, entry.enabled ? WHITE : DIM, TextAnchor.MiddleLeft);

                UGUIShip.CreateRowEndButton(rowGo.transform, -(removeW + toggleW + editW + 4f), editW, ROW_H,
                    "edit", BTN_DARK, () => OpenWizard(idx));

                UGUIShip.CreateRowEndButton(rowGo.transform, -(removeW + toggleW + 2f), toggleW, ROW_H,
                    entry.enabled ? "on" : "off", entry.enabled ? BTN_APPLY : BTN_DARK, () => ToggleEntry(idx));

                UGUIShip.CreateRowEndButton(rowGo.transform, -2f, removeW, ROW_H, "x", BTN_REMOVE, () => RemoveEntry(idx));
            }

            var addBtn = UGUIShip.CreateButton(_entryContent, new Rect(0f, 0f, TabWidth - PAD * 2f - 8f, ROW_H),
                "+ Add Texture", BTN_ADD, WHITE, FS, new Action(() => OpenWizard(-1)));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = ROW_H;
            addLe.flexibleWidth = 1f;
        }

        void OpenWizard(int editIdx)
        {
            var wizard = BetterFGTabRegistry.NewTab<SkinTextureWizardTab>();
            wizard.EditIndex = editIdx;
            wizard.TargetEntries = Snapshot.skinTexEntries;
            wizard.OwnerListTab = BuildSelf;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, wizard);
        }

        Tab BuildSelf()
        {
            var tab = BetterFGTabRegistry.NewTab<PetSkinTextureTab>();
            tab.Snapshot = Snapshot;
            tab.EditIndexCarry = EditIndexCarry;
            PetService.Instance?.SavePet(Snapshot);
            return tab;
        }

        void ToggleEntry(int idx)
        {
            Snapshot.skinTexEntries[idx].enabled = !Snapshot.skinTexEntries[idx].enabled;
            PetService.Instance?.SavePet(Snapshot);
            RefreshEntryList();
        }

        void RemoveEntry(int idx)
        {
            Snapshot.skinTexEntries.RemoveAt(idx);
            PetService.Instance?.SavePet(Snapshot);
            RefreshEntryList();
        }

        protected override Tab MakeSwitchTarget() => BuildWizard();
        public override Tab MakeFallbackTab() => BuildWizard();

        Tab BuildWizard()
        {
            var wizard = BetterFGTabRegistry.NewTab<PetWizardTab>();
            wizard.EditIndex = EditIndexCarry;
            wizard.ResumeFromSkinTexture = this;
            return wizard;
        }
    }
}
