using System;
using System.Collections.Generic;
using BetterFG.Customization.Menu;
using BetterFG.Customization.Player;
using BetterFG.Services;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class PlinthsTab : BetterFGTab
    {
        public PlinthsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Plinths";
        protected override string TitleDisplay => _source == Source.Ugc ? "Plinths - UGC" : "Plinths - In-game";

        static float PAD => UIScale.PAD;
        static float SH => UIScale.SH;
        static float BTN_H => UIScale.BTN_H;
        static float ROW_H => UIScale.ROW_H;
        static float COVER_W => UIScale.COVER_W;
        static float COVER_H => UIScale.COVER_H;
        static float SEL_W => UIScale.SEL_W;
        static int FS => UIScale.FS;
        static int FS_SM => UIScale.FS_SM;

        static readonly Color WHITE = Color.white;
        static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        static readonly Color BTN_DARK = new Color(0.18f, 0.18f, 0.18f, 1f);
        static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        static readonly Color COVER_BG = new Color(0.04f, 0.04f, 0.04f, 1f);
        static readonly Color ITEM_BG = new Color(0f, 0f, 0f, 0f);
        static readonly Color ORANGE = new Color(1f, 0.55f, 0.1f, 1f);
        static readonly Color LINK = new Color(1f, 0.72f, 0.35f, 0.85f);

        enum Source { InGame, Ugc }
        Source _source = Source.InGame;

        static Texture2D _bgTex, _hoverTex;
        GameObject _bgHoverGo;

        Text _titleText, _switchLink;
        RectTransform _listContent, _scrollRt;
        Text _statusLbl;

        static readonly Dictionary<string, Texture2D> _gameCovers = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Image> _ugcCoverImgs = new Dictionary<string, Image>();

        static MenuCustomizationApplication PlinthApp => CustomizationServices.PlinthApp;
        static SkinCatalogService Catalog => CustomizationServices.CatalogService;

        void Awake()
        {
            if (Catalog != null)
            {
                Catalog.OnSkinCoverLoaded += OnCoverLoaded;
                Catalog.OnSkinsLoaded += OnSkinsLoaded;
                Catalog.OnFetchCompleted += OnCatalogChanged;
            }
            if (PlinthApp != null) PlinthApp.OnStatus += OnPlinthStatus;
        }

        void OnDestroy()
        {
            if (Catalog != null)
            {
                Catalog.OnSkinCoverLoaded -= OnCoverLoaded;
                Catalog.OnSkinsLoaded -= OnSkinsLoaded;
                Catalog.OnFetchCompleted -= OnCatalogChanged;
            }
            if (PlinthApp != null) PlinthApp.OnStatus -= OnPlinthStatus;
        }

        void OnPlinthStatus(string msg)
        {
            Rebuild();
            SetStatus(msg);
        }

        static Texture2D LoadTex(string resource, ref Texture2D cache)
        {
            if (cache != null) return cache;
            cache = EmbeddedResourceandUnity.LoadTexture(resource);
            return cache;
        }

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.tab.plinths.png", ref _bgTex);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0f);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = LoadTex("BetterFG.assets.ui.bg_hover.png", ref _hoverTex);
            if (hoverTex == null) return;

            var hoverGo = new GameObject("BG_Hover");
            hoverGo.transform.SetParent(bgGo.transform, false);
            var hoverRt = hoverGo.AddComponent<RectTransform>();
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
            hoverGo.AddComponent<RawImage>().texture = hoverTex;
            hoverGo.SetActive(false);
            _bgHoverGo = hoverGo;
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        string SwitchLabel => _source == Source.Ugc ? "In-game →" : "UGC →";

        protected override void BuildTitleExtras(Transform titleBar, Text title)
        {
            _titleText = title;
            _switchLink = UGUIShip.CreateLinkText(titleBar, new Rect(0f, 0f, 90f, TITLE_H), SwitchLabel,
                new Action(() => SetSource(_source == Source.Ugc ? Source.InGame : Source.Ugc)), LINK, FS_SM);
            _switchLink.gameObject.SetActive(IsOpen);
        }

        public override void OnOpened() => _switchLink.gameObject.SetActive(true);
        public override void OnClosed() => _switchLink.gameObject.SetActive(false);

        void RefreshSwitchLink()
        {
            _switchLink.text = SwitchLabel;
            var rt = _switchLink.rectTransform;
            UGUIShip.SetPixelRect(rt, new Rect(_titleText.rectTransform.offsetMin.x + _titleText.preferredWidth + PAD * 1.5f,
                0f, rt.sizeDelta.x, TITLE_H));
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;

            _repoRowParent = contentRoot;
            _repoRowY = PAD;
            _repoRowW = w;

            var (scroll, content) = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, PAD, w, BTN_H));
            _scrollRt = scroll.GetComponent<RectTransform>();
            _listContent = content;
            var vlg = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD * 0.5f;
            vlg.padding = new RectOffset(0, (int)PAD, (int)(PAD * 0.5f), (int)(PAD * 0.5f));
            _listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            float statusH = UIScale.LH;
            float statusY = TabHeight - PAD - statusH;
            float removeW = 64f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - removeW, statusY, removeW, statusH), "Remove",
                BTN_REMOVE, WHITE, FS_SM, new Action(OnRemove));
            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, statusY, w - removeW - PAD, statusH),
                "", FS_SM, HINT, TextAnchor.MiddleLeft);

            SetSource(_source);
        }

        RectTransform _repoRowParent;
        float _repoRowY, _repoRowW;

        public override void OnRepoChanged()
        {
            if (_source == Source.Ugc) RepoSelectorTab.BuildCurrentRepoRow(this, _repoRowParent, _repoRowY, _repoRowW);
            Rebuild();
        }

        void SetSource(Source src)
        {
            _source = src;
            RefreshTitle();
            RefreshSwitchLink();

            float y = _repoRowY;
            if (src == Source.Ugc) y = RepoSelectorTab.BuildCurrentRepoRow(this, _repoRowParent, _repoRowY, _repoRowW);
            else
            {
                var existingRow = _repoRowParent.Find("RepoActiveRow");
                if (existingRow != null) Destroy(existingRow.gameObject);
            }

            float statusY = TabHeight - PAD - UIScale.LH;
            UGUIShip.SetPixelRect(_scrollRt, new Rect(PAD, y, _repoRowW, statusY - SH - y));

            Rebuild();
        }

        void SetStatus(string msg)
        {
            if (_statusLbl != null) _statusLbl.text = msg;
        }

        void OnSkinsLoaded(List<SkinInfo> _) => OnCatalogChanged();

        void OnCatalogChanged()
        {
            if (_source == Source.Ugc) Rebuild();
        }

        void OnCoverLoaded(string key, Texture2D tex)
        {
            if (tex == null) return;
            if (_ugcCoverImgs.TryGetValue(key, out var img) && img != null) ApplyCover(img, tex);
        }

        void OnRemove()
        {
            PlinthApp?.RemovePlinth();
            Rebuild();
            SetStatus("Plinth removed.");
        }

        void Rebuild()
        {
            if (_listContent == null) return;

            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);
            _ugcCoverImgs.Clear();

            if (_source == Source.InGame) BuildGameRows();
            else BuildUgcRows();
        }

        void BuildGameRows()
        {
            string active = PlinthApp?.ActiveGameId;
            foreach (var gp in MenuCustomizationApplication.GamePlinths)
            {
                var captured = gp;
                var img = BuildRow(gp.Label, "from the game", gp.Id == active, new Action(() =>
                {
                    PlinthApp?.ApplyGamePlinth(captured);
                    SetStatus($"Applying {captured.Label}...");
                }));

                if (!_gameCovers.TryGetValue(gp.Id, out var tex) || tex == null)
                {
                    tex = EmbeddedResourceandUnity.LoadTexture(gp.Cover);
                    if (tex != null) _gameCovers[gp.Id] = tex;
                }
                if (tex != null) ApplyCover(img, tex);
                else UGUIShip.CreateStretchLabel(img.transform, "No Preview", FS_SM, HINT);
            }

            SetStatus($"{MenuCustomizationApplication.GamePlinths.Length} game plinths.");
        }

        void BuildUgcRows()
        {
            var catalog = Catalog;
            if (catalog == null) { SetStatus("Catalog isn't up yet."); return; }

            var plinths = new List<SkinInfo>();
            foreach (var skin in catalog.AvailableSkins)
                if (SkinTypeParser.FromString(skin.type) == SkinType.Plinth) plinths.Add(skin);

            if (plinths.Count == 0)
            {
                var reg = RepoRegistry.Instance;
                bool fetching = catalog.IsFetching;
                if (!fetching && reg?.Active != null && !catalog.IsFetchedRepo(reg.Active.githubUrl))
                {
                    catalog.FetchSkins(reg.Active);
                    fetching = true;
                }

                var hintGo = new GameObject("Empty");
                hintGo.transform.SetParent(_listContent, false);
                hintGo.AddComponent<RectTransform>();
                hintGo.AddComponent<LayoutElement>().preferredHeight = ROW_H;
                UGUIShip.CreateStretchLabel(hintGo.transform,
                    fetching ? "Fetching..." : "No plinths in this repo. Pick another above.", FS_SM, HINT);
                SetStatus(fetching ? "Fetching..." : "Nothing here yet.");
                return;
            }

            string activeFile = PlinthApp?.ActiveFile;
            foreach (var skin in plinths)
            {
                var captured = skin;
                var img = BuildRow(skin.name, "by " + skin.author, skin.file == activeFile, new Action(() =>
                {
                    PlinthApp?.ApplyPlinthFromSource(captured, new Action<string>(SetStatus));
                    SetStatus($"Applying {captured.name}...");
                }));

                string key = (string.IsNullOrEmpty(skin.sourceRepo) ? "" : skin.sourceRepo) + "|" + skin.file;
                _ugcCoverImgs[key] = img;

                if (catalog.TryGetCover(skin, out var tex) && tex != null) ApplyCover(img, tex);
                else
                {
                    catalog.EnsureCover(skin, true);
                    UGUIShip.CreateStretchLabel(img.transform, "No Preview", FS_SM, HINT);
                }
            }

            SetStatus($"{plinths.Count} UGC plinth(s).");
        }

        Image BuildRow(string title, string sub, bool isActive, Action onSelect)
        {
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(_listContent, false);
            var rowRt = rowGo.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, ROW_H);
            rowGo.AddComponent<Image>().color = ITEM_BG;

            var gradGo = new GameObject("SelGradient");
            gradGo.transform.SetParent(rowGo.transform, false);
            var gradRt = gradGo.AddComponent<RectTransform>();
            gradRt.anchorMin = Vector2.zero;
            gradRt.anchorMax = Vector2.one;
            gradRt.offsetMin = gradRt.offsetMax = Vector2.zero;
            gradGo.AddComponent<Image>().color = Color.white;
            var grad = gradGo.AddComponent<GradientImage>();
            grad.Vertical = true;
            grad.TopColor = new Color(ORANGE.r, ORANGE.g, ORANGE.b, 0f);
            grad.BottomColor = new Color(ORANGE.r, ORANGE.g, ORANGE.b, 0.4f);
            gradGo.SetActive(isActive);

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = PAD;
            hlg.padding = new RectOffset((int)(PAD * 2f), (int)(PAD * 2f), (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            var infoGo = new GameObject("Info");
            infoGo.transform.SetParent(rowGo.transform, false);
            infoGo.AddComponent<RectTransform>();
            var infoLE = infoGo.AddComponent<LayoutElement>();
            infoLE.preferredWidth = 100f * UIScale.S;
            infoLE.flexibleWidth = 1f;
            var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVlg.childForceExpandHeight = false;
            infoVlg.childForceExpandWidth = true;
            infoVlg.spacing = 0f;
            infoVlg.padding = new RectOffset(0, 0, (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            UGUIShip.CreateFlowLabel(infoGo.transform, title, FS, WHITE);
            UGUIShip.CreateFlowLabel(infoGo.transform, sub, FS_SM, HINT);

            var coverGo = new GameObject("Cover");
            coverGo.transform.SetParent(rowGo.transform, false);
            coverGo.AddComponent<RectTransform>();
            var coverLE = coverGo.AddComponent<LayoutElement>();
            coverLE.preferredWidth = COVER_W;
            coverLE.preferredHeight = COVER_H;
            coverLE.minWidth = COVER_W;
            var coverImg = coverGo.AddComponent<Image>();
            coverImg.color = COVER_BG;

            var selBtn = UGUIShip.CreateButton(rowGo.transform, "Select",
                BTN_DARK, isActive ? ORANGE : WHITE, FS_SM, onSelect);
            var selLE = selBtn.gameObject.AddComponent<LayoutElement>();
            selLE.preferredWidth = SEL_W;
            selLE.minWidth = SEL_W;
            selLE.preferredHeight = ROW_H - PAD;

            return coverImg;
        }

        static void ApplyCover(Image img, Texture2D tex)
        {
            for (int i = img.transform.childCount - 1; i >= 0; i--)
                Destroy(img.transform.GetChild(i).gameObject);
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            img.preserveAspect = true;
            img.color = WHITE;
        }
    }
}
