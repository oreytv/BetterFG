using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.UI.Components;
using BetterFG.Utilities;
using BetterFG.UI.Windows;
using BettrFG.uGUI;
using UnityEngine;

namespace BetterFG.Features.Onboarding
{
    // "first time you open X" help. offers a rundown, and if you take it, pages through what the
    // thing is for, pointing the pulse ring at a control when a page is about one. every page
    // advances on Next; nothing here makes you click the real buttons.
    //
    // dismissing one writes "seen.<id>", so each guide only ever shows once.
    public class HelpPrompt : MonoBehaviour
    {
        public HelpPrompt(IntPtr ptr) : base(ptr) { }

        private const int CanvasOrder = 1400;
        private const int BlockerOrder = 1399;

        private static HelpPrompt _live;

        private string _id;
        private string _titleId;
        private string _askId;
        private Page[] _pages;
        private int _page = -1;

        private Canvas _canvas;
        private Canvas _blockerCanvas;
        private OnboardingBlocker _blocker;
        private GameObject _popup;
        private GameObject _pulse;

        private struct Page
        {
            public string BodyId;
            public string TargetKey; // null = no control to point at, popup sits centred
            public Action Do;        // runs as the page opens, drive the real UI, don't just describe it
            public float Settle;     // seconds to let Do's animation land before the popup is placed
            public bool Box;         // outline the target rect instead of ringing its centre
            public Page(string bodyId, string targetKey = null, Action doIt = null, float settle = 0f, bool box = false)
            { BodyId = bodyId; TargetKey = targetKey; Do = doIt; Settle = settle; Box = box; }
        }

        private struct Entry
        {
            public string Id;
            public string TitleId;
            public string AskId;
            public Page[] Pages;
        }

        // what each surface explains about itself, keyed by the sidewheel entry's locId
        private static readonly Dictionary<string, Entry> ForWheelEntry = new Dictionary<string, Entry>
        {
            ["sidewheel.profiles"] = new Entry
            {
                Id = "profileswindow",
                TitleId = "help.profiles.title",
                AskId = "help.profiles.ask",
                Pages = new[]
                {
                    new Page("help.profiles.p1"),
                    new Page("help.profiles.p2", "profiles.export"),
                    new Page("help.profiles.p3", "profiles.import"),
                    new Page("help.profiles.p4"),
                },
            },
            ["sidewheel.menu_music"] = new Entry
            {
                Id = "menumusicwindow",
                TitleId = "help.menumusic.title",
                AskId = "help.menumusic.ask",
                Pages = new[]
                {
                    new Page("help.menumusic.p1"),
                    new Page("help.menumusic.p2", "menumusic.enabled"),
                    new Page("help.menumusic.p3", "menumusic.refresh"),
                    new Page("help.menumusic.p4", "menumusic.track"),
                },
            },
        };

        // swap the open slot over to another UI section, the same call its own section bar makes
        private static void GoToSection<T>() where T : UI.Tab
        {
            var uim = UI.BetterFGUIMan.Instance;
            var cur = uim?.OpenTab;
            if (cur != null) uim.SwitchSlotTab(cur, UI.BetterFGTabRegistry.NewTab<T>());
        }

        private static RectTransform SectionButton(int i)
        {
            var rects = UI.Tabs.UITab.SectionButtonRects;
            return (rects != null && i < rects.Length) ? rects[i] : null;
        }

        private static RectTransform ResolveTarget(string key)
        {
            switch (key)
            {
#if PROFILES
                case "profiles.export": return ProfilesWindow.ExportRect;
                case "profiles.import": return ProfilesWindow.ImportRect;
#endif
                case "menumusic.enabled": return MenuMusicWindow.EnabledToggleRect;
                case "menumusic.refresh": return MenuMusicWindow.RefreshRect;
                case "menumusic.track": return MenuMusicWindow.FirstTrackRect;
                case "uitab.sections": return UI.Tabs.UITab.SectionBarRect;
                case "uitab.carousel": return UI.Tabs.UITab.CarouselRect;
                case "uitab.preview": return UI.Tabs.UITab.PreviewRect;
                case "uitab.edit": return UI.Tabs.UITab.EditRect;
                case "uitab.section.0": return SectionButton(0);
                case "uitab.section.1": return SectionButton(1);
                case "uitab.section.2": return SectionButton(2);
                case "uitab.section.3": return SectionButton(3);
                default: return null;
            }
        }

        // what each tab explains about itself, keyed by Tab.TabTitle
        private static readonly Dictionary<string, Entry> ForTab = new Dictionary<string, Entry>
        {
            ["User Interface"] = new Entry
            {
                Id = "uitab",
                TitleId = "help.uitab.title",
                AskId = "help.uitab.ask",
                Pages = new[]
                {
                    new Page("help.uitab.p1"),
                    // runs the carousel so the previews visibly change while you read about them
                    new Page("help.uitab.p2", "uitab.preview", () => UI.Tabs.UITab.Live?.DemoCarousel(), box: true),
                    new Page("help.uitab.p3", "uitab.edit", box: true),
                    // then walk the sections for real, each page IS that section, and boxes the
                    // button that got you there rather than the middle of the whole bar
                    new Page("help.uitab.p4", "uitab.section.1", () => GoToSection<UI.Tabs.UIFontTab>(), 0.45f, true),
                    new Page("help.uitab.p5", "uitab.section.2", () => GoToSection<UI.Tabs.UIBackgroundTab>(), 0.45f, true),
                    new Page("help.uitab.p6", "uitab.section.3", () => GoToSection<UI.Tabs.UIScalingTab>(), 0.45f, true),
                    // this page is about the bar itself, so box the whole bar, not one button
                    new Page("help.uitab.p7", "uitab.sections", () => GoToSection<UI.Tabs.UITab>(), 0.45f, true),
                },
            },
        };

        public static void OfferForWheelEntry(string locId)
        {
            if (locId != null && ForWheelEntry.TryGetValue(locId, out var e)) Offer(e);
        }

        public static void OfferForTab(string tabTitle)
        {
            if (tabTitle != null && ForTab.TryGetValue(tabTitle, out var e)) Offer(e);
        }

        private static void Offer(Entry e)
        {
            if (_live != null) return;
            if (SettingsService.Get("seen." + e.Id, "false") == "true") return;

            var go = new GameObject("BetterFG_HelpPrompt");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            var p = go.AddComponent<HelpPrompt>();
            p._id = e.Id;
            p._titleId = e.TitleId;
            p._askId = e.AskId;
            p._pages = e.Pages;
            _live = p;
            p.StartCoroutine(p.BuildAfterOpenAnim().WrapToIl2Cpp());
        }

        // the tab slide (0.3s) and the wheel window's roll-out (0.18s) both move the controls we
        // point at, so let whichever one is running settle before measuring anything
        private IEnumerator BuildAfterOpenAnim()
        {
            yield return new WaitForSecondsRealtime(0.4f);
            Build();
        }

        private void Build()
        {
            _blockerCanvas = UGUIShip.CreateCanvas("BetterFG_HelpBlocker");
            _blockerCanvas.sortingOrder = BlockerOrder;
            _blocker = new OnboardingBlocker(_blockerCanvas);
            _blocker.SetHole(null);

            _canvas = UGUIShip.CreateCanvas("BetterFG_HelpCanvas");
            _canvas.sortingOrder = CanvasOrder;

            ShowAsk();
        }

        private void ShowAsk()
        {
            var (pos, pivot) = Place(null);
            Swap(OnboardingPopup.Show(_canvas, _titleId, _askId, pos, pivot,
                new[]
                {
                    new OnboardingPopup.ButtonSpec("help.btn.no", new Action(Dismiss), OnboardingPopup.BtnGhost),
                    new OnboardingPopup.ButtonSpec("help.btn.yes", new Action(NextPage), OnboardingPopup.BtnPrimary),
                }));
        }

        // every popup clears the open tab's column, whether or not it's pointing at something inside
        // it, a centred one lands right on top of the tab. `box` only steers the height it sits at.
        private (Vector2 pos, Vector2 pivot) Place(Rect? box)
        {
            var host = HostPanelAabb();
            if (host == null)
                return box == null
                    ? (Vector2.zero, new Vector2(0.5f, 0.5f))
                    : (CanvasGeom.Beside(box.Value), new Vector2(0f, 0.5f));

            float y = box?.center.y ?? host.Value.center.y;
            return (CanvasGeom.Beside(Rect.MinMaxRect(host.Value.xMin, y, host.Value.xMax, y)),
                    new Vector2(0f, 0.5f));
        }

        private void NextPage()
        {
            _page++;
            if (_page >= _pages.Length) { Dismiss(); return; }

            ClearPulse();
            var page = _pages[_page];

            if (page.Do != null)
            {
                page.Do();
                // a page that navigates has to let the slide finish, or the popup is placed against
                // a tab that's still moving and the pulse latches onto a rect about to be destroyed
                if (page.Settle > 0f)
                {
                    if (_popup != null) { StartCoroutine(CloseThenDestroy(_popup).WrapToIl2Cpp()); _popup = null; }
                    StartCoroutine(ShowPageAfter(page, _page, page.Settle).WrapToIl2Cpp());
                    return;
                }
            }
            ShowPage(page);
        }

        private IEnumerator ShowPageAfter(Page page, int forPage, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (_live != this || _page != forPage) yield break;
            ShowPage(page);
        }

        private void ShowPage(Page page)
        {
            bool last = _page == _pages.Length - 1;

            Rect? box = null;
            var target = ResolveTarget(page.TargetKey);
            if (target != null)
            {
                var b = CanvasGeom.AabbOf(_canvas, target, 4f);
                SpawnPulse(b.center, Mathf.Max(60f, b.height * 2.6f), target, page.Box);
                box = b;
            }

            var (pos, pivot) = Place(box);
            Swap(OnboardingPopup.Show(_canvas, _titleId, page.BodyId, pos, pivot,
                new[]
                {
                    new OnboardingPopup.ButtonSpec(last ? "help.btn.done" : "help.btn.next",
                        new Action(NextPage), OnboardingPopup.BtnPrimary),
                }));
        }

        // the open tab's column, when the thing we're pointing at lives inside one
        private Rect? HostPanelAabb()
        {
            var root = UI.BetterFGUIMan.Instance?.OpenTabRoot;
            return root != null ? CanvasGeom.AabbOf(_canvas, root, 0f) : (Rect?)null;
        }

        private void SpawnPulse(Vector2 canvasPos, float size, RectTransform follow, bool box = false)
        {
            var go = new GameObject("Help_Pulse");
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(1f, 1f);
            rt.anchoredPosition = canvasPos;
            if (box)
            {
                var b = go.AddComponent<HollowBoxPulse>();
                b.Follow = follow;
                b.HostCanvas = _canvas;
            }
            else
            {
                var c = go.AddComponent<HollowCirclePulse>();
                c.BaseSize = size;
                c.Follow = follow;
                c.HostCanvas = _canvas;
            }
            _pulse = go;
        }

        private void ClearPulse()
        {
            if (_pulse != null) { UnityEngine.Object.Destroy(_pulse); _pulse = null; }
        }

        private void Swap(GameObject next)
        {
            if (_popup != null) StartCoroutine(CloseThenDestroy(_popup).WrapToIl2Cpp());
            _popup = next;
            StartCoroutine(OpenPopup(next).WrapToIl2Cpp());
        }

        private static IEnumerator CloseThenDestroy(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            float e = 0f;
            while (e < OnboardingPopup.AnimDur)
            {
                e += Time.unscaledDeltaTime;
                rt.localScale = new Vector3(1f - Mathf.Clamp01(e / OnboardingPopup.AnimDur), 1f, 1f);
                yield return null;
            }
            UnityEngine.Object.Destroy(go);
        }

        private static IEnumerator OpenPopup(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            float e = 0f;
            while (e < OnboardingPopup.AnimDur)
            {
                e += Time.unscaledDeltaTime;
                rt.localScale = new Vector3(
                    OnboardingPopup.OpenCurve.Evaluate(Mathf.Clamp01(e / OnboardingPopup.AnimDur)), 1f, 1f);
                yield return null;
            }
            rt.localScale = Vector3.one;
        }

        private void Dismiss()
        {
            SettingsService.Set("seen." + _id, "true");
            StartCoroutine(TearDown().WrapToIl2Cpp());
        }

        private IEnumerator TearDown()
        {
            ClearPulse();
            if (_popup != null)
            {
                yield return CloseThenDestroy(_popup);
                _popup = null;
            }
            if (_canvas != null) UnityEngine.Object.Destroy(_canvas.gameObject);
            if (_blockerCanvas != null) UnityEngine.Object.Destroy(_blockerCanvas.gameObject);
            if (_live == this) _live = null;
            UnityEngine.Object.Destroy(gameObject);
        }
    }
}
