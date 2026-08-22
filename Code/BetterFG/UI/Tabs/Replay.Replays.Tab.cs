using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.Replay;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tabs
{
    public class ReplaysTab : ReplayTab
    {
        public ReplaysTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Replays";

        protected override string SwitchLabel => "Images →";
        protected override Tab MakeSwitchTarget() => BetterFGTabRegistry.NewTab<ReplayImagesTab>();

        const float ROW_H = 48f;
        const int PAGE_SIZE = 25;

        const string SP = "BetterFG.assets.ui.feature.qualificationtime.";
        static Sprite _starOn, _starOff, _delIdle, _delHover;
        static Sprite StarOn => _starOn ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_favoritedstar.png");
        static Sprite StarOff => _starOff ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_favoritestar.png");
        static Sprite DelIdle => _delIdle ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_delete_idle.png");
        static Sprite DelHover => _delHover ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_delete.png");

        static readonly Color TIME_COL = new Color(1f, 0.92f, 0.2f);

        Button _autoBtn, _filterBtn;
        InputField _searchField;
        bool _wasOpen;

        readonly List<(string path, RawImage raw, RectTransform iRt)> _pendingThumbs = new List<(string, RawImage, RectTransform)>();
        Coroutine _thumbRoutine;
        Coroutine _scanRoutine;

        readonly List<ReplayMeta> _data = new List<ReplayMeta>();
        List<ReplayMeta> _filtered = new List<ReplayMeta>();
        string _query = "";
        bool _filterFav, _filterCreative, _filterUnity;
        enum SortMode { Date, Name, Duration }
        SortMode _sort = SortMode.Date;
        bool _sortDesc = true;

        protected override float BuildHeader(RectTransform contentRoot, float y, float w)
        {
            float openW = 86f;
            float topH = BTN_H * 0.8f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, openW, topH), "Open file...",
                DARK, Color.white, FS_SM - 1, new Action(OpenFromDisk));

            bool auto = FeatureReplay.AutoRecord;
            _autoBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD + openW + PAD, y, w - openW - PAD, topH),
                AutoLabel(auto), auto ? GREEN : DARK, Color.white, FS_SM, new Action(ToggleAuto));
            y += topH + SH;

            UGUIShip.CreateDivider(contentRoot, PAD, y, w);
            y += 1f + SH;

            float ddW = 56f;
            float refreshW = 24f;
            float dirW = 24f;
            float searchW = w - (ddW + PAD) * 2f - (dirW + PAD) - (refreshW + PAD);

            _searchField = UGUIShip.CreateInputField(contentRoot, new Rect(PAD, y, searchW, HEADER_H),
                "search replays...", new Color(0f, 0f, 0f, 0.4f), Color.white, FS_SM);
            _searchField.onValueChanged.AddListener(new Action<string>(val =>
            {
                _query = val ?? "";
                ApplySearch();
            }));

            float ddX = PAD + searchW + PAD;
            float listW = 120f;

            _filterBtn = UGUIShip.CreateMultiSelectDropdown(contentRoot, new Rect(ddX, y, ddW, HEADER_H),
                "Filters",
                new List<string> { "Favourited", "Creative round", "Unity round" },
                new List<bool> { _filterFav, _filterCreative, _filterUnity },
                new Action<int, bool>((i, on) =>
                {
                    if (i == 0) _filterFav = on;
                    else if (i == 1) _filterCreative = on;
                    else _filterUnity = on;
                    var lbl = _filterBtn.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.color = (_filterFav || _filterCreative || _filterUnity) ? new Color(1f, 0.85f, 0.2f) : Color.white;
                    ApplySearch();
                }), FS_SM, listW);
            UGUIShip.AddHeaderIcon(_filterBtn, "BetterFG.assets.ui.button.filter.png");

            var sortBtn = UGUIShip.CreateMultiSelectDropdown(contentRoot, new Rect(ddX + ddW + PAD, y, ddW, HEADER_H),
                "Sort",
                new List<string> { "Sort by date", "Sort by name", "Sort by length" },
                new List<bool> { _sort == SortMode.Date, _sort == SortMode.Name, _sort == SortMode.Duration },
                new Action<int, bool>((i, on) =>
                {
                    _sort = i == 1 ? SortMode.Name : i == 2 ? SortMode.Duration : SortMode.Date;
                    ApplySearch();
                }), FS_SM, listW, 20f, true, true);
            UGUIShip.AddHeaderIcon(sortBtn, "BetterFG.assets.ui.button.sort.png");

            Button dirBtn = null;
            dirBtn = UGUIShip.CreateButton(contentRoot,
                new Rect(ddX + (ddW + PAD) * 2f, y, dirW, HEADER_H),
                _sortDesc ? "↓" : "↑", DARK, Color.white, FS_SM, new Action(() =>
                {
                    _sortDesc = !_sortDesc;
                    var lbl = dirBtn.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _sortDesc ? "↓" : "↑";
                    ApplySearch();
                }));
            var dirTxt = dirBtn.GetComponentInChildren<Text>();
            if (dirTxt != null)
            {
                dirTxt.fontSize = FS + 2;
                dirTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                dirTxt.verticalOverflow = VerticalWrapMode.Overflow;
            }

            var refreshBtn = UGUIShip.CreateButton(contentRoot,
                new Rect(ddX + (ddW + PAD) * 2f + dirW + PAD, y, refreshW, HEADER_H),
                "↻", DARK, Color.white, FS_SM, new Action(Refresh));
            var refreshTxt = refreshBtn.GetComponentInChildren<Text>();
            if (refreshTxt != null)
            {
                refreshTxt.fontSize = FS + 6;
                refreshTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                refreshTxt.verticalOverflow = VerticalWrapMode.Overflow;
            }

            return y + HEADER_H + SH;
        }

        protected override void BuildListLayout(RectTransform content)
        {
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 1f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void Update()
        {
            if (_wasOpen == IsOpen) return;
            _wasOpen = IsOpen;
            if (!IsOpen) return;
            Refresh();
            PaintAuto(FeatureReplay.AutoRecord);
        }

        static string AutoLabel(bool on) => on ? "Auto-record rounds: ON" : "Auto-record rounds: OFF";

        void ToggleAuto()
        {
            bool on = !FeatureReplay.AutoRecord;
            FeatureReplay.SetAutoRecord(on);
            PaintAuto(on);
            if (on)
                BetterFGUIMan.Instance?.ShowTooltipTimed(
                    "Unfortunately replays are very resource expensive. With this setting on, please beware of the giant FPS drop in certain levels.", 3.5f);
            Plugin.Log.LogInfo(on ? "replays will record from the next round start" : "auto-record off, rounds won't be saved");
        }

        void PaintAuto(bool on)
        {
            _autoBtn.GetComponentInChildren<Text>().text = AutoLabel(on);
            var col = on ? GREEN : DARK;
            _autoBtn.GetComponent<Image>().color = col;
            var cols = _autoBtn.colors;
            cols.normalColor = col;
            cols.highlightedColor = col * 1.2f;
            cols.pressedColor = col * 0.7f;
            _autoBtn.colors = cols;
        }

        // headers only. thumbnails and rows are built per page in RenderPage so a folder full of
        // replays doesn't turn into a folder full of live textures
        protected override void Refresh()
        {
            if (_scanRoutine != null) StopCoroutine(_scanRoutine);
            _scanRoutine = StartCoroutine(ScanFolder().WrapToIl2Cpp());
        }

        IEnumerator ScanFolder()
        {
            var files = LoadReplay.ListFiles();
            _data.Clear();

            bool painted = false;
            var slice = System.Diagnostics.Stopwatch.StartNew();
            foreach (string path in files)
            {
                _data.Add(LoadReplay.ReadMeta(path));
                if (slice.ElapsedMilliseconds < 4L) continue;

                if (!painted && _data.Count >= PAGE_SIZE) { painted = true; ApplySearch(); }
                SetStatus($"reading replays...  {_data.Count}/{files.Count}");
                yield return null;
                slice.Restart();
            }

            _scanRoutine = null;
            ApplySearch();
        }

        void ApplySearch()
        {
            string q = (_query ?? "").ToLowerInvariant();
            bool searching = q.Length > 0;

            var matched = _data.Where(m =>
            {
                if (searching && !m.haystack.Contains(q)) return false;
                if (_filterFav && !m.isFav) return false;
                if (_filterCreative && !m.isUgc) return false;
                if (_filterUnity && m.isUgc) return false;
                return true;
            });

            IOrderedEnumerable<ReplayMeta> ordered;
            if (_sort == SortMode.Name)
                ordered = _sortDesc
                    ? matched.OrderByDescending(m => m.label, StringComparer.OrdinalIgnoreCase)
                    : matched.OrderBy(m => m.label, StringComparer.OrdinalIgnoreCase);
            else if (_sort == SortMode.Duration)
                ordered = _sortDesc ? matched.OrderByDescending(m => m.duration) : matched.OrderBy(m => m.duration);
            else
                ordered = _sortDesc ? matched.OrderByDescending(m => m.when) : matched.OrderBy(m => m.when);
            _filtered = ordered.ToList();

            Page = 0;
            RenderPage();
        }

        protected override void RenderPage()
        {
            if (ListContent == null) return;
            if (_thumbRoutine != null) { StopCoroutine(_thumbRoutine); _thumbRoutine = null; }
            _pendingThumbs.Clear();

            ClearList();

            int total = _filtered.Count;
            int pageCount = Math.Max(1, (total + PAGE_SIZE - 1) / PAGE_SIZE);
            Page = Mathf.Clamp(Page, 0, pageCount - 1);
            int start = Page * PAGE_SIZE;
            int end = Math.Min(start + PAGE_SIZE, total);

            for (int i = start; i < end; i++)
                BuildRow(_filtered[i], (i - start) % 2 == 0 ? ROW_ALT : ROW_CLEAR);

            if (_pendingThumbs.Count > 0)
                _thumbRoutine = StartCoroutine(LoadPendingThumbs().WrapToIl2Cpp());

            ShowPaging(pageCount > 1);

            if (_data.Count == 0) SetStatus("nothing recorded yet");
            else if (total == 0) SetStatus("no results");
            else if (pageCount > 1) SetStatus(string.Format("{0} replays  ·  page {1}/{2}", total, Page + 1, pageCount));
            else SetStatus(total + (total == 1 ? " replay" : " replays"));
        }

        void BuildRow(ReplayMeta meta, Color bg)
        {
            var rowGo = new GameObject("ReplayRow");
            rowGo.transform.SetParent(ListContent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;

            // the zebra rides in the colour block rather than the image so the hover lift is the same
            // on both stripes — tinting a transparent row only ever multiplies back to nothing. no
            // EventTrigger either: it would eat the drag and the list would stop scrolling by hand
            var rowImg = rowGo.AddComponent<Image>();
            rowImg.color = Color.white;
            var rowBtn = rowGo.AddComponent<Button>();
            var cols = rowBtn.colors;
            cols.normalColor = bg;
            cols.highlightedColor = ROW_HOVER;
            cols.pressedColor = ROW_PRESS;
            cols.selectedColor = bg;
            cols.fadeDuration = 0f;
            rowBtn.colors = cols;
            var nav = rowBtn.navigation;
            nav.mode = Navigation.Mode.None;
            rowBtn.navigation = nav;
            rowBtn.onClick.AddListener(new Action(() =>
            {
                AudioService.PlayButtonClick();
                OpenFile(meta.path);
            }));

            float thumbW = ROW_H * 2.4f;
            float starW = 26f, delW = 24f;
            float rowW = TabWidth - PAD * 2f;

            var cachedThumb = ReplayThumbnail.Peek(meta.path);
            bool hasThumb = cachedThumb != null || ReplayThumbnail.HasThumbnail(meta.path);
            if (hasThumb)
            {
                var maskGo = new GameObject("Thumb");
                maskGo.transform.SetParent(rowGo.transform, false);
                var mRt = maskGo.AddComponent<RectTransform>();
                mRt.anchorMin = new Vector2(0f, 0f);
                mRt.anchorMax = new Vector2(0f, 1f);
                mRt.pivot = new Vector2(0f, 0.5f);
                mRt.anchoredPosition = new Vector2(0f, 0f);
                mRt.sizeDelta = new Vector2(thumbW, 0f);
                maskGo.AddComponent<RectMask2D>();

                var imgGo = new GameObject("Img");
                imgGo.transform.SetParent(maskGo.transform, false);
                var iRt = imgGo.AddComponent<RectTransform>();
                iRt.anchorMin = Vector2.zero;
                iRt.anchorMax = Vector2.one;
                iRt.pivot = new Vector2(0.5f, 0.5f);
                var raw = imgGo.AddComponent<RawImage>();
                raw.raycastTarget = false;

                if (cachedThumb != null)
                {
                    raw.texture = cachedThumb;
                    SizeThumbImage(iRt, thumbW, cachedThumb);
                }
                else
                {
                    _pendingThumbs.Add((meta.path, raw, iRt));
                }
            }

            float timeW = 80f;
            var t = TimeSpan.FromSeconds(meta.duration);
            var tTxt = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, timeW, FS + 4f),
                string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds), FS, TIME_COL, TextAnchor.UpperRight);
            tTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tRt = tTxt.rectTransform;
            tRt.anchorMin = tRt.anchorMax = tRt.pivot = new Vector2(1f, 1f);
            tRt.anchoredPosition = new Vector2(-(starW + delW + 8f), -3f);

            double sizeMb = meta.sizeBytes / (1024.0 * 1024.0);
            var szTxt = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, timeW, FS_SM),
                (sizeMb >= 100.0 ? $"{sizeMb:F0} MB" : $"{sizeMb:F1} MB"), FS_SM - 2, DIM, TextAnchor.UpperRight);
            szTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            var szRt = szTxt.rectTransform;
            szRt.anchorMin = szRt.anchorMax = szRt.pivot = new Vector2(1f, 1f);
            szRt.anchoredPosition = new Vector2(-(starW + delW + 8f), -3f - (FS + 4f));

            float textX = (hasThumb ? thumbW + 6f : 4f);
            float textW = rowW - textX - starW - delW - 12f;

            var dTxt = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, textW, FS_SM),
                meta.when.ToString("g", System.Globalization.CultureInfo.CurrentCulture),
                FS_SM - 2, DIM, TextAnchor.UpperLeft);
            dTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            dTxt.verticalOverflow = VerticalWrapMode.Truncate;
            var dRt2 = dTxt.rectTransform;
            dRt2.anchorMin = dRt2.anchorMax = dRt2.pivot = new Vector2(0f, 1f);
            dRt2.anchoredPosition = new Vector2(textX, -3f);

            var nameMask = new GameObject("NameMask");
            nameMask.transform.SetParent(rowGo.transform, false);
            var nmRt = nameMask.AddComponent<RectTransform>();
            nmRt.anchorMin = new Vector2(0f, 0f);
            nmRt.anchorMax = new Vector2(0f, 0f);
            nmRt.pivot = new Vector2(0f, 0f);
            nmRt.anchoredPosition = new Vector2(textX, 3f);
            nmRt.sizeDelta = new Vector2(textW, FS_SM + 4f);
            nameMask.AddComponent<RectMask2D>();

            var nTxt = UGUIShip.CreateLabel(nameMask.transform, new Rect(0f, 0f, textW + 400f, 0f),
                meta.label, FS_SM, meta.isUgc ? UGC_COL : Color.white, TextAnchor.LowerLeft);
            nTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            nTxt.verticalOverflow = VerticalWrapMode.Overflow;
            var nRt = nTxt.rectTransform;
            nRt.anchorMin = new Vector2(0f, 0f);
            nRt.anchorMax = new Vector2(0f, 1f);
            nRt.pivot = new Vector2(0f, 0f);
            nRt.anchoredPosition = Vector2.zero;

            string sub = meta.players == 1 ? "1 player" : meta.players + " players";
            if (!string.IsNullOrEmpty(meta.shareCode)) sub = meta.shareCode + "  ·  " + sub;
            var cTxt = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, textW, FS_SM),
                sub, FS_SM - 2, DIM, TextAnchor.LowerLeft);
            cTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            cTxt.verticalOverflow = VerticalWrapMode.Truncate;
            var cRt = cTxt.rectTransform;
            cRt.anchorMin = cRt.anchorMax = cRt.pivot = new Vector2(0f, 0f);
            cRt.anchoredPosition = new Vector2(textX, 3f + FS_SM + 3f);

            var starOn = StarOn;
            var starOff = StarOff;
            var (_, starImg) = UGUIShip.CreateSpriteButton(rowGo.transform,
                new Rect(0f, 0f, starW, starW), meta.isFav ? starOn : starOff, null, null);
            var sRt = starImg.GetComponent<RectTransform>();
            sRt.anchorMin = sRt.anchorMax = new Vector2(1f, 0.5f);
            sRt.pivot = new Vector2(1f, 0.5f);
            sRt.anchoredPosition = new Vector2(-(delW + 6f), 0f);
            starImg.GetComponent<Button>().onClick.AddListener(new Action(() =>
            {
                bool now = LoadReplay.ToggleFavourite(meta.path);
                meta.isFav = now;
                var spr = now ? starOn : starOff;
                if (spr != null) starImg.sprite = spr;
                if (_filterFav && !now) ApplySearch();
            }));

            var (_, delImg) = UGUIShip.CreateSpriteButton(rowGo.transform,
                new Rect(0f, 0f, delW, delW), DelIdle, DelHover, new Action(() =>
                {
                    if (LoadReplay.Delete(meta.path)) Refresh();
                }));
            var dRt = delImg.GetComponent<RectTransform>();
            dRt.anchorMin = dRt.anchorMax = new Vector2(1f, 0.5f);
            dRt.pivot = new Vector2(1f, 0.5f);
            dRt.anchoredPosition = new Vector2(-3f, 0f);
        }

        static void SizeThumbImage(RectTransform iRt, float thumbW, Texture thumb)
        {
            float imgH = thumbW / ((float)thumb.width / thumb.height);
            iRt.offsetMin = new Vector2(0f, -(imgH - ROW_H) * 0.5f);
            iRt.offsetMax = new Vector2(0f, (imgH - ROW_H) * 0.5f);
        }

        IEnumerator LoadPendingThumbs()
        {
            var batch = new List<(string path, RawImage raw, RectTransform iRt)>(_pendingThumbs);
            _pendingThumbs.Clear();

            foreach (var (path, raw, iRt) in batch)
            {
                if (raw == null || iRt == null) continue;
                var tex = ReplayThumbnail.Load(path);
                if (tex != null && raw != null && iRt != null)
                {
                    raw.texture = tex;
                    SizeThumbImage(iRt, ROW_H * 2.4f, tex);
                }
                yield return null;
            }
            _thumbRoutine = null;
        }

        void OpenFromDisk()
        {
            WinDialogs.PickFile("Open replay", new Action<string>(OpenFile), SaveReplay.PickerFilter);
        }

        static void OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            var rec = LoadReplay.Read(path);
            if (rec == null) return;

            BetterFGUIMan.Instance?.SetVisible(false);
            if (ReplayViewer.Instance != null) ReplayViewer.Instance.Swap(rec);
            else ReplayViewer.Open(rec);
        }
    }
}
