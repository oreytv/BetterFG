using System;
using System.Collections.Generic;
using BetterFG.Customization.Pets;
using BetterFG.Customization.Player;
using BetterFG.Customization.Social;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Tabs
{
    public class PetWizardTab : WizardTab
    {
        public PetWizardTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => EditIndex >= 0 ? "Pet - Edit" : "Pet - New";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";

        static readonly Color BTN_REMOVE = UGUIShip.BTN_REMOVE;

        enum WizardStep { Name, Cosmetics, Scale, SkinTexture, UgcCustomization, Phrases }
        protected override string[] StepTitles => new[]
        {
            "ui.name", "ui.in_game_cosmetics", "ui.scale_2", "ui.skin_texture_optional", "ui.bettrfg_ugc_customization_optional", "ui.phrases_optional"
        };

        string _id = Guid.NewGuid().ToString("N");
        InputField _nameField;
        float _scale = 0.6f;
        string _top = "", _bottom = "", _pattern = "", _faceplate = "", _colour = "";
        SkinInfo _costume;
        List<SkinTexEntry> _skinTexEntries = new List<SkinTexEntry>();
        readonly List<PhraseEntry> _phrases = new List<PhraseEntry>();
        float _phraseIntervalMin = 15f, _phraseIntervalMax = 45f;

        RawImage _previewImg;
        Text _lookLbl;
        Text _costumeLbl;
        Text _skinTexLbl;
        Text _scaleValueLbl;

        // the render texture only gets pixels drawn into it when something actually calls
        // PetPreview.Render() - EyePreview's own tab does this every frame while visible
        // (FeatureCustomizeFallGuys.TickPreview), same shape here
        void Update() { if (IsOpen) PetPreview.Render(); }

        protected override void BuildContent(RectTransform contentRoot)
        {
            _previewImg = PetPreviewPanel.Build(Root, TabWidth, TabHeight, TITLE_H, SH, UIScale.S);
            _previewImg.transform.parent.gameObject.SetActive(false);
            base.BuildContent(contentRoot);
        }

        // the preview frame lives outside _contentArea (see PetPreviewPanel), so closing the tab
        // doesn't hide it on its own
        public override void OnOpened()
        {
            if (_previewImg != null) _previewImg.transform.parent.gameObject.SetActive(true);
        }

        public override void OnClosed()
        {
            if (_previewImg != null) _previewImg.transform.parent.gameObject.SetActive(false);
            PetPreview.Invalidate();
        }

        protected override void BuildStep(int step, RectTransform root, float w, float bodyH)
        {
            switch ((WizardStep)step)
            {
                case WizardStep.Name: BuildNameStep(root, w); break;
                case WizardStep.Cosmetics: BuildCosmeticsStep(root, w); break;
                case WizardStep.Scale: BuildScaleStep(root, w); break;
                case WizardStep.SkinTexture: BuildSkinTextureStep(root, w); break;
                case WizardStep.UgcCustomization: BuildUgcStep(root, w); break;
                case WizardStep.Phrases: BuildPhrasesStep(root, w); break;
            }
        }

        void BuildNameStep(RectTransform root, float w)
        {
            float y = SH;
            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH), "ui.what_s_your_pet_called", FS_SM, LABEL);
            y += LH + SH;
            _nameField = UGUIShip.CreateInputField(root, new Rect(PAD, y, w, BTN_H), "ui.pet_name", Color.black, WHITE, FS_SM);
        }

        void BuildCosmeticsStep(RectTransform root, float w)
        {
            float y = SH;
            _lookLbl = UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH * 2f), LookSummary(), FS_SM, WHITE, TextAnchor.UpperLeft);
            y += LH * 2f + SH;

            UGUIShip.CreateButton(root, new Rect(PAD, y, w, BTN_H), "ui.choose_look", BTN_DARK, WHITE, FS_SM,
                new Action(OpenLookPicker));
        }

        void BuildScaleStep(RectTransform root, float w)
        {
            float y = SH;
            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH), "ui.how_big_should_the_pet_be", FS_SM, LABEL);
            y += LH + SH;

            float valW = 50f * UIScale.S;
            UGUIShip.CreateSlider(root, PAD, y, w - valW - PAD, "Scale", _scale, BTN_H, PAD, (int)FS_SM,
                v => { _scale = v; RefreshScaleLabel(); RefreshPreviewFromLook(); });
            _scaleValueLbl = UGUIShip.CreateLabel(root, new Rect(PAD + w - valW, y, valW, BTN_H), ScaleText(), FS_SM, WHITE, TextAnchor.MiddleRight);
        }

        string ScaleText() => _scale.ToString("0.00") + "x";
        void RefreshScaleLabel() { if (_scaleValueLbl != null) UGUIShip.RelabelText(_scaleValueLbl, ScaleText()); }

        string LookSummary() =>
            $"Upper: {ResolvedOrNone(_top)}\n" +
            $"Lower: {ResolvedOrNone(_bottom)}   " +
            $"Pattern: {ResolvedOrNone(_pattern)}   " +
            $"Faceplate: {ResolvedOrNone(_faceplate)}   " +
            $"Colour: {ResolvedOrNone(_colour)}";

        static string ResolvedOrNone(string optionName) =>
            string.IsNullOrEmpty(optionName) ? "None" : SkinApplicationService.ResolveOptionDisplayName(optionName);

        void OpenLookPicker()
        {
            var picker = BetterFGTabRegistry.NewTab<PetLookPickerTab>();
            picker.Snapshot = CurrentData();
            picker.EditIndexCarry = EditIndex;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, picker);
        }

        void BuildSkinTextureStep(RectTransform root, float w)
        {
            float y = SH;
            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH),
                "ui.optional_give_this_pet_its_own_texture_overrides", FS_SM, HINT, TextAnchor.UpperLeft);
            y += LH + SH;

            _skinTexLbl = UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH), SkinTexSummary(), FS_SM, WHITE);
            y += LH + SH;

            UGUIShip.CreateButton(root, new Rect(PAD, y, w, BTN_H), "ui.edit_skin_textures", BTN_BLUE, WHITE, FS_SM,
                new Action(OpenSkinTextures));
        }

        string SkinTexSummary() => _skinTexEntries.Count == 0 ? "none yet" : $"{_skinTexEntries.Count} texture(s) applied";

        void OpenSkinTextures()
        {
            var picker = BetterFGTabRegistry.NewTab<PetSkinTextureTab>();
            picker.Snapshot = CurrentData();
            picker.EditIndexCarry = EditIndex;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, picker);
        }

        void BuildUgcStep(RectTransform root, float w)
        {
            float y = SH;
            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH),
                "ui.optional_dress_the_pet_in_a_full_costume_from_yo",
                FS_SM, HINT, TextAnchor.UpperLeft);
            y += LH * 2f + SH;

            _costumeLbl = UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH),
                _costume != null ? _costume.name : "No costume attached", FS_SM, WHITE);
            y += LH + SH;

            float halfW = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, halfW, BTN_H), "ui.choose_costume", BTN_DARK, WHITE, FS_SM,
                new Action(OpenCostumePicker));
            UGUIShip.CreateButton(root, new Rect(PAD + halfW + PAD * 0.5f, y, halfW, BTN_H), "ui.remove_costume", BTN_REMOVE, WHITE, FS_SM,
                new Action(() => { _costume = null; if (_costumeLbl != null) UGUIShip.RelabelText(_costumeLbl, "ui.no_costume_attached"); RefreshPreviewFromLook(); }));
        }

        // reuses the real UGC Customization tab to pick a costume rather than a trimmed rebuild of
        // it - CustomizationTab.PetPickTarget puts it in pick mode: Select on a Costume row (or its
        // own "< Back" link) calls this with the skin (null = back/cancel) and switches to whatever
        // Tab this returns, exactly like SwitchTab's own back-link mechanism
        void OpenCostumePicker()
        {
            var snapshot = CurrentData();
            int editIndexCarry = EditIndex;

            var picker = BetterFGTabRegistry.NewTab<CustomizationTab>();
            picker.PetPickTarget = skin =>
            {
                if (skin != null) snapshot.costume = skin;
                var wizard = BetterFGTabRegistry.NewTab<PetWizardTab>();
                wizard.EditIndex = editIndexCarry;
                wizard.ResumeFromCostume = snapshot;
                return wizard;
            };
            BetterFGUIMan.Instance?.SwitchSlotTab(this, picker);
        }

        void BuildPhrasesStep(RectTransform root, float w)
        {
            float y = SH;
            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH),
                "ui.optional_phrases_the_pet_pops_up_above_its_head", FS_SM, HINT, TextAnchor.UpperLeft);
            y += LH + SH;

            UGUIShip.CreateLabel(root, new Rect(PAD, y, w, LH),
                _phrases.Count == 0 ? "none yet" : $"{_phrases.Count} phrase(s)", FS_SM, WHITE);
            y += LH + SH;

            UGUIShip.CreateButton(root, new Rect(PAD, y, w, BTN_H), "ui.edit_phrases", BTN_BLUE, WHITE, FS_SM,
                new Action(OpenPhrases));
        }

        void OpenPhrases()
        {
            var picker = BetterFGTabRegistry.NewTab<PetPhrasesTab>();
            picker.Snapshot = CurrentData();
            picker.EditIndexCarry = EditIndex;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, picker);
        }

        void RefreshPreviewFromLook()
        {
            PetPreview.Rebuild(this, CurrentData());
            if (_previewImg != null) _previewImg.texture = PetPreview.Ensure();
        }

        PetData CurrentData()
        {
            return new PetData
            {
                id = _id,
                name = string.IsNullOrEmpty(_nameField?.text) ? "Pet" : _nameField.text.Trim(),
                costumeTop = _top, costumeBottom = _bottom, pattern = _pattern, faceplate = _faceplate, colour = _colour,
                scale = Mathf.Clamp(_scale, 0.3f, 1.5f),
                costume = _costume,
                skinTexEntries = _skinTexEntries,
                phrases = new List<PhraseEntry>(_phrases),
                phraseIntervalMin = _phraseIntervalMin,
                phraseIntervalMax = _phraseIntervalMax,
            };
        }

        // PetLookPickerTab/PetSkinTextureTab/PetPhrasesTab and the CustomizationTab pick-mode round
        // trip all destroy this wizard to navigate away (SwitchSlotTab always destroys `from`), so
        // they carry the in-progress pet forward as a PetData snapshot instead of trying to keep
        // this instance alive
        public PetLookPickerTab ResumeFromLook;
        public PetData ResumeFromCostume;
        public PetSkinTextureTab ResumeFromSkinTexture;
        public PetPhrasesTab ResumeFromPhrases;
        protected override bool SkipLoadEditedEntry =>
            ResumeFromLook != null || ResumeFromCostume != null || ResumeFromSkinTexture != null || ResumeFromPhrases != null;

        PetData _pendingLoad;

        protected override int LoadEditedEntry()
        {
            var pets = PetService.Instance?.Pets;
            if (pets == null || EditIndex >= pets.Count) { EditIndex = -1; return -1; }
            _pendingLoad = pets[EditIndex];
            return 0;
        }

        protected override void ResumeWip()
        {
            PetData data = null;
            if (ResumeFromLook != null)
            {
                data = ResumeFromLook.Snapshot;
                Step = (int)WizardStep.Cosmetics;
                ResumeFromLook = null;
            }
            else if (ResumeFromCostume != null)
            {
                data = ResumeFromCostume;
                Step = (int)WizardStep.UgcCustomization;
                ResumeFromCostume = null;
            }
            else if (ResumeFromSkinTexture != null)
            {
                data = ResumeFromSkinTexture.Snapshot;
                Step = (int)WizardStep.SkinTexture;
                ResumeFromSkinTexture = null;
            }
            else if (ResumeFromPhrases != null)
            {
                data = ResumeFromPhrases.Snapshot;
                Step = (int)WizardStep.Phrases;
                ResumeFromPhrases = null;
            }
            else if (_pendingLoad != null)
            {
                data = _pendingLoad;
                _pendingLoad = null;
            }

            if (data != null) ApplyData(data);
            RefreshScaleLabel();
            RefreshPreviewFromLook();
        }

        void ApplyData(PetData data)
        {
            _id = data.id;
            _scale = data.scale;
            _costume = data.costume;
            _top = data.costumeTop ?? ""; _bottom = data.costumeBottom ?? "";
            _pattern = data.pattern ?? ""; _faceplate = data.faceplate ?? ""; _colour = data.colour ?? "";
            _skinTexEntries = data.skinTexEntries ?? new List<SkinTexEntry>();
            _phrases.Clear();
            if (data.phrases != null) _phrases.AddRange(data.phrases);
            _phraseIntervalMin = data.phraseIntervalMin;
            _phraseIntervalMax = data.phraseIntervalMax;
            UGUIShip.SetInputText(_nameField, data.name, false);
            if (_lookLbl != null) UGUIShip.RelabelText(_lookLbl, LookSummary());
            if (_costumeLbl != null) UGUIShip.RelabelText(_costumeLbl, _costume != null ? _costume.name : "No costume attached");
            if (_skinTexLbl != null) UGUIShip.RelabelText(_skinTexLbl, SkinTexSummary());
        }

        protected override Tab MakeListTarget() => BetterFGTabRegistry.CreateTab("Pets");

        protected override bool Save()
        {
            var data = CurrentData();
            if (string.IsNullOrEmpty(data.name)) { SetStatus("ui.give_your_pet_a_name_first"); return false; }
            PetService.Instance?.SavePet(data);
            Plugin.Log.LogInfo($"pet saved: {data.name} ({data.costumeTop}/{data.costumeBottom}/{data.pattern}, scale {data.scale})");
            return true;
        }

        protected override void OnLeave() => PetPreview.Invalidate();
    }
}
