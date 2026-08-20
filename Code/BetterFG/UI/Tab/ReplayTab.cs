using System;
using UnityEngine;
using UnityEngine.UI;
using BetterFG.Utilities;

namespace BetterFG.UI.Tab
{
    public class ReplayTab : BetterFGTab
    {
        public ReplayTab(IntPtr ptr) : base(ptr) { }

        protected static float PAD => UIScale.PAD;
        protected static float SH => UIScale.SH;
        protected static float BTN_H => UIScale.BTN_H;
        protected static int FS => UIScale.FS;
        protected static int FS_SM => UIScale.FS_SM;

        protected const float HEADER_H = 20f;

        protected static readonly Color GREEN = new Color(0.22f, 0.42f, 0.26f, 1f);
        protected static readonly Color DARK = new Color(0.18f, 0.18f, 0.2f, 1f);
        protected static readonly Color ROW_ALT = new Color(1f, 1f, 1f, 0.03f);
        protected static readonly Color ROW_CLEAR = new Color(0f, 0f, 0f, 0f);
        protected static readonly Color ROW_HOVER = new Color(1f, 1f, 1f, 0.13f);
        protected static readonly Color ROW_PRESS = new Color(1f, 1f, 1f, 0.2f);
        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        protected static readonly Color DIM = new Color(1f, 1f, 1f, 0.4f);
        protected static readonly Color UGC_COL = new Color(0.75f, 0.9f, 1f);
        protected static readonly Color LINK = new Color(1f, 0.72f, 0.35f, 0.85f);

        GameObject _bgHoverGo;
        Text _titleText, _switchLink;
        Text _statusLbl;
        Button _prevBtn, _nextBtn;

        protected RectTransform ListContent;
        protected int Page;

        protected virtual string SwitchLabel => "";
        protected virtual BetterFGTab MakeSwitchTarget() => null;

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.tab.replay.png");
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(0f, 0f);
            bgRt.offsetMax = new Vector2(0f, 1f);
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0f);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.bg_hover.png");
            if (hoverTex == null) return;
            _bgHoverGo = new GameObject("BG_Hover");
            _bgHoverGo.transform.SetParent(bgGo.transform, false);
            var hoverRt = _bgHoverGo.AddComponent<RectTransform>();
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
            _bgHoverGo.AddComponent<RawImage>().texture = hoverTex;
            _bgHoverGo.SetActive(false);
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        protected override void BuildTitleExtras(Transform titleBar, Text title)
        {
            _titleText = title;
            _switchLink = UGUIShip.CreateLinkText(titleBar, new Rect(0f, 0f, 90f, TITLE_H), SwitchLabel,
                new Action(SwitchView), LINK, FS_SM);
            _switchLink.gameObject.SetActive(IsOpen);
        }

        void SwitchView() => BetterFGUIMan.Instance?.SwitchSlotTab(this, MakeSwitchTarget());

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
            float w = TabWidth - PAD * 2f;
            float y = BuildHeader(contentRoot, PAD, w);

            float statusH = UIScale.LH;
            float statusY = TabHeight - PAD - statusH;

            var (_, content) = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, statusY - SH - y));
            ListContent = content;
            BuildListLayout(content);

            float pageBtnW = 52f;
            _prevBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD, statusY, pageBtnW, statusH), "‹ Prev",
                DARK, Color.white, FS_SM, new Action(() => { Page--; RenderPage(); }));
            _nextBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - pageBtnW, statusY, pageBtnW, statusH), "Next ›",
                DARK, Color.white, FS_SM, new Action(() => { Page++; RenderPage(); }));
            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD + pageBtnW, statusY, w - pageBtnW * 2f, statusH),
                "", FS_SM, HINT, TextAnchor.MiddleCenter);

            PositionSwitchLink();
            Refresh();
        }

        protected virtual float BuildHeader(RectTransform root, float y, float w) => y;
        protected virtual void BuildListLayout(RectTransform content) { }
        protected virtual void Refresh() { }
        protected virtual void RenderPage() { }

        protected void SetStatus(string msg)
        {
            if (_statusLbl != null) _statusLbl.text = msg;
        }

        protected void ShowPaging(bool on)
        {
            _prevBtn.gameObject.SetActive(on);
            _nextBtn.gameObject.SetActive(on);
        }

        protected void ClearList()
        {
            for (int i = ListContent.childCount - 1; i >= 0; i--)
                Destroy(ListContent.GetChild(i).gameObject);
        }
    }
}
