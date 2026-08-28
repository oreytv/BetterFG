using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Customization.Pets;
using BetterFG.Customization.Social;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tabs
{
    // pet-scoped phrase list, same PhraseEntry shape (text + image + up to 3 sounds) and row layout
    // the player's own Social > Phrases tab already uses (EmoticonsPhrasesTab) - reuses its
    // BuildSoundRows helper instead of a trimmed rebuild. delivery is the real MotorFunctionSpeech
    // system (PetSpeechComponent), not anything built here.
    public class PetPhrasesTab : SwitchTab
    {
        public PetPhrasesTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Pet Phrases";
        protected override string BgResource => "BetterFG.assets.ui.tab.customskintexture.png";
        protected override string SwitchLabel => "< Back";

        public PetData Snapshot;
        public int EditIndexCarry = -1;

        static readonly Color WHITE = Color.white;
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.4f);
        static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        static readonly Color BTN_ADD = new Color(0.22f, 0.42f, 0.22f, 1f);
        static readonly Color BTN_RM = new Color(0.45f, 0.1f, 0.1f, 1f);
        static readonly Color TOGGLE_ON = new Color(0.25f, 0.5f, 0.25f, 1f);
        static readonly Color TOGGLE_OFF = new Color(0.28f, 0.28f, 0.28f, 1f);
        static float ROW_H => UIScale.ROW_H * 1.6f;
        static float RBTN_H => BTN_H * 0.8f;

        RawImage _previewImg;
        RectTransform _phraseContent;
        string _editingId;
        Text _minValLbl, _maxValLbl;

        readonly Dictionary<string, Texture2D> _previewTextures = new Dictionary<string, Texture2D>();

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

            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, w, BTN_H), "+ Add Phrase", BTN_ADD, WHITE, FS_SM, new Action(AddPhrase));
            y += BTN_H + SH;

            float slidersH = (BTN_H + SH) * 2f;
            float listH = TabHeight - y - slidersH - SH - VPAD;
            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, listH));
            _phraseContent = scroll.content;
            var vlg = _phraseContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = PAD;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            _phraseContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += listH + SH;

            float valW = 50f * UIScale.S;
            UGUIShip.CreateSlider(contentRoot, PAD, y, w - valW - PAD, "Min gap", Mathf.InverseLerp(5f, 120f, Snapshot.phraseIntervalMin), BTN_H, PAD, (int)FS_SM,
                v =>
                {
                    Snapshot.phraseIntervalMin = Mathf.Lerp(5f, 120f, v);
                    if (Snapshot.phraseIntervalMax < Snapshot.phraseIntervalMin) Snapshot.phraseIntervalMax = Snapshot.phraseIntervalMin;
                    RefreshIntervalLabels();
                });
            _minValLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD + w - valW, y, valW, BTN_H), "", FS_SM, WHITE, TextAnchor.MiddleRight);
            y += BTN_H + SH;

            UGUIShip.CreateSlider(contentRoot, PAD, y, w - valW - PAD, "Max gap", Mathf.InverseLerp(5f, 120f, Snapshot.phraseIntervalMax), BTN_H, PAD, (int)FS_SM,
                v =>
                {
                    Snapshot.phraseIntervalMax = Mathf.Lerp(5f, 120f, v);
                    if (Snapshot.phraseIntervalMax < Snapshot.phraseIntervalMin) Snapshot.phraseIntervalMin = Snapshot.phraseIntervalMax;
                    RefreshIntervalLabels();
                });
            _maxValLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD + w - valW, y, valW, BTN_H), "", FS_SM, WHITE, TextAnchor.MiddleRight);

            RefreshIntervalLabels();
            RebuildPhraseRows();
        }

        void RefreshIntervalLabels()
        {
            if (_minValLbl != null) _minValLbl.text = Snapshot.phraseIntervalMin.ToString("0") + "s";
            if (_maxValLbl != null) _maxValLbl.text = Snapshot.phraseIntervalMax.ToString("0") + "s";
        }

        void SaveLive() => PetService.Instance?.SavePet(Snapshot, respawnIfActive: false);

        void AddPhrase()
        {
            Snapshot.phrases.Add(new PhraseEntry { phraseText = "new phrase" });
            SaveLive();
            RebuildPhraseRows();
        }

        Texture2D LoadPreview(PhraseEntry e)
        {
            if (string.IsNullOrEmpty(e.imagePath) || !File.Exists(e.imagePath)) return null;
            if (_previewTextures.TryGetValue(e.id, out var cached)) return cached;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(File.ReadAllBytes(e.imagePath));
                tex.Apply();
                tex.hideFlags = HideFlags.HideAndDontSave;
                _previewTextures[e.id] = tex;
                return tex;
            }
            catch { return null; }
        }

        void RebuildPhraseRows()
        {
            if (_phraseContent == null) return;
            for (int i = _phraseContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_phraseContent.GetChild(i).gameObject);

            var phrases = Snapshot.phrases;
            if (phrases.Count == 0)
                UGUIShip.CreateLabel(_phraseContent, new Rect(6f, 0f, TabWidth, ROW_H), "no phrases yet", FS_SM, HINT);

            for (int i = 0; i < phrases.Count; i++)
                CreateRow(phrases[i], i);
        }

        void CreateRow(PhraseEntry entry, int index)
        {
            int captured = index;
            bool editing = _editingId == entry.id;
            float rowH = editing ? ROW_H + (RBTN_H + PAD * 0.5f) * 3f + PAD : ROW_H;

            var rowGo = new GameObject("PhraseRow_" + entry.id);
            rowGo.transform.SetParent(_phraseContent, false);
            rowGo.AddComponent<RectTransform>();
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = rowH;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 1f);

            float imgColW = ROW_H;
            float leftW = TabWidth - PAD * 4f - imgColW - PAD;

            // ── image column ─────────────────────────────────────────────────
            var imgColGo = new GameObject("ImgCol");
            imgColGo.transform.SetParent(rowGo.transform, false);
            var imgColRt = imgColGo.AddComponent<RectTransform>();
            imgColRt.anchorMin = new Vector2(1f, 1f);
            imgColRt.anchorMax = new Vector2(1f, 1f);
            imgColRt.pivot = new Vector2(1f, 1f);
            imgColRt.sizeDelta = new Vector2(imgColW, ROW_H - PAD * 2f);
            imgColRt.anchoredPosition = new Vector2(-PAD, -PAD);
            imgColGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1f);

            Texture2D preview = LoadPreview(entry);
            if (preview != null)
            {
                var imgGo = new GameObject("Preview");
                imgGo.transform.SetParent(imgColGo.transform, false);
                var imgRt = imgGo.AddComponent<RectTransform>();
                imgRt.anchorMin = Vector2.zero; imgRt.anchorMax = Vector2.one;
                imgRt.offsetMin = imgRt.offsetMax = Vector2.zero;
                imgGo.AddComponent<RawImage>().texture = preview;
            }
            else
            {
                UGUIShip.CreateLabel(imgColGo.transform, new Rect(0f, 0f, imgColW, ROW_H - PAD * 2f), "No img", FS_SM, HINT, TextAnchor.MiddleCenter);
            }

            float browseBtnH = BTN_H * 0.8f;
            UGUIShip.CreateButton(imgColGo.transform, new Rect(0f, ROW_H - PAD * 2f - browseBtnH, imgColW, browseBtnH),
                "Browse", new Color(0.22f, 0.32f, 0.42f, 1f), WHITE, FS_SM,
                new Action(() => WinDialogs.PickPng("Select phrase image", path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    Snapshot.phrases[captured].imagePath = path;
                    _previewTextures.Remove(Snapshot.phrases[captured].id);
                    SaveLive();
                    RebuildPhraseRows();
                })));

            // ── left column: controls ──────────────────────────────────────────
            float gap = PAD * 0.5f;
            float line1Y = gap;
            float toggleW = BTN_H * 2.2f;
            float textW = leftW - toggleW - PAD;

            UGUIShip.CreateButton(rowGo.transform, new Rect(PAD, line1Y, toggleW, RBTN_H),
                entry.enabled ? "ON" : "OFF", entry.enabled ? TOGGLE_ON : TOGGLE_OFF, WHITE, FS_SM,
                new Action(() => { Snapshot.phrases[captured].enabled = !Snapshot.phrases[captured].enabled; SaveLive(); RebuildPhraseRows(); }));

            var tf = UGUIShip.CreateInputField(rowGo.transform, new Rect(PAD + toggleW + PAD, line1Y, textW, RBTN_H),
                "phrase text...", Color.black, WHITE, FS_SM);
            tf.text = entry.phraseText;
            tf.onEndEdit.AddListener(new Action<string>(val =>
            {
                if (captured < Snapshot.phrases.Count) Snapshot.phrases[captured].phraseText = val ?? "";
                SaveLive();
            }));

            float line2Y = line1Y + RBTN_H + gap;
            float editW = BTN_H * 2.2f;
            float minusW = BTN_H * 1.4f;

            UGUIShip.CreateButton(rowGo.transform, new Rect(leftW - minusW - editW, line2Y, editW, RBTN_H),
                editing ? "Done" : "Edit", editing ? new Color(0.2f, 0.4f, 0.25f, 1f) : new Color(0.22f, 0.32f, 0.42f, 1f), WHITE, FS_SM,
                new Action(() => { _editingId = editing ? null : entry.id; RebuildPhraseRows(); }));
            UGUIShip.CreateButton(rowGo.transform, new Rect(leftW - minusW + PAD, line2Y, minusW, RBTN_H),
                "-", BTN_RM, WHITE, FS,
                new Action(() => { Snapshot.phrases.RemoveAt(captured); SaveLive(); RebuildPhraseRows(); }));

            if (editing)
                EmoticonsPhrasesTab.BuildSoundRows(rowGo.transform, entry.soundPaths, line2Y + RBTN_H + gap, leftW, SaveLive);
        }

        protected override Tab MakeSwitchTarget() => BuildWizard();
        public override Tab MakeFallbackTab() => BuildWizard();

        Tab BuildWizard()
        {
            SaveLive();
            var wizard = BetterFGTabRegistry.NewTab<PetWizardTab>();
            wizard.EditIndex = EditIndexCarry;
            wizard.ResumeFromPhrases = this;
            return wizard;
        }
    }
}
