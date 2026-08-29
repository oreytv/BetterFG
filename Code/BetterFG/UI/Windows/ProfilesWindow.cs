using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Profiles;
using UnityEngine;
using UnityEngine.UI;
using BettrFG.uGUI;

namespace BetterFG.UI.Windows
{
    // Player profiles: save your current customization tagged with a player name; when that name
    // shows up in-round (clean name, you or remote) the bean gets that look. Rows show the player
    // name + an enabled toggle + delete. Save row makes one from your current setup. Import (button
    // or drop a .bfgprofile onto the drop bar) pulls a shared profile in.
    public class ProfilesWindow : BetterFGWindow
    {
        public ProfilesWindow(IntPtr ptr) : base(ptr) { }

        // live instance so external callers (drag-drop import) can refresh the row list
        private static ProfilesWindow _instance;
        void OnEnable() => _instance = this;
        void OnDisable() { if (_instance == this) _instance = null; }
        public static void RefreshOpen() => _instance?.RebuildContent();

        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 160f;
        protected override string WindowTitle => "Profiles";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";

        private const float ROW_H = 22f;
        private const float BTN_H = 16f;
        private const float ROW_PAD = 6f;
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color BTN_DEL = new Color(0.7f, 0.3f, 0.3f, 1f);
        private static readonly Color BTN_SAVE = new Color(0.25f, 0.45f, 0.75f, 1f);
        private static readonly Color BTN_IMPORT = new Color(0.3f, 0.55f, 0.4f, 1f);
        private static readonly Color ON_COL = new Color(0.3f, 0.6f, 0.35f, 1f);
        private static readonly Color OFF_COL = new Color(0.4f, 0.4f, 0.4f, 1f);

        private ScrollRect _scroll;
        private float _scrollPos = 1f;

        private void RebuildKeepingScroll()
        {
            if (_scroll != null) _scrollPos = _scroll.verticalNormalizedPosition;
            RebuildContent();
            StartCoroutine(RestoreScroll().WrapToIl2Cpp());
        }

        private IEnumerator RestoreScroll()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (_scroll != null) _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scrollPos);
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(148.7123f, 50.0206f, 0f);
            BgScale = new Vector3(1.2833f, 4.3332f, 1f);
            ContentPosition = new Vector3(138.3868f, -16.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(27.8546f, -20.2182f, 0);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H));
            _scroll = scroll.scrollRect;
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = scrollRt.offsetMax = Vector2.zero;

            var listRt = scroll.content;
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            listRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildSaveRow(listRt);
            BuildImportRow(listRt);

            var names = ProfileService.List();
            for (int i = 0; i < names.Count; i++)
                BuildProfileRow(listRt, names[i], i % 2 == 0 ? ROW_EVEN : ROW_ODD);
        }

        // export your current setup to a shareable file. the profile auto-captures YOUR player name
        // as its match key, so whoever imports it sees it on you. does NOT join the list below —
        // that list is for OTHER players' profiles you import.
        private void BuildSaveRow(RectTransform parent)
        {
            var rowGo = new GameObject("Row_Save");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = new Color(0.2f, 0.3f, 0.45f, 0.18f);

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(ROW_PAD + 20f, 0f, 150f, ROW_H),
                "export my setup to a file", 12, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);

            var btnGo = new GameObject("ExportBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-ROW_PAD, 0f);
            btnRt.sizeDelta = new Vector2(54f, BTN_H);

            UGUIShip.CreateButton(btnGo.transform, new Rect(0f, 0f, 54f, BTN_H), "EXPORT", BTN_SAVE, Color.white, 9)
                .onClick.AddListener(new Action(() =>
                    WinDialogs.SaveFile("Export profile", "bfgprofile", ProfileService.LocalPlayerName(), path =>
                    {
                        if (string.IsNullOrEmpty(path)) return;
                        ProfileService.SaveCurrentToPath(path);
                    })));
        }

        // drop-a-file bar + IMPORT button (both open the picker; native OS drop is a later add)
        private void BuildImportRow(RectTransform parent)
        {
            var rowGo = new GameObject("Row_Import");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = new Color(0.2f, 0.4f, 0.3f, 0.14f);

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(ROW_PAD + 20f, 0f, 150f, ROW_H),
                "drop profile here →", 12, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);

            var btnGo = new GameObject("ImportBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-ROW_PAD, 0f);
            btnRt.sizeDelta = new Vector2(60f, BTN_H);

            UGUIShip.CreateButton(btnGo.transform, new Rect(0f, 0f, 60f, BTN_H), "IMPORT", BTN_IMPORT, Color.white, 9)
                .onClick.AddListener(new Action(() =>
                    WinDialogs.PickFile("Select a .bfgprofile", path =>
                    {
                        if (string.IsNullOrEmpty(path)) return;
                        ProfileService.Import(path);
                        RebuildContent();
                    })));
        }

        private void BuildProfileRow(RectTransform parent, string name, Color bg)
        {
            var rowGo = new GameObject("Row_" + name);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            UGUIShip.PaintStaticRowFill(rowGo, bg);

            // name label positioned the same as PresetsWindow rows
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(ROW_PAD + 20f, 0f, 150f, ROW_H),
                name, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            float right = ROW_PAD;
            AddRowButton(rowGo, "DEL", 34f, ref right, BTN_DEL, () => { ProfileService.Delete(name); RebuildKeepingScroll(); });

            bool on = ProfileService.IsEnabled(name);
            AddRowButton(rowGo, on ? "ON" : "OFF", 38f, ref right, on ? ON_COL : OFF_COL, () =>
            {
                ProfileService.SetEnabled(name, !ProfileService.IsEnabled(name));
                RebuildKeepingScroll();
            });
        }

        private void AddRowButton(GameObject row, string label, float w, ref float rightOffset, Color col, Action onClick)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(row.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-rightOffset, 0f);
            rt.sizeDelta = new Vector2(w, BTN_H);
            rightOffset += w + 3f;

            UGUIShip.CreateButton(go.transform, new Rect(0f, 0f, w, BTN_H), label, col, Color.white, 9)
                .onClick.AddListener(new Action(() => onClick?.Invoke()));
        }
    }
}
