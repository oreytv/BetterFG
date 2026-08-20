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

        protected static float PAD => UIScale.PAD;
        protected static float SH => UIScale.SH;
        protected static float BTN_H => UIScale.BTN_H;
        protected static float ROW_H => UIScale.ROW_H;
        protected static float COVER_W => UIScale.COVER_W;
        protected static float COVER_H => UIScale.COVER_H;
        protected static float SEL_W => UIScale.SEL_W;
        protected static int FS => UIScale.FS;
        protected static int FS_SM => UIScale.FS_SM;

        protected static readonly Color WHITE = Color.white;
        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        protected static readonly Color BTN_DARK = new Color(0.18f, 0.18f, 0.18f, 1f);
        protected static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        protected static readonly Color COVER_BG = new Color(0.04f, 0.04f, 0.04f, 1f);
        protected static readonly Color ITEM_BG = new Color(0f, 0f, 0f, 0f);
        protected static readonly Color ORANGE = new Color(1f, 0.55f, 0.1f, 1f);
        protected static readonly Color LINK = new Color(1f, 0.72f, 0.35f, 0.85f);

        static Texture2D _bgTex, _hoverTex;
        GameObject _bgHoverGo;

        Text _titleText, _switchLink;
        RectTransform _scrollRt;
        Text _statusLbl;

        protected RectTransform ContentRoot;
        protected RectTransform ListContent;
        protected float ListW;

        protected static MenuCustomizationApplication PlinthApp => CustomizationServices.PlinthApp;
        protected static SkinCatalogService Catalog => CustomizationServices.CatalogService;

        protected virtual string SwitchLabel => "";
        protected virtual BetterFGTab MakeSwitchTarget() => null;

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

        void OnSkinsLoaded(List<SkinInfo> _) => OnCatalogChanged();

        protected virtual void OnCoverLoaded(string key, Texture2D tex) { }
        protected virtual void OnCatalogChanged() { }

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

        protected override void BuildTitleExtras(Transform titleBar, Text title)
        {
            _titleText = title;
            _switchLink = UGUIShip.CreateLinkText(titleBar, new Rect(0f, 0f, 90f, TITLE_H), SwitchLabel,
                new Action(SwitchSource), LINK, FS_SM);
            _switchLink.gameObject.SetActive(IsOpen);
        }

        void SwitchSource() => BetterFGUIMan.Instance?.SwitchSlotTab(this, MakeSwitchTarget());

        public override void OnOpened() => _switchLink.gameObject.SetActive(true);
        public override void OnClosed() => _switchLink.gameObject.SetActive(false);

        void PositionSwitchLink()
        {
            var rt = _switchLink.rectTransform;
            UGUIShip.SetPixelRect(rt, new Rect(_titleText.rectTransform.offsetMin.x + _titleText.preferredWidth + PAD * 1.5f,
                0f, rt.sizeDelta.x, TITLE_H));
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            ContentRoot = contentRoot;
            ListW = TabWidth - PAD * 2f;

            var (scroll, content) = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, PAD, ListW, BTN_H));
            _scrollRt = scroll.GetComponent<RectTransform>();
            ListContent = content;
            var vlg = ListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD * 0.5f;
            vlg.padding = new RectOffset(0, (int)PAD, (int)(PAD * 0.5f), (int)(PAD * 0.5f));
            ListContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            float statusH = UIScale.LH;
            float statusY = TabHeight - PAD - statusH;
            float removeW = 64f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + ListW - removeW, statusY, removeW, statusH), "Remove",
                BTN_REMOVE, WHITE, FS_SM, new Action(OnRemove));
            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, statusY, ListW - removeW - PAD, statusH),
                "", FS_SM, HINT, TextAnchor.MiddleLeft);

            PositionSwitchLink();
            LayoutList();
            Rebuild();
        }

        void LayoutList()
        {
            float y = HeaderY();
            float statusY = TabHeight - PAD - UIScale.LH;
            UGUIShip.SetPixelRect(_scrollRt, new Rect(PAD, y, ListW, statusY - SH - y));
        }

        protected virtual float HeaderY() => PAD;

        public override void OnRepoChanged()
        {
            LayoutList();
            Rebuild();
        }

        protected void SetStatus(string msg)
        {
            if (_statusLbl != null) _statusLbl.text = msg;
        }

        void OnRemove()
        {
            PlinthApp?.RemovePlinth();
            Rebuild();
            SetStatus("Plinth removed.");
        }

        protected void Rebuild()
        {
            if (ListContent == null) return;

            for (int i = ListContent.childCount - 1; i >= 0; i--)
                Destroy(ListContent.GetChild(i).gameObject);

            BuildRows();
        }

        protected virtual void BuildRows() { }

        protected Image BuildRow(string title, string sub, bool isActive, Action onSelect)
        {
            var rowGo = new GameObject("Row");
            rowGo.transform.SetParent(ListContent, false);
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

        protected static void ApplyCover(Image img, Texture2D tex)
        {
            for (int i = img.transform.childCount - 1; i >= 0; i--)
                Destroy(img.transform.GetChild(i).gameObject);
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            img.preserveAspect = true;
            img.color = WHITE;
        }
    }
}
