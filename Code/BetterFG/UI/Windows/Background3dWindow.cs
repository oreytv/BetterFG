using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Tweaks;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class Background3dWindow : PartialWindow
    {
        public Background3dWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 220f;
        protected override string WindowTitle => "3D Backgrounds";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";

        protected override BetterFGWindow MakeBackTarget() => Spawn<TweaksWindow>();

        private const float ROW_H = 22f;
        private const float BTN_W = 44f;
        private const float BTN_H = 16f;
        private const float ROW_PAD = 6f;
        private const float HEADER_H = 18f;
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color DL_COL = new Color(0.25f, 0.45f, 0.75f, 1f);
        private static readonly Color DEL_COL = new Color(0.45f, 0.22f, 0.22f, 1f);

        private RectTransform _listRt;
        private readonly List<string> _barBundles = new List<string>();
        private readonly List<RectTransform> _barFills = new List<RectTransform>();

        protected override void BuildContent(RectTransform contentRoot)
        {
            // same frame numbers as TweaksWindow so backing out of it doesn't shift anything
            BgPosition = new Vector3(139.3993f, 74.9552f, 0f);
            BgScale = new Vector3(1.3415f, 4.5877f, 1f);
            ContentPosition = new Vector3(-1.6132f, -17.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1.04f, 1f);
            ContentOffsetMin = new Vector2(-1.6132f, -25.92f);
            ContentOffsetMax = new Vector2(-1.6132f, -18.72f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(20.0368f, -6.7966f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H));
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = scrollRt.offsetMax = Vector2.zero;

            _listRt = scroll.content;
            var vlg = _listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            var csf = _listRt.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // the seed off SwapFor happens in FetchCatalogue's synchronous head, so by the time
            // BuildRows runs below there's already a list to draw
            StartCoroutine(Background3dTweak.FetchCatalogue(Rebuild).WrapToIl2Cpp());
            BuildRows();
        }

        private void Rebuild()
        {
            if (_listRt == null) return;
            for (int i = _listRt.childCount - 1; i >= 0; i--)
                Destroy(_listRt.GetChild(i).gameObject);
            BuildRows();
        }

        private void BuildRows()
        {
            _barBundles.Clear();
            _barFills.Clear();

            BuildHeader("BACKDROPS");

            int i = 0;
            foreach (var kv in Background3dTweak.Catalogue)
            {
                BuildRow(kv.Key, kv.Value, i % 2 == 0 ? ROW_EVEN : ROW_ODD);
                i++;
            }
        }

        private void BuildHeader(string title)
        {
            var rowGo = new GameObject("Header_" + title);
            rowGo.transform.SetParent(_listRt, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = HEADER_H;
            le.flexibleWidth = 1f;

            var lbl = UGUIShip.CreateLabel(rowGo.transform, new Rect(22f, 0f, 200f, HEADER_H),
                title, 10, Color.white, TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            var rt = lbl.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(22f, 0f);
            rt.localScale = new Vector3(1.3f, 1.3f, 1f);
        }

        private void BuildRow(string label, string bundle, Color bg)
        {
            var rowGo = new GameObject("Row_" + bundle);
            rowGo.transform.SetParent(_listRt, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            // names come off the release, so they can be any length ("Junkyard (credits to
            // BurntApple)"). stretch to whatever's left of the button and let it shrink to fit.
            var nameLbl = UGUIShip.CreateLabel(rowGo.transform, new Rect(0f, 0f, 0f, ROW_H),
                label, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
            nameLbl.resizeTextForBestFit = true;
            nameLbl.resizeTextMinSize = 8;
            nameLbl.resizeTextMaxSize = 13;
            var nameRt = nameLbl.rectTransform;
            nameRt.anchorMin = Vector2.zero;
            nameRt.anchorMax = Vector2.one;
            nameRt.offsetMin = new Vector2(ROW_PAD + 20f, 0f);
            nameRt.offsetMax = new Vector2(-(ROW_PAD + BTN_W + 4f), 0f);

            var btnGo = new GameObject("Dl");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-ROW_PAD, 0f);
            btnRt.sizeDelta = new Vector2(BTN_W, BTN_H);

            var captured = bundle;

            if (File.Exists(Path.Combine(Background3dTweak.BundleDir, bundle)))
            {
                UGUIShip.CreateButton(btnGo.transform, new Rect(0f, 0f, BTN_W, BTN_H),
                    "Delete", DEL_COL, WHITE, 9)
                    .onClick.AddListener(new Action(() =>
                    {
                        Background3dTweak.Delete(captured);
                        Rebuild();
                    }));
                return;
            }

            bool busy = Background3dTweak.Downloading.ContainsKey(bundle);
            var btn = UGUIShip.CreateButton(btnGo.transform, new Rect(0f, 0f, BTN_W, BTN_H),
                busy ? "..." : "Get", DL_COL, WHITE, 9);
            btn.interactable = !busy;
            btn.onClick.AddListener(new Action(() =>
            {
                SettingsService.Set(Background3dTweak.ASKED_KEY, "true");
                StartCoroutine(Grab(captured).WrapToIl2Cpp());
                Rebuild();
            }));

            if (!busy) return;

            // hairline track along the bottom of the row, fill runs left to right. it only exists
            // while the bundle's in flight — the rebuild on completion takes it away
            var trackGo = new GameObject("Bar");
            trackGo.transform.SetParent(rowGo.transform, false);
            var trackRt = trackGo.AddComponent<RectTransform>();
            trackRt.anchorMin = new Vector2(0f, 0f);
            trackRt.anchorMax = new Vector2(1f, 0f);
            trackRt.pivot = new Vector2(0.5f, 0f);
            trackRt.offsetMin = Vector2.zero;
            trackRt.offsetMax = new Vector2(0f, 2f);
            trackGo.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(trackGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            fillGo.AddComponent<Image>().color = DL_COL;

            _barBundles.Add(bundle);
            _barFills.Add(fillRt);
        }

        private IEnumerator Grab(string bundle)
        {
            yield return Background3dTweak.Fetch(bundle).WrapToIl2Cpp();
            Rebuild();
            Background3dTweak.ApplyIfWanted();
        }

        private int _seq = -1;

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();

            if (_seq != Background3dTweak.DownloadSeq)
            {
                _seq = Background3dTweak.DownloadSeq;
                Rebuild();
            }

            for (int i = 0; i < _barBundles.Count; i++)
            {
                var fill = _barFills[i];
                if (fill == null) continue;
                Background3dTweak.Downloading.TryGetValue(_barBundles[i], out float p);
                fill.anchorMax = new Vector2(Mathf.Clamp01(p), 1f);
            }
        }
    }
}
