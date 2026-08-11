using System;
using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Customization.Player;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using FGClient;

namespace BetterFG.Customization.Menu
{
    public partial class MenuCustomizationApplication
    {
        // foreground keys — shared with MenuTab
        public const string KEY_FG_CYAN_ON = "menu.fg.cyan.on";
        public const string KEY_FG_CYAN_R = "menu.fg.cyan.r";
        public const string KEY_FG_CYAN_G = "menu.fg.cyan.g";
        public const string KEY_FG_CYAN_B = "menu.fg.cyan.b";
        public const string KEY_FG_BLACK_ON = "menu.fg.black.on";
        public const string KEY_FG_BLACK_R = "menu.fg.black.r";
        public const string KEY_FG_BLACK_G = "menu.fg.black.g";
        public const string KEY_FG_BLACK_B = "menu.fg.black.b";
        public const string KEY_FG_YELLOW_ON = "menu.fg.yellow.on";
        public const string KEY_FG_YELLOW_R = "menu.fg.yellow.r";
        public const string KEY_FG_YELLOW_G = "menu.fg.yellow.g";
        public const string KEY_FG_YELLOW_B = "menu.fg.yellow.b";
        public const string KEY_FG_BLUE_ON = "menu.fg.blue.on";
        public const string KEY_FG_BLUE_R = "menu.fg.blue.r";
        public const string KEY_FG_BLUE_G = "menu.fg.blue.g";
        public const string KEY_FG_BLUE_B = "menu.fg.blue.b";
        public const string KEY_FG_PINK_ON = "menu.fg.pink.on";
        public const string KEY_FG_PINK_R = "menu.fg.pink.r";
        public const string KEY_FG_PINK_G = "menu.fg.pink.g";
        public const string KEY_FG_PINK_B = "menu.fg.pink.b";
        public const string KEY_FG_ORANGE_ON = "menu.fg.orange.on";
        public const string KEY_FG_ORANGE_R = "menu.fg.orange.r";
        public const string KEY_FG_ORANGE_G = "menu.fg.orange.g";
        public const string KEY_FG_ORANGE_B = "menu.fg.orange.b";

        // cache: instanceID → original color, so we can revert
        private readonly Dictionary<int, Color> _fgOriginals = new Dictionary<int, Color>();
        // parallel ref cache so RevertForeground doesn't have to GetComponentsInChildren the
        // entire UICanvas just to find the ~dozens of images we actually touched. id -> Image.
        private readonly Dictionary<int, UnityEngine.UI.Image> _fgTouchedImages = new Dictionary<int, UnityEngine.UI.Image>();
        private readonly Dictionary<int, UnityEngine.Sprite> _showSelectOriginalSprites = new Dictionary<int, UnityEngine.Sprite>();

        public static IEnumerator ReapplyForegroundFromSettingsCoroutine(Transform scopeRoot = null, bool anyImage = false)
        {
            yield return null;
            yield return new WaitForSeconds(0.01f);
            Instance.ReapplyForegroundFromSettings(scopeRoot, anyImage: anyImage);
        }

        public void ReapplyForegroundFromSettings(Transform scopeRoot = null, string excludeSubtreeName = null, bool anyImage = false, bool refreshOriginals = false)
        {
            bool fullCanvas = scopeRoot == null;

            var pal = FgPalette.FromSettings();
            if (pal.AnyOn)
                ApplyForeground(pal, scopeRoot, excludeSubtreeName, anyImage, refreshOriginals);

            if (fullCanvas)
                ApplyKnownSpecialForegroundScreens();
        }

        private static string FullPath(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new System.Text.StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        // ── Foreground ────────────────────────────────────────────────────────

        private struct FgPalette
        {
            public bool CyanOn, BlueOn, BlackOn, YellowOn, PinkOn, OrangeOn;
            public Color Cyan, Blue, Black, Yellow, Pink, Orange;

            public bool AnyOn => CyanOn || BlueOn || BlackOn || YellowOn || PinkOn || OrangeOn;

            public static FgPalette FromSettings() => new FgPalette
            {
                CyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true", Cyan = CyanReplacement(),
                BlueOn = SettingsService.Get(KEY_FG_BLUE_ON, "false") == "true", Blue = BlueReplacement(),
                BlackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true", Black = BlackReplacement(),
                YellowOn = SettingsService.Get(KEY_FG_YELLOW_ON, "false") == "true", Yellow = YellowReplacement(),
                PinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true", Pink = PinkReplacement(),
                OrangeOn = SettingsService.Get(KEY_FG_ORANGE_ON, "false") == "true", Orange = OrangeReplacement(),
            };

            // orange sits between pink and yellow, so band order decides who eats it
            public bool TryRetint(Color c, out Color target)
            {
                Color.RGBToHSV(c, out float h, out float s, out float v);
                if (BlueOn && s > 0.3f && h > 0.58f && h <= 0.70f) target = Blue;
                else if (CyanOn && s > 0.3f && h >= 0.47f && h <= 0.58f) target = Cyan;
                else if (PinkOn && s > 0.3f && (h >= 0.88f || h <= 0.05f) && v > 0.3f) target = Pink;
                else if (OrangeOn && s > 0.3f && h > 0.05f && h < 0.1f && v > 0.3f) target = Orange;
                else if (YellowOn && s > 0.3f && h >= 0.1f && h <= 0.2f) target = Yellow;
                else if (BlackOn && v < 0.15f && s < 0.15f) target = Black;
                else { target = c; return false; }
                return true;
            }
        }

        private void ApplyForeground(FgPalette pal, Transform scopeRoot = null, string excludeSubtreeName = null, bool anyImage = false, bool refreshOriginals = false)
        {
            if (scopeRoot == null)
            {
                RevertForeground();
                var uiCanvas = GameObject.Find("UICanvas_Client_V2(Clone)");
                if (uiCanvas == null) return;
                scopeRoot = uiCanvas.transform;
            }

            var images = scopeRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            // excludeSubtreeName may be several names joined by '|' (e.g. the creative bg canvas plus
            // the options screen's Description block); anything under one of them is left alone.
            string[] excludes = excludeSubtreeName == null ? null : excludeSubtreeName.Split('|');

            foreach (var img in images)
            {
                if (img == null) continue;
                var p = img.transform;
                bool inStarPopup = false;
                bool inExcluded = false;
                bool inTeamContainer = false;
                bool inPhraseOverlay = false;
                bool inTileFolder = false;
                bool inRadialItem = false;
                while (p != null)
                {
                    if (p.name.StartsWith("StarPopup_")) inStarPopup = true;
                    if (p.name == "TeamContainer") inTeamContainer = true;
                    if (p.name == "TabContentSocialPhraseOverlay") inPhraseOverlay = true;
                    if (p.name == "Folder Items" || p.name == "Carousel_Items") inTileFolder = true;
                    if (p.name.StartsWith("CustomiserSubMenu")) inRadialItem = true;
                    if (excludes != null) foreach (var ex in excludes) if (p.name == ex) { inExcluded = true; break; }
                    p = p.parent;
                }
                if (inStarPopup || inExcluded || inTeamContainer || inPhraseOverlay) continue;
                string n = img.gameObject.name;
                // radial fills belong to RetintRadialItems; sweeping them fought the toggle state and
                // re-cached our own output as the original, double-tinting on the next pass
                if (inRadialItem && n == "Background_Fill") continue;
                // carousel/folder tiles are recoloured by name in ApplyFolderTileColours (Fill->blue,
                // Selected->cyan); the hue sweep mis-buckets Background_Fill as cyan, so leave both to it.
                // scoped to the tile subtrees ApplyFolderTileColours actually runs on — skipping the name
                // globally also killed the private lobby player list's Background_Fill, which nothing else paints.
                if (!anyImage && inTileFolder && (n == "Background_Fill" || n == "Background_Selected")) continue;
                bool isFill = n.Contains("Fill") || n.Contains("Background") || n.Contains("Outline") || n.Contains("Inline") || n.Contains("BG");
                bool isCrowns = n.Equals("crowns");
                bool isBacking = n.Contains("Backing");
                // level-editor screens (anyImage) recolour every hue-matching image, not just the
                // menu's Fill/Backing/crowns-named ones — their controls are named Slider/Overlay/etc.
                if (!anyImage && !isFill && !isBacking && !isCrowns) continue;

                int id = img.GetInstanceID();
                // refreshOriginals: the game just repainted this image to a new state colour (radial
                // select/deselect/disable), so the live colour is the real source hue, not the stale cache.
                Color c = (!refreshOriginals && _fgOriginals.TryGetValue(id, out var cachedOrig)) ? cachedOrig : img.color;

                var hues = pal;
                if (isBacking && !isFill) hues.BlackOn = hues.YellowOn = false;
                if (isCrowns && pal.BlueOn) hues.Cyan = pal.Blue; // crowns follow cyan, but want the blue colour
                if (!hues.TryRetint(c, out Color target)) continue;

                if (refreshOriginals || !_fgOriginals.ContainsKey(id))
                {
                    _fgOriginals[id] = c;
                    _fgTouchedImages[id] = img;
                }

                img.color = new Color(target.r, target.g, target.b, img.color.a);
            }

            ApplyMainPlayButtonUnselectedFill(scopeRoot, pal.YellowOn, pal.Yellow);
        }

        // level-editor variation folder: recolour by name only. Background_Fill -> blue, Background_Selected
        // -> cyan. covers the disabled tile states too (GetComponentsInChildren(true)); originals tracked so
        // RevertForeground puts them back.
        public void ApplyFolderTileColours(Transform folderRoot)
        {
            bool blueOn = SettingsService.Get(KEY_FG_BLUE_ON, "false") == "true";
            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            if (!blueOn && !cyanOn)
            {
                Plugin.Log.LogInfo($"folder tile colours: blue+cyan both off, skipping {FullPath(folderRoot)}");
                return;
            }

            Color blue = BlueReplacement();
            Color cyan = CyanReplacement();

            foreach (var img in folderRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;
                string n = img.gameObject.name;
                bool fill = blueOn && n == "Background_Fill";
                bool sel = cyanOn && n == "Background_Selected";
                if (!fill && !sel) continue;

                int id = img.GetInstanceID();
                Color before = img.color;
                if (!_fgOriginals.ContainsKey(id)) { _fgOriginals[id] = before; _fgTouchedImages[id] = img; }
                Color target = fill ? blue : cyan;
                img.color = new Color(target.r, target.g, target.b, img.color.a);
                Plugin.Log.LogInfo($"folder tile colour {FullPath(img.transform)} {before} -> {img.color}");
            }
        }

        // OnApply's full-canvas RevertForeground clears the same _fgOriginals cache the carousel/folder
        // tiles are tracked in, but nothing re-sweeps them by name afterward — so a manual apply while
        // browsing left every visible tile blank. re-hit whatever's actually open right now: the
        // in-editor object carousel AND the level browser's own tile grid (two different screens,
        // two different game types, same Background_Fill/Background_Selected naming).
        public void ReapplyLevelBrowserTilesLive()
        {
            var carrousels = FindObjectsOfType<LevelEditorCarrouselViewModel>();
            Plugin.Log.LogInfo($"reapply level browser tiles: {carrousels.Length} editor carrousel(s) live");
            foreach (var carrousel in carrousels)
            {
                if (carrousel == null) continue;
                var carouselItems = carrousel.transform.Find("Carousel_Items");
                if (carouselItems != null) ApplyFolderTileColours(carouselItems);
                var folderItems = carrousel.transform.Find("Folder Items");
                if (folderItems != null) ApplyFolderTileColours(folderItems);
            }

            // browser tiles use a totally different prefab to the editor's object carousel, so they
            // go through the generic hue sweep instead of the Background_Fill/Selected name match.
            var browserTiles = FindObjectsOfType<Wushu.LevelEditor.Runtime.UI.LevelBrowser.LevelBrowserTileViewModel>();
            Plugin.Log.LogInfo($"reapply level browser tiles: {browserTiles.Length} browser tile(s) live");
            foreach (var tile in browserTiles)
            {
                if (tile == null) continue;
                ReapplyForegroundFromSettings(tile.transform, null, true);
            }
        }

        private struct RadialItemColours
        {
            public LevelEditor_RadialMenuItemViewModel Item;
            public Color Selected, Deselected;
        }

        // a radial item repaints its own fill from _default*Color on hover/press/submit, so the tint
        // has to live in those and be matched off the cached true originals, never off our own output
        private readonly Dictionary<int, RadialItemColours> _radialOriginals = new Dictionary<int, RadialItemColours>();

        public void RetintRadialItems(Transform scopeRoot, bool remapLive = false)
        {
            var pal = FgPalette.FromSettings();
            if (!pal.AnyOn) return;

            foreach (var item in scopeRoot.GetComponentsInChildren<LevelEditor_RadialMenuItemViewModel>(true))
            {
                if (item._toggle == null) continue;

                int id = item.GetInstanceID();
                if (!_radialOriginals.TryGetValue(id, out var orig))
                {
                    orig = new RadialItemColours { Item = item, Selected = item._defaultSelectedColor, Deselected = item._defaultDeselectedColor };
                    _radialOriginals[id] = orig;
                }

                Color selected = pal.TryRetint(orig.Selected, out var s) ? new Color(s.r, s.g, s.b, orig.Selected.a) : orig.Selected;
                Color deselected = pal.TryRetint(orig.Deselected, out var d) ? new Color(d.r, d.g, d.b, orig.Deselected.a) : orig.Deselected;
                PaintRadialItem(item, selected, deselected);

                if (!remapLive) continue;
                foreach (var img in item.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (img.gameObject.name != "Background_Fill") continue;
                    Color to;
                    if (SameRgb(img.color, orig.Selected)) to = selected;
                    else if (SameRgb(img.color, orig.Deselected)) to = deselected;
                    else continue;

                    int imgId = img.GetInstanceID();
                    if (!_fgOriginals.ContainsKey(imgId)) { _fgOriginals[imgId] = img.color; _fgTouchedImages[imgId] = img; }
                    img.color = new Color(to.r, to.g, to.b, img.color.a);
                }
            }
        }

        private static bool SameRgb(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;

        private static void PaintRadialItem(LevelEditor_RadialMenuItemViewModel item, Color selected, Color deselected)
        {
            item._defaultSelectedColor = selected;
            item._defaultDeselectedColor = deselected;
        }

        public void RevertForeground()
        {
            foreach (var kv in _radialOriginals)
                if (kv.Value.Item != null && kv.Value.Item._toggle != null)
                    PaintRadialItem(kv.Value.Item, kv.Value.Selected, kv.Value.Deselected);
            _radialOriginals.Clear();

            if (_fgOriginals.Count == 0) return;

            // restore via the tracked Image refs — avoids a full UICanvas GetComponentsInChildren
            // walk every state-change reapply, which was visibly freezing transitions.
            foreach (var kv in _fgTouchedImages)
            {
                var img = kv.Value;
                if (img == null) continue;
                if (_fgOriginals.TryGetValue(kv.Key, out var orig))
                    img.color = orig;
            }

            _fgOriginals.Clear();
            _fgTouchedImages.Clear();
        }

        // the season progress banner is driven by UITween so it can't be recoloured once-and-done —
        // SeasonProgressHoverPatch calls this every tween frame with the live Image and whether the
        // banner is hovered. idle = blue, hover = pink. only stomp the tween's colour if that colour's
        // foreground toggle is actually on; otherwise leave the game's colour alone.
        public void RecolourSeasonProgressImage(UnityEngine.UI.Image img, bool hovered)
        {
            if (img == null) return;

            if (hovered)
            {
                if (SettingsService.Get(KEY_FG_PINK_ON, "false") != "true") return;
                var p = PinkReplacement();
                img.color = new Color(p.r, p.g, p.b, img.color.a);
            }
            else
            {
                if (SettingsService.Get(KEY_FG_BLUE_ON, "false") != "true") return;
                var b = BlueReplacement();
                img.color = new Color(b.r, b.g, b.b, img.color.a);
            }
        }

        public enum SpecialScreen
        {
            PrivateLobbyShowSelect,
            PrivateLobbyPlayerList
        }

        public IEnumerator ReapplySpecialForegroundNextFrame(SpecialScreen screen)
        {
            yield return null;
            ReapplySpecialForeground(screen);
        }

        public void ReapplySpecialForeground(SpecialScreen screen)
        {
            switch (screen)
            {
                case SpecialScreen.PrivateLobbyShowSelect:
                {
                    var root = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prefab_UI_PrivateLobbyShowSelect(Clone)");
                    if (root == null) return;
                    ReapplyForegroundFromSettings(root.transform);
                    ApplyPrivateLobbyShowSelect(root.transform);
                    break;
                }
                case SpecialScreen.PrivateLobbyPlayerList:
                {
                    var root = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prime_UI_PrivateLobbyPlayerList(Clone)");
                    if (root == null) return;
                    ReapplyForegroundFromSettings(root.transform);
                    SetImageColor(root.transform, "SafeArea/PlayerListArea/PlayerListBackground/Background_Fill/OuterBorder", BlueReplacement(), true);
                    BetterFG.Features.MorePlatformIcon.FeatureMorePlatformIcon.ApplyPrivateLobbyNamesFromScene(root.transform);
                    break;
                }
            }
        }

        public void ApplyKnownSpecialForegroundScreens()
        {
            ReapplySpecialForeground(SpecialScreen.PrivateLobbyShowSelect);
            ReapplySpecialForeground(SpecialScreen.PrivateLobbyPlayerList);

            // navigation overlay sits outside UICanvas_Client_V2, so the main sweep misses it.
            var nav = GameObject.Find("Prefab_UI_NavigationOverlay(Clone)");
            if (nav != null) ReapplyForegroundFromSettings(nav.transform);

            // RevertForeground put the radial's toggles back, so redo them if it's open right now
            var radial = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prime_UI_LE_RadialMenu_Canvas(Clone)");
            if (radial != null && radial.activeInHierarchy)
            {
                ReapplyForegroundFromSettings(radial.transform, "SettingsBackingSprite", true);
                ApplyEditorShader(radial.transform);
                RetintRadialItems(radial.transform, true);
            }
        }

        private void ApplyMainPlayButtonUnselectedFill(Transform scopeRoot, bool yellowOn, Color yellowTarget)
        {
            if (scopeRoot == null || !yellowOn) return;

            string[] roots =
            {
                "Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/",
                "MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/",
                "Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/",
                "SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/"
            };

            foreach (var root in roots)
            {
                var unselected = scopeRoot.Find(root + "Panel_Unselected/Panel_Fill");
                var img = unselected == null ? null : unselected.GetComponent<UnityEngine.UI.Image>();
                if (img == null) continue;

                int id = img.GetInstanceID();
                if (!_fgOriginals.ContainsKey(id))
                {
                    _fgOriginals[id] = img.color;
                    _fgTouchedImages[id] = img;
                }

                img.color = new Color(yellowTarget.r, yellowTarget.g, yellowTarget.b, img.color.a);
                return;
            }
        }

        public void ReapplyPrivateLobbyShowEntryForeground(Transform entry)
        {
            if (entry == null) return;

            ReapplyForegroundFromSettings(entry);
            var outline = entry.Find("ButtonImage/Outline");
            var img = outline == null ? null : outline.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = Color.white;
        }

        private void ApplyPrivateLobbyShowSelect(Transform root)
        {
            if (root == null) return;

            var container = root.Find("Container");
            var image = container == null ? null : container.GetComponent<UnityEngine.UI.Image>();
            if (image != null && image.sprite != null)
            {
                int id = image.GetInstanceID();
                if (!_showSelectOriginalSprites.TryGetValue(id, out var original) || original == null)
                {
                    original = image.sprite;
                    _showSelectOriginalSprites[id] = original;
                }

                image.sprite = ProcessShowSelectSprite(original);
            }

            SetImageColor(root, "Container/ShowsScrollList/TabScrollbar", BlueReplacement(), true);

            var content = root.Find("Container/ShowsScrollList/ShowsListViewport/ShowsListContent");
            if (content == null) return;

            foreach (var img in content.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img != null && img.name == "Outline")
                    img.color = Color.white;
            }
        }

        private void SetImageColor(Transform root, string path, Color color, bool requireBlueOn = false)
        {
            if (root == null || requireBlueOn && SettingsService.Get(KEY_FG_BLUE_ON, "false") != "true") return;

            var t = root.Find(path);
            var img = t == null ? null : t.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = new Color(color.r, color.g, color.b, img.color.a);
        }

        private static Color BlueReplacement() =>
            new Color(ParseF(KEY_FG_BLUE_R, 0.1f), ParseF(KEY_FG_BLUE_G, 0.25f), ParseF(KEY_FG_BLUE_B, 0.85f), 1f);

        private static Color CyanReplacement() =>
            new Color(ParseF(KEY_FG_CYAN_R, 0f), ParseF(KEY_FG_CYAN_G, 0.3f), ParseF(KEY_FG_CYAN_B, 1f), 1f);

        private static Color PinkReplacement() =>
            new Color(ParseF(KEY_FG_PINK_R, 1f), ParseF(KEY_FG_PINK_G, 0.2f), ParseF(KEY_FG_PINK_B, 0.5f), 1f);

        private static Color BlackReplacement() =>
            new Color(ParseF(KEY_FG_BLACK_R, 0f), ParseF(KEY_FG_BLACK_G, 0f), ParseF(KEY_FG_BLACK_B, 0f), 1f);

        public static Color YellowReplacement() =>
            new Color(ParseF(KEY_FG_YELLOW_R, 1f), ParseF(KEY_FG_YELLOW_G, 0.5f), ParseF(KEY_FG_YELLOW_B, 0f), 1f);

        public static Color OrangeReplacement() =>
            new Color(ParseF(KEY_FG_ORANGE_R, 1f), ParseF(KEY_FG_ORANGE_G, 0.55f), ParseF(KEY_FG_ORANGE_B, 0.1f), 1f);

        // these gameplay images have hot-pink + dark-grey baked into their TEXTURE, not the image
        // colour, so colour tinting can't touch them. we recolour the actual pixels at runtime:
        // hot pink -> pink replacement, dark grey -> black replacement. cached per-image so we only
        // build the swapped texture once.
        //
        // paths are RELATIVE to the GameStates root so we can resolve them with transform.Find,
        // which (unlike GameObject.Find) also finds INACTIVE children — needed because only one
        // GameState is active at a time (CountdownState vs PlayingState).
        private const string GameStatesRootPath =
            "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates";


        // shader-material recolour. the hot-pink/dark-grey HUD bits are baked into the TEXTURE, not the
        // image colour, so a tint can't touch them. instead of reading pixels back and rebuilding textures
        // on the CPU (the old bake — a per-round GPU stall) we assign a custom UI material whose shader
        // remaps those hue/sat bands on the GPU at draw time. zero readback, zero per-round cost.
        private UnityEngine.Material _pinkGreyMat;

        // every Image we've put our material on, so we can clear it when the feature turns off.
        private readonly HashSet<UnityEngine.UI.Image> _shadedImages = new HashSet<UnityEngine.UI.Image>();

        private UnityEngine.Material GetPinkGreyMaterial()
        {
            if (_pinkGreyMat != null) return _pinkGreyMat;
            var sh = BetterFG.Core.AssetManager.GetShader("bettrfg_ui_shader");
            if (sh == null) { Plugin.Log.LogWarning("bettrfg_ui_shader missing, menu recolour is off"); return null; }
            _pinkGreyMat = new UnityEngine.Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_pinkGreyMat);
            return _pinkGreyMat;
        }

        public void ReapplyBakedPinkGreyTextures()
        {
            bool pinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true";
            bool blackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true";

            // feature off and nothing ever shaded -> nothing to do.
            if (!pinkOn && !blackOn && _shadedImages.Count == 0) return;

            // turned off: strip our material from everything we touched, then bail.
            if (!pinkOn && !blackOn)
            {
                foreach (var i in _shadedImages) if (i != null) i.material = null;
                _shadedImages.Clear();
                return;
            }

            var mat = GetPinkGreyMaterial();
            if (mat == null) return;

            Color pink = PinkReplacement();
            Color black = BlackReplacement();
            mat.SetColor("_PinkColor", pink);
            mat.SetColor("_BlackColor", black);
            mat.SetFloat("_PinkOn", pinkOn ? 1f : 0f);
            mat.SetFloat("_BlackOn", blackOn ? 1f : 0f);

            var gameStates = GameObject.Find(GameStatesRootPath);
            if (gameStates == null) return;
            var root = gameStates.transform;

            // only full-white images under one of these four gameplay view-models get the shader.
            foreach (var img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;

                var c = img.color;
                // white = full rgb, alpha ignored (some are faded in/out)
                if (c.r < 0.99f || c.g < 0.99f || c.b < 0.99f) continue;

                bool inScope = false;
                bool inCreatorInfoSlim = false;
                bool inTeamContainer = false;
                for (var p = img.transform; p != null; p = p.parent)
                {
                    var n = p.name;
                    if (n == "Generic_UI_LE_CreatorInfoSlim") { inCreatorInfoSlim = true; break; }
                    if (n == "TeamContainer") { inTeamContainer = true; break; }
                    if (n == "GameplayTimerViewModel" || n == "GameplayScoringViewModel"
                        || n == "GameplayInstructionsViewModel" || n == "GameplayTimeAttackViewModel")
                    {
                        inScope = true;
                    }
                }
                if (inCreatorInfoSlim || inTeamContainer || !inScope) continue;
                if (img.gameObject.name.Contains("Glyph")) continue;

                if (img.material != mat) img.material = mat;
                _shadedImages.Add(img);
            }
        }

        // editor screens (radial, settings backing, etc) bake their colours into the texture behind a
        // white image tint, so a flat colour swap can't reach them — the same reason the ingame HUD
        // needs a shader. white-tinted images get the pink/grey UI shader (with a cyan band added) so
        // the baked cyan/pink/grey remap on the GPU while white edges stay white. its own material keeps
        // this config off the ingame _pinkGreyMat.
        private UnityEngine.Material _editorShaderMat;

        public void ApplyEditorShader(Transform scopeRoot)
        {
            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            bool pinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true";
            bool blackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true";
            bool anyOn = cyanOn || pinkOn || blackOn;

            if (anyOn)
            {
                if (_editorShaderMat == null)
                {
                    var sh = BetterFG.Core.AssetManager.GetShader("bettrfg_ui_shader");
                    if (sh == null) { Plugin.Log.LogWarning("bettrfg_ui_shader missing, editor recolour is off"); return; }
                    _editorShaderMat = new UnityEngine.Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(_editorShaderMat);
                }
                _editorShaderMat.SetColor("_CyanColor", CyanReplacement());
                _editorShaderMat.SetColor("_PinkColor", PinkReplacement());
                _editorShaderMat.SetColor("_BlackColor", BlackReplacement());
                _editorShaderMat.SetFloat("_CyanOn", cyanOn ? 1f : 0f);
                _editorShaderMat.SetFloat("_PinkOn", pinkOn ? 1f : 0f);
                _editorShaderMat.SetFloat("_BlackOn", blackOn ? 1f : 0f);
            }

            foreach (var img in scopeRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;
                if (img.gameObject.name.IndexOf("glyph", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // row SelectedStateOverlay highlights are light cyan/blue; the flat sweep turns them
                // into the cyan replacement, but they should read as faint white instead.
                if (img.gameObject.name == "SelectedStateOverlay")
                {
                    if (cyanOn) img.color = new Color(1f, 1f, 1f, 0.2f);
                    continue;
                }

                // white-tinted images carry their colour in the texture — hand them the shader.
                var c = img.color;
                if (c.r >= 0.99f && c.g >= 0.99f && c.b >= 0.99f)
                    img.material = anyOn ? _editorShaderMat : null;
            }
        }

        private static UnityEngine.Sprite ProcessShowSelectSprite(UnityEngine.Sprite sprite)
        {
            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            if (!cyanOn) return sprite;

            var cyan = CyanReplacement();
            var src = sprite.texture;
            var rect = sprite.textureRect;
            int w = Mathf.RoundToInt(rect.width);
            int h = Mathf.RoundToInt(rect.height);
            if (src == null || w <= 0 || h <= 0) return sprite;

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var old = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = src.name + "_betterfg_private_lobby";
            tex.filterMode = src.filterMode;
            tex.wrapMode = src.wrapMode;
            tex.anisoLevel = src.anisoLevel;
            tex.mipMapBias = src.mipMapBias;
            tex.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
            tex.Apply(false, false);

            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = tex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.a <= 0.01f) continue;

                Color.RGBToHSV(c, out float hue, out float sat, out float val);
                if (val > 0.82f && sat < 0.2f)
                    pixels[i] = new Color(1f, 1f, 1f, c.a);
                else if (sat > 0.25f && hue >= 0.45f && hue <= 0.6f)
                    pixels[i] = new Color(cyan.r, cyan.g, cyan.b, c.a);
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);

            var made = UnityEngine.Sprite.Create(
                tex,
                new Rect(0, 0, w, h),
                new UnityEngine.Vector2(sprite.pivot.x / w, sprite.pivot.y / h),
                sprite.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                sprite.border
            );
            made.name = sprite.name + "_betterfg";
            return made;
        }
    }
}
