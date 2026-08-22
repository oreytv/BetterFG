using System;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Customization.Player;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tabs
{
    public class CustomSkinTextureTab : Tab
    {
        public CustomSkinTextureTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Skin Texture";

        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float LH => UIScale.LH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;

        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color BTN_APPLY = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color ROW_ALT = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_CLEAR = new Color(0f, 0f, 0f, 0f);
        private static readonly Color ROW_HOVER = new Color(1f, 1f, 1f, 0.13f);
        private static readonly Color ROW_PRESS = new Color(1f, 1f, 1f, 0.2f);
        private static readonly Color ROW_SEL = new Color(0.45f, 1f, 0.45f, 0.16f);

        private static float ROW_H => 30f * UIScale.S;

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
            catch (Exception ex) { Plugin.Log.LogError("CustomSkinTex: tex load fail: " + ex.Message); }
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

        private List<SkinTexEntry> _entries = new List<SkinTexEntry>();
        private int _selectedEntry = -1;

        private RectTransform _entryContent;
        private Text _statusLbl;

        protected override void BuildContent(RectTransform contentRoot)
        {
            _entries = SkinApplicationService.LoadEntries();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            float listH = TabHeight - y - BTN_H - LH - VPAD - 4f;
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
            y += listH + 2f;

            float halfW = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, halfW, BTN_H),
                "Apply Selected", BTN_APPLY, WHITE, FS_SM, new Action(OnApplySelected));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + halfW + PAD * 0.5f, y, halfW, BTN_H),
                "Revert All", BTN_REMOVE, WHITE, FS_SM, new Action(OnRevert));
            y += BTN_H + 2f;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshEntryList();
        }

        public override void OnOpened()
        {
            _entries = SkinApplicationService.LoadEntries();
            if (_selectedEntry >= _entries.Count) _selectedEntry = _entries.Count - 1;
            RefreshEntryList();
        }

        private void RefreshEntryList()
        {
            if (_entryContent == null) return;

            for (int i = _entryContent.childCount - 1; i >= 0; i--)
            {
                var ch = _entryContent.GetChild(i);
                if (ch != null) GameObject.Destroy(ch.gameObject);
            }

            _rows.Clear();
            float rowW = TabWidth - PAD * 2f - 8f;

            for (int i = 0; i < _entries.Count; i++)
            {
                int idx = i;
                var entry = _entries[i];

                var rowGo = new GameObject("ERow_" + i);
                rowGo.transform.SetParent(_entryContent, false);
                rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;
                rowGo.AddComponent<Image>().color = WHITE;

                var rowBtn = rowGo.AddComponent<Button>();
                var nav = rowBtn.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.None;
                rowBtn.navigation = nav;
                rowBtn.onClick.AddListener(new Action(() =>
                {
                    AudioService.PlayButtonClick();
                    SelectEntry(idx);
                }));

                float thumbSz = (ROW_H - 6f) * 1.4f;
                var thumbGo = new GameObject("Thumb");
                thumbGo.transform.SetParent(rowBtn.transform, false);
                var thumbRt = thumbGo.AddComponent<RectTransform>();
                UGUIShip.SetPixelRect(thumbRt, new Rect(3f + thumbSz * 0.2f, 3f - thumbSz * 0.2f, thumbSz, thumbSz));
                var raw = thumbGo.AddComponent<RawImage>();
                raw.raycastTarget = false;
                Texture thumb = CostumeIconTexture(entry.costumeName);
                if (thumb == null) thumb = SkinApplicationService.GetCachedCustomTex(entry);
                if (thumb != null) raw.texture = thumb;
                else raw.color = new Color(0f, 0f, 0f, 0.4f);

                float editW = 30f * UIScale.S, toggleW = 30f * UIScale.S, removeW = 22f * UIScale.S;
                float nameX = 3f + thumbSz * 1.2f + 6f;
                float nameW = rowW - editW - toggleW - removeW - nameX - 10f;

                var nameLbl = UGUIShip.CreateLabel(rowBtn.transform,
                    new Rect(nameX, 0f, nameW, ROW_H), entry.entryName,
                    FS_SM, WHITE, TextAnchor.MiddleLeft);

                BuildRowBtn(rowBtn.transform, -(removeW + toggleW + editW + 4f), editW,
                    "edit", BTN_DARK, () => OpenWizard(idx));

                var toggleBtn = BuildRowBtn(rowBtn.transform, -(removeW + toggleW + 2f), toggleW,
                    "on", BTN_APPLY, () => ToggleEntry(idx));

                BuildRowBtn(rowBtn.transform, -2f, removeW, "x", BTN_REMOVE, () => RemoveEntry(idx));

                _rows.Add(new RowRefs
                {
                    Row = rowBtn,
                    Name = nameLbl,
                    Toggle = toggleBtn,
                    ToggleLbl = toggleBtn.GetComponentInChildren<Text>()
                });
            }

            PaintRows();

            var addBtn = UGUIShip.CreateButton(_entryContent, new Rect(0f, 0f, TabWidth - PAD * 2f - 8f, ROW_H),
                "+ Add Texture", BTN_ADD, WHITE, FS, new Action(() => OpenWizard(-1)));
            var addLe = addBtn.gameObject.AddComponent<LayoutElement>();
            addLe.preferredHeight = ROW_H;
            addLe.flexibleWidth = 1f;
        }

        private static Dictionary<string, Sprite> _costumeIcons;
        private static readonly Dictionary<string, Texture2D> _iconTextures =
            new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private static Texture2D CostumeIconTexture(string costumeName)
        {
            if (string.IsNullOrEmpty(costumeName)) return null;
            if (_iconTextures.TryGetValue(costumeName, out var owned) && owned != null) return owned;

            var sprite = CostumeIcon(costumeName);
            if (sprite == null) return null;
            var atlas = sprite.texture;
            if (atlas == null) return null;

            var tr = sprite.textureRect;
            int rx = Mathf.Clamp(Mathf.FloorToInt(tr.x), 0, atlas.width);
            int ry = Mathf.Clamp(Mathf.FloorToInt(tr.y), 0, atlas.height);
            int rw = Mathf.Clamp(Mathf.CeilToInt(tr.width), 1, atlas.width - rx);
            int rh = Mathf.Clamp(Mathf.CeilToInt(tr.height), 1, atlas.height - ry);

            var rt = RenderTexture.GetTemporary(atlas.width, atlas.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(atlas, rt);
            RenderTexture.active = rt;
            var crop = new Texture2D(rw, rh, TextureFormat.RGBA32, false);
            crop.ReadPixels(new Rect(rx, ry, rw, rh), 0, 0);
            crop.Apply();
            crop.hideFlags = HideFlags.HideAndDontSave;
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            _iconTextures[costumeName] = crop;
            return crop;
        }

        private static Sprite CostumeIcon(string costumeName)
        {
            if (_costumeIcons != null)
            {
                if (!_costumeIcons.TryGetValue(costumeName, out var hit)) return null;
                if (hit != null) return hit;
                _costumeIcons = null;
            }

            var found = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            var all = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<CostumeOption>());
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    CostumeOption opt;
                    try { opt = all[i].Cast<CostumeOption>(); } catch { continue; }
                    Sprite spr = null;
                    try { spr = ((ItemDefinitionSO)opt)?.MenuDisplaySprite; } catch { }
                    if (spr != null && !string.IsNullOrEmpty(opt.name)) found[opt.name] = spr;
                }
            }
            if (found.Count == 0) return null;
            _costumeIcons = found;
            Plugin.Log.LogInfo($"costume icons for the skin texture list: {found.Count}");

            return _costumeIcons.TryGetValue(costumeName, out var sprite) ? sprite : null;
        }

        private Button BuildRowBtn(Transform parent, float anchoredX, float bw,
            string label, Color bg, Action onClick)
        {
            float bh = Mathf.Min(ROW_H - 6f, 24f * UIScale.S);
            var btn = UGUIShip.CreateButton(parent, new Rect(0f, 0f, bw, bh), label, bg, WHITE, FS_SM - 1, onClick);
            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(anchoredX, 0f);
            rt.sizeDelta = new Vector2(bw, bh);
            return btn;
        }

        private struct RowRefs
        {
            public Button Row;
            public Button Toggle;
            public Text ToggleLbl;
            public Text Name;
        }

        private readonly List<RowRefs> _rows = new List<RowRefs>();

        private void PaintRows()
        {
            for (int i = 0; i < _rows.Count && i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var row = _rows[i];

                var cols = row.Row.colors;
                cols.normalColor = i == _selectedEntry ? ROW_SEL : (i % 2 == 0 ? ROW_ALT : ROW_CLEAR);
                cols.highlightedColor = ROW_HOVER;
                cols.pressedColor = ROW_PRESS;
                cols.selectedColor = cols.normalColor;
                cols.fadeDuration = 0f;
                row.Row.colors = cols;

                UGUIShip.SetButtonColor(row.Toggle, entry.enabled ? BTN_APPLY : BTN_DARK);
                row.ToggleLbl.text = entry.enabled ? "on" : "off";
                row.Name.color = entry.enabled ? WHITE : DIM;
            }
        }

        private void OpenWizard(int editIdx)
        {
            var wizard = BetterFGTabRegistry.NewTab<SkinTextureWizardTab>();
            wizard.EditIndex = editIdx;
            BetterFGUIMan.Instance?.SwitchSlotTab(this, wizard);
        }

        private void SelectEntry(int idx)
        {
            if (_selectedEntry == idx)
            {
                _selectedEntry = -1;
                PaintRows();
                return;
            }
            _selectedEntry = idx;
            PaintRows();
            SetStatus(_entries[idx].entryName + " selected");
        }

        private void ToggleEntry(int idx)
        {
            _entries[idx].enabled = !_entries[idx].enabled;
            SkinApplicationService.SaveEntries(_entries);
            PaintRows();
            RevertAllEnabled();
        }

        private void RemoveEntry(int idx)
        {
            bool wasEnabled = _entries[idx].enabled;
            _entries.RemoveAt(idx);
            if (_selectedEntry >= _entries.Count) _selectedEntry = _entries.Count - 1;
            SkinApplicationService.SaveEntries(_entries);
            RefreshEntryList();

            if (wasEnabled) RevertAllEnabled();
        }

        private void OnApplySelected()
        {
            if (_selectedEntry < 0 || _selectedEntry >= _entries.Count)
            {
                SetStatus("select an entry first");
                return;
            }
            if (!_entries[_selectedEntry].enabled) { SetStatus("entry is disabled"); return; }
            RevertAllEnabled();
        }

        private void RevertAllEnabled()
            => SkinApplicationService.ReapplyAllEnabled(_entries, SetStatus);

        private void OnRevert()
        {
            SkinApplicationService.RevertAllBeans();
            SetStatus("reverted");
        }

        public void SetStatus(string msg)
        {
            if (_statusLbl != null) _statusLbl.text = msg;
        }
    }
}
