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
        private const string FGUI_CUSTOM_BACKDROP_PARENT_PATH = "3D Environment/Generic_UI_CurrentSeasonBackground_Container_MainMenu_Variant/Generic_UI_SeasonS11Background_Canvas_Variant/Mask";
        private const string FGUI_ORIGINAL_BACKDROP_PATH = "3D Environment/Generic_UI_CurrentSeasonBackground_Container_MainMenu_Variant/Generic_UI_SeasonS11Background_Canvas_Variant/Mask/Backdrop";
        private const string FGUI_ORIGINAL_CIRCLES_PATH = "3D Environment/Generic_UI_CurrentSeasonBackground_Container_MainMenu_Variant/Generic_UI_SeasonS11Background_Canvas_Variant/Mask/Circles";

        private Vector3 FG_UI_ORIGINAL_BACKDROP_PREFERREDLOCALPOS = new Vector3(-0f, 6f, -15);
        private Vector3 FG_UI_ORIGINAL_CIRCLES_PREFERREDLOCALPOS = new Vector3(0f, 0f, -95.4f);
        private Vector3 FG_UI_CUSTOM_BACKDROP_PREFERREDLOCALPOS = new Vector3(-0, -121f, -50.5f);

        // menu background
        private GameObject _menuBgGo;
        private Material _menuBgMat;
        // BG GO active toggle — separate from screen.fallforce.enabled (which now only governs
        // whether the custom gradient/pattern apply). users wanted to choose "use the BettrFG bg"
        // independent of "use my custom colours".
        public const string KEY_BG_ENABLED = "screen.fallforce.bg.enabled";

        // title screen background — same SeasonS11Background Mask as the menu one but under the
        // TitleScreen prefab. handled by ScreenBackgroundService (FallForce screen).
        private const string FGUI_TITLE_BACKDROP_PARENT_PATH = "UICanvas_Client_V2(Clone)/Default/Prefab_UI_TitleScreen(Clone)/Generic_UI_CurrentSeasonBackground_Container_Prefab/Generic_UI_SeasonS11Background_Canvas_Variant/Mask";

        // circles pattern — FallForce screen's pattern key (menu + title share it)
        public const string KEY_PATTERN_PATH = "screen.fallforce.pattern.path";
        // cached on first apply so remove can restore it. ONCE set, never overwritten by ApplyPatternFromSettings
        // (the reapply coroutine would otherwise re-read the live texture, which is our own custom one, and
        // RestorePattern would then restore the custom texture instead of the real original).
        private Texture _originalPatternTex;
        private bool _originalPatternCaptured;
        private Texture2D _appliedPatternTex; // the custom tex we last set, so we can destroy it on swap/remove

        // menu background image (sibling quad, unlit texture)
        private GameObject _menuBgImageGo;
        private Material _menuBgImageMat;
        private Texture2D _menuBgImageTex;
        public const string KEY_BG_IMG_ENABLED = "menu.bg.img.enabled";
        public const string KEY_BG_IMG_PATH = "menu.bg.img.path";
        public const string KEY_BG_IMG_POS_X = "menu.bg.img.pos.x";
        public const string KEY_BG_IMG_POS_Y = "menu.bg.img.pos.y";
        public const string KEY_BG_IMG_POS_Z = "menu.bg.img.pos.z";
        public const string KEY_BG_IMG_SCALE = "menu.bg.img.scale";
        public const string KEY_BG_IMG_SCALE_X = "menu.bg.img.scale.x";
        public const string KEY_BG_IMG_SCALE_Y = "menu.bg.img.scale.y";

        // bg image slider ranges + defaults — single source of truth (UI reads these too)
        public const float BG_IMG_POS_MIN = -10f;
        public const float BG_IMG_POS_MAX = 10f;
        public const float BG_IMG_POS_DEFAULT = 0f;
        public const float BG_IMG_SCALE_MIN = 0f;
        public const float BG_IMG_SCALE_MAX = 15f;
        public const float BG_IMG_SCALE_DEFAULT = 5f;
        public const float BG_IMG_SCALE_AXIS_MIN = 0.1f;
        public const float BG_IMG_SCALE_AXIS_MAX = 3f;
        public const float BG_IMG_SCALE_AXIS_DEFAULT = 1f;

        // gradient settings keys — these are the FallForce screen's keys (menu + title share them).
        // the Screen tab edits the same screen.fallforce.* keys via ScreenBackgroundService.
        public const string KEY_BG_TOP_R = "screen.fallforce.top.r";
        public const string KEY_BG_TOP_G = "screen.fallforce.top.g";
        public const string KEY_BG_TOP_B = "screen.fallforce.top.b";
        public const string KEY_BG_TOP_A = "screen.fallforce.top.a";
        public const string KEY_BG_BOT_R = "screen.fallforce.bot.r";
        public const string KEY_BG_BOT_G = "screen.fallforce.bot.g";
        public const string KEY_BG_BOT_B = "screen.fallforce.bot.b";
        public const string KEY_BG_BOT_A = "screen.fallforce.bot.a";
        public const string KEY_BG_BIAS = "screen.fallforce.bias";
        public const string KEY_BG_SMOOTH = "screen.fallforce.smooth";

        // one-shot: previously screen.fallforce.enabled drove BOTH the GO active state AND the
        // custom-colour apply. it's now split — bg.enabled drives the GO. for users who already
        // had the screen enabled, carry that state into the new bg.enabled so the bg stays on.
        private static void MigrateBgSplit()
        {
            if (SettingsService.Get("screen.bgsplit.migrated", "false") == "true") return;
            if (SettingsService.Get("screen.fallforce.enabled", "false") == "true")
                SettingsService.Set(KEY_BG_ENABLED, "true");
            SettingsService.Set("screen.bgsplit.migrated", "true");
        }

        // one-time copy of the old menu.bg.* gradient/pattern values into the new screen.fallforce.*
        // keys so existing setups keep their look after the migration to per-screen settings.
        private static void MigrateOldBgKeys()
        {
            if (SettingsService.Get("screen.migrated", "false") == "true") return;
            (string oldK, string newK)[] map =
            {
                ("menu.bg.top.r", KEY_BG_TOP_R), ("menu.bg.top.g", KEY_BG_TOP_G), ("menu.bg.top.b", KEY_BG_TOP_B),
                ("menu.bg.bot.r", KEY_BG_BOT_R), ("menu.bg.bot.g", KEY_BG_BOT_G), ("menu.bg.bot.b", KEY_BG_BOT_B),
                ("menu.bg.bias", KEY_BG_BIAS), ("menu.bg.smooth", KEY_BG_SMOOTH),
                ("menu.bg.enabled", "screen.fallforce.enabled"),
                ("menu.bg.pattern.path", "screen.fallforce.pattern.path"),
            };
            foreach (var (oldK, newK) in map)
            {
                var v = SettingsService.Get(oldK, "");
                if (!string.IsNullOrEmpty(v)) SettingsService.Set(newK, v);
            }
            SettingsService.Set("screen.migrated", "true");
        }

        // ── Menu background ───────────────────────────────────────────────────

        public void SpawnMenuBg()
        {
            // gradient prefab: spawn once
            if (_menuBgGo == null)
            {
                TweakFallGuysBgForBetterfg();

                var go = AssetManager.SpawnPersistent("betterfg_menubg");
                if (go == null) { Plugin.Log.LogWarning("menubg prefab missing from the bundle"); return; }

                _menuBgGo = go;
                _menuBgGo.transform.SetParent(GameObject.Find(FGUI_CUSTOM_BACKDROP_PARENT_PATH).transform, true);
                _menuBgGo.transform.localPosition = FG_UI_CUSTOM_BACKDROP_PREFERREDLOCALPOS;
                _menuBgGo.transform.localRotation = Quaternion.Euler(270, 0, 0);
                _menuBgGo.layer = LayerMask.NameToLayer("PlayerUI");
                _menuBgGo.name = "BetterFG_MenuBg";

                var rend = _menuBgGo.GetComponent<Renderer>();
                if (rend != null) _menuBgMat = rend.material;
            }

            // restore everything every menu enter (not just first time)
            ApplyGradientFromSettings();
            BetterFG.UI.Tab.UITab.ApplyCanvasScalingFromSettings();

            bool bgEnabled = SettingsService.Get(KEY_BG_ENABLED, "false") == "true";
            if (_menuBgGo != null) _menuBgGo.SetActive(bgEnabled);

            EnsureImageBg();
            ApplyImageBgFromSettings();

            // sun GO is fresh each menu enter — recapture its original rotation before applying.
            // ambient/sun must run after the game finishes its own scene lighting setup, else it
            // overwrites RenderSettings/the sun transform — so defer a frame.
            _sunSaved = false;
            _ambientSaved = false;
            _plinthColSaved = false;
            StartCoroutine(ApplyAmbientAndSunNextFrame().WrapToIl2Cpp());

        }

        // title screen uses the FallForce screen's gradient + pattern (same look as the menu).
        // the bg respawns over the first few hundred ms, so re-assert a few times to win the race.
        public void ReapplyTitleScreenBg()
        {
            StartCoroutine(ReapplyTitleScreenBgLoop().WrapToIl2Cpp());
        }

        private IEnumerator ReapplyTitleScreenBgLoop()
        {
            yield return null; // let the title screen finish building its background first
            for (int i = 0; i < 6; i++)
            {
                var maskGo = GameObject.Find(FGUI_TITLE_BACKDROP_PARENT_PATH);
                if (maskGo != null)
                    ScreenBackgroundService.Apply(ScreenBackgroundService.Screen.FallForce, maskGo.transform);
                yield return new WaitForSeconds(0.1f);
            }
        }

        private IEnumerator ApplyAmbientAndSunNextFrame()
        {
            yield return null;
            ApplyAmbientFromSettings();
            ApplySunFromSettings();
            ApplyPlinthColorFromSettings();
            ApplyPatternFromSettings();

            // game re-runs its own scene lighting setup a bit into the menu and stomps our ambient.
            // coming back from a game the scene takes longer to settle than a single 0.1s window, so
            // reassert a handful of times over ~1s to win the race regardless of how late it lands.
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSeconds(0.12f);
                ApplyAmbientFromSettings();
            }
        }

        // ── Menu background image ─────────────────────────────────────────────

        // fallback base only — the real base is PB_UI_Character's world position, cached once in the
        // OnMainMenuEntered postfix (content updates move it, same story as the cam). never re-cached.
        private static readonly Vector3 BG_IMG_BASE_WORLD_POS = new Vector3(0f, 3.5f, 4.3f);
        private Vector3 _bgImgBasePos = BG_IMG_BASE_WORLD_POS;
        private bool _bgImgBaseCached;

        public void CacheBgImageBase()
        {
            if (_bgImgBaseCached) return;
            var t = GameObject.Find("3D Environment/MainMenu_Environment/PlinthRig/CharacterAndPlinthHolder_Main/ENV_Plinth_MO/CharacterHolder/PB_UI_Character");
            if (t == null) return;
            _bgImgBasePos = t.transform.position;
            _bgImgBaseCached = true;
        }

        private void EnsureImageBg()
        {
            // ?. against a destroyed Unity object returns true for == null, so this also recreates after scene unload
            if (_menuBgImageGo != null) return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "BetterFG_MenuBgImage";
            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // unparented + scene-bound: gets cleaned up when the menu scene unloads,
            // so it never leaks into rounds.
            quad.transform.position = _bgImgBasePos;
            quad.transform.rotation = Quaternion.Euler(0, 0, 0);
            quad.transform.localScale = Vector3.one;
            quad.layer = LayerMask.NameToLayer("PlayerUI");

            Shader shader = null;
            foreach (var name in new[] { "Unlit/Texture", "Unlit/Transparent", "Universal Render Pipeline/Unlit", "Sprites/Default", "UI/Default" })
            {
                shader = Shader.Find(name);
                if (shader != null) break;
            }
            if (shader == null) return;

            var mat = new Material(shader);
            mat.color = Color.white;
            var rend = quad.GetComponent<Renderer>();
            rend.material = mat;
            // re-read the renderer's actual instance — assigning .material may instantiate a copy
            _menuBgImageMat = rend.material;
            _menuBgImageMat.renderQueue = 2990; // just behind plinth, in front of gradient backdrop

            _menuBgImageGo = quad;
        }

        // image bg is hidden whenever the customiser prefab canvas or store screen is active
        private const string CUSTOMISER_CANVAS_PATH = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Customiser/Prime_UI_Customizer_Prefab_Canvas(Clone)";
        private const string STORE_SCREEN_PATH = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Store";

        public void SetImageBgEnabled(bool enabled)
        {
            SettingsService.Set(KEY_BG_IMG_ENABLED, enabled ? "true" : "false");
            EnsureImageBg();
            RefreshImageBgVisibility();
        }

        // true when the customiser prefab canvas is active in the hierarchy
        private static bool IsCustomiserOpen()
        {
            var go = GameObject.Find(CUSTOMISER_CANVAS_PATH);
            return go != null && go.activeInHierarchy;
        }

        private static bool IsStoreOpen()
        {
            var go = GameObject.Find(STORE_SCREEN_PATH);
            return go != null && go.activeInHierarchy;
        }

        public void RefreshImageBgVisibility()
        {
            if (_menuBgImageGo == null) return;
            bool enabled = SettingsService.Get(KEY_BG_IMG_ENABLED, "false") == "true";
            _menuBgImageGo.SetActive(enabled && !IsCustomiserOpen() && !IsStoreOpen());
        }

        public void HideImageBg()
        {
            if (_menuBgImageGo != null) _menuBgImageGo.SetActive(false);
        }

        public void ApplyImageBgTexture(string path)
        {
            EnsureImageBg();
            if (_menuBgImageMat == null) return;

            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                _menuBgImageMat.mainTexture = null;
                return;
            }

            try
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) { Plugin.Log.LogWarning($"not a decodable image: {System.IO.Path.GetFileName(path)}"); return; }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply();
                if (_menuBgImageTex != null) Destroy(_menuBgImageTex);
                _menuBgImageTex = tex;

                foreach (var prop in new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_UnlitColorMap", "_Texture", "_Tex" })
                    if (_menuBgImageMat.HasProperty(prop))
                        _menuBgImageMat.SetTexture(prop, tex);
                _menuBgImageMat.mainTexture = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError($"menu bg image '{System.IO.Path.GetFileName(path)}' failed to load: {ex.Message}"); }
        }

        public void ApplyImageBgTransform(float posX, float posY, float posZ, float scaleUniform, float scaleX, float scaleY)
        {
            EnsureImageBg();
            if (_menuBgImageGo == null) return;

            _menuBgImageGo.transform.position =
                _bgImgBasePos + new Vector3(posX, posY, posZ);
            _menuBgImageGo.transform.localScale =
                new Vector3(scaleUniform * scaleX, scaleUniform * scaleY, 1f);
        }

        public void ApplyImageBgFromSettings()
        {
            EnsureImageBg();
            if (_menuBgImageGo == null) return;

            ApplyImageBgTexture(SettingsService.Get(KEY_BG_IMG_PATH, ""));
            ApplyImageBgTransform(
                ParseF(KEY_BG_IMG_POS_X, BG_IMG_POS_DEFAULT),
                ParseF(KEY_BG_IMG_POS_Y, BG_IMG_POS_DEFAULT),
                ParseF(KEY_BG_IMG_POS_Z, BG_IMG_POS_DEFAULT),
                ParseF(KEY_BG_IMG_SCALE, BG_IMG_SCALE_DEFAULT),
                ParseF(KEY_BG_IMG_SCALE_X, BG_IMG_SCALE_AXIS_DEFAULT),
                ParseF(KEY_BG_IMG_SCALE_Y, BG_IMG_SCALE_AXIS_DEFAULT));

            RefreshImageBgVisibility();
        }

        public void SetMenuBgEnabled(bool enabled)
        {
            SettingsService.Set(KEY_BG_ENABLED, enabled ? "true" : "false");
            if (_menuBgGo != null)
                _menuBgGo.SetActive(enabled);
        }

        public void TweakFallGuysBgForBetterfg()
        {
            GameObject.Find(FGUI_ORIGINAL_BACKDROP_PATH).transform.localPosition = FG_UI_ORIGINAL_BACKDROP_PREFERREDLOCALPOS;
            GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH).transform.localPosition = FG_UI_ORIGINAL_CIRCLES_PREFERREDLOCALPOS;
        }

        // applies the saved circles pattern texture onto the Circles image material. safe to call
        // repeatedly — the Circles GO is fresh each menu enter so we re-resolve it every time, and
        // we cache the untouched original on first apply so RestorePattern can put it back.
        public void ApplyPatternFromSettings()
        {
            var circlesGo = GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH);
            if (circlesGo == null) return; // not up yet — retry loop will catch it

            var img = circlesGo.GetComponent<UnityEngine.UI.Image>();
            if (img == null || img.material == null) return;

            // cache the REAL original exactly once per session. only safe to read here when we haven't
            // applied a custom yet — once we have, GetTexture("_Pattern") returns our own custom tex.
            if (!_originalPatternCaptured && _appliedPatternTex == null)
            {
                _originalPatternTex = img.material.GetTexture("_Pattern");
                _originalPatternCaptured = true;
            }

            string path = SettingsService.Get(KEY_PATTERN_PATH, "");
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

            try
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.Apply();

                if (_appliedPatternTex != null) Destroy(_appliedPatternTex);
                _appliedPatternTex = tex;

                img.material.SetTexture("_Pattern", tex);
                Plugin.Log.LogInfo($"menu pattern -> {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex) { Plugin.Log.LogError($"menu pattern failed: {ex.Message}"); }
        }

        public void RestorePattern()
        {
            // nothing to do if we never applied a custom in the first place
            if (!_originalPatternCaptured && _appliedPatternTex == null) return;

            var circlesGo = GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH);
            if (circlesGo == null) return;
            var img = circlesGo.GetComponent<UnityEngine.UI.Image>();
            if (img == null || img.material == null) return;

            img.material.SetTexture("_Pattern", _originalPatternTex);

            if (_appliedPatternTex != null)
            {
                Destroy(_appliedPatternTex);
                _appliedPatternTex = null;
            }
        }

        public void ApplyGradient(Color top, Color bot, float bias, float smoothness)
        {
            if (_menuBgMat == null) return;
            _menuBgMat.SetColor("_TopColor", top);
            _menuBgMat.SetColor("_BottomColor", bot);
            _menuBgMat.SetFloat("_Bias", bias);
            _menuBgMat.SetFloat("_Smoothness", smoothness);
        }

        public void ApplyGradientFromSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float Parse(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            var top = new Color(Parse(KEY_BG_TOP_R, 0f), Parse(KEY_BG_TOP_G, 0f), Parse(KEY_BG_TOP_B, 0f), Parse(KEY_BG_TOP_A, 1f));
            var bot = new Color(Parse(KEY_BG_BOT_R, 1f), Parse(KEY_BG_BOT_G, 1f), Parse(KEY_BG_BOT_B, 1f), Parse(KEY_BG_BOT_A, 1f));
            float bias = Parse(KEY_BG_BIAS, 0f);
            float smooth = Parse(KEY_BG_SMOOTH, 1f);
            ApplyGradient(top, bot, bias, smooth);
        }

        // ── Lobby background ─────────────────────────────────────────────────

        // lobby bg texture cache: instanceID → original sprite (cached once, never destroyed)
        private readonly Dictionary<int, UnityEngine.Sprite> _lobbyTexOriginals = new Dictionary<int, UnityEngine.Sprite>();

        // lobby bg color cache: instanceID → original color (cached once, never overwritten)
        private readonly Dictionary<int, Color> _lobbyColorOriginals = new Dictionary<int, Color>();

        // falling screen (lobby bg) — only ever driven by the falling-screen custom slot colours now.
        // the Foreground cyan/colour toggles no longer touch the lobby bg at all.
        public void ApplyLobbyBGForeground()
        {
            if (SettingsService.Get(KEY_LOBBYBG_ENABLED, "false").Equals("true"))
                ApplyLobbyBgCustomColors(
                    new Color(ParseF(KEY_LOBBYBG_SLOT0_R, 0f), ParseF(KEY_LOBBYBG_SLOT0_G, 0f), ParseF(KEY_LOBBYBG_SLOT0_B, 1f)),
                    new Color(ParseF(KEY_LOBBYBG_SLOT1_R, 0f), ParseF(KEY_LOBBYBG_SLOT1_G, 0.5f), ParseF(KEY_LOBBYBG_SLOT1_B, 1f)),
                    new Color(ParseF(KEY_LOBBYBG_SLOT2_R, 0.8f), ParseF(KEY_LOBBYBG_SLOT2_G, 0.8f), ParseF(KEY_LOBBYBG_SLOT2_B, 1f)));
            else
                RevertLobbyBGForeground();
        }

        public void RevertLobbyBGForeground()
        {
            if (_lobbyTexOriginals.Count == 0) return;

            var lobbyRoot = GameObject.Find("Menu_Screen_Lobby(Clone)/BackgroundCanvas/Prefab_UI_Lobby");
            if (lobbyRoot == null) return;

            var images = lobbyRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach (var img in images)
            {
                if (img == null) continue;
                int id = img.GetInstanceID();
                if (_lobbyTexOriginals.TryGetValue(id, out var origSprite))
                    img.sprite = origSprite;
                if (_lobbyColorOriginals.TryGetValue(id, out var origColor))
                    img.color = origColor;
            }

            foreach (var id in _lobbyTexOriginals.Keys) { _fgOriginals.Remove(id); _fgTouchedImages.Remove(id); }
            _lobbyTexOriginals.Clear();
            _lobbyColorOriginals.Clear();
        }

        public static IEnumerator ApplyLobbyBGForegroundNextFrame()
        {
            yield return null;
            Instance?.ApplyLobbyBGForeground();
        }

        // keys for lobby bg custom slot colours
        // enabled gate for the falling-screen (lobby bg) custom colours — independent of the
        // Foreground cyan toggle now that it has its own UI in the UI tab's Background section.
        public const string KEY_LOBBYBG_ENABLED = "menu.lobbybg.enabled";
        public const string KEY_LOBBYBG_SLOT0_R = "menu.lobbybg.slot0.r";
        public const string KEY_LOBBYBG_SLOT0_G = "menu.lobbybg.slot0.g";
        public const string KEY_LOBBYBG_SLOT0_B = "menu.lobbybg.slot0.b";
        public const string KEY_LOBBYBG_SLOT1_R = "menu.lobbybg.slot1.r";
        public const string KEY_LOBBYBG_SLOT1_G = "menu.lobbybg.slot1.g";
        public const string KEY_LOBBYBG_SLOT1_B = "menu.lobbybg.slot1.b";
        public const string KEY_LOBBYBG_SLOT2_R = "menu.lobbybg.slot2.r";
        public const string KEY_LOBBYBG_SLOT2_G = "menu.lobbybg.slot2.g";
        public const string KEY_LOBBYBG_SLOT2_B = "menu.lobbybg.slot2.b";

        // scans Prefab_UI_Lobby images and returns up to 3 dominant clustered colours from originals
        // slot indices: 0=DarkBlue, 1=MedBlue, 2=LightBlue
        private static readonly string[] LobbyBgSlotNames = { "DarkBlue", "MedBlue", "LightBlue" };

        public Color[] ScanLobbyBgColors()
        {
            var lobbyRoot = GameObject.Find("Menu_Screen_Lobby(Clone)/BackgroundCanvas/Prefab_UI_Lobby");
            var result = new Color[] { new Color(0.05f, 0.1f, 0.3f), new Color(0.1f, 0.3f, 0.7f), new Color(0.3f, 0.6f, 1f) };
            if (lobbyRoot == null) return result;

            var images = lobbyRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);

            for (int slot = 0; slot < LobbyBgSlotNames.Length; slot++)
            {
                float r = 0f, g = 0f, b = 0f;
                int count = 0;
                foreach (var img in images)
                {
                    if (img == null || !img.gameObject.name.Contains(LobbyBgSlotNames[slot])) continue;
                    int id = img.GetInstanceID();
                    Color c = _lobbyColorOriginals.TryGetValue(id, out var orig) ? orig : img.color;
                    if (c.a < 0.05f) continue;
                    r += c.r; g += c.g; b += c.b;
                    count++;
                }
                if (count > 0)
                    result[slot] = new Color(r / count, g / count, b / count);
            }

            return result;
        }

        // applies 3 custom slot colours to lobby bg images by name-based group match (DarkBlue/MedBlue/LightBlue)
        public void ApplyLobbyBgCustomColors(Color slot0, Color slot1, Color slot2)
        {
            var lobbyRoot = GameObject.Find("Menu_Screen_Lobby(Clone)/BackgroundCanvas/Prefab_UI_Lobby");
            if (lobbyRoot == null) return;

            var slotColors = new Color[] { slot0, slot1, slot2 };
            var images = lobbyRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);

            foreach (var img in images)
            {
                if (img == null) continue;

                int slotIdx = -1;
                for (int k = 0; k < LobbyBgSlotNames.Length; k++)
                    if (img.gameObject.name.Contains(LobbyBgSlotNames[k])) { slotIdx = k; break; }
                if (slotIdx < 0) continue;

                int id = img.GetInstanceID();
                if (!_lobbyColorOriginals.ContainsKey(id))
                    _lobbyColorOriginals[id] = img.color;
                if (!_lobbyTexOriginals.ContainsKey(id))
                    _lobbyTexOriginals[id] = img.sprite;

                Color original = _lobbyColorOriginals[id];
                if (original.a < 0.05f) continue;

                img.sprite = null;
                var target = slotColors[slotIdx];
                img.color = new Color(target.r, target.g, target.b, original.a);
            }

        }

        // ── Creative (level browser) background ────────────────────────────────
        // Generic_UI_CreativeBackground_Prefab_Canvas is a flat paper-craft UI with named image
        // groups, not a gradient. one colour per slot; Drawings also covers the Grid image.
        // originals cached per Image id so remove restores the game's own colours.
        public enum CreativeSlot { Backdrop, Glows, Drawings, Vignette }
        public const string KEY_CREATIVE_ENABLED = "screen.creative.enabled";

        public static Color CreativeSlotDefault(CreativeSlot slot) =>
              slot == CreativeSlot.Backdrop ? new Color(0.98f, 0.93f, 0.82f)
            : slot == CreativeSlot.Glows ? new Color(1f, 0.98f, 0.9f)
            : slot == CreativeSlot.Drawings ? new Color(0.2f, 0.3f, 0.45f)
            : Color.black;

        public static Color CreativeSlotColor(CreativeSlot slot)
        {
            string k = $"screen.creative.{slot}";
            Color d = CreativeSlotDefault(slot);
            return new Color(ParseF(k + ".r", d.r), ParseF(k + ".g", d.g), ParseF(k + ".b", d.b));
        }

        private readonly Dictionary<int, Color> _creativeOriginals = new Dictionary<int, Color>();

        // the prefab shows up in two spots, both full-path pinned (the level editor's other variants
        // share the name): the level browser popup, and the level-editor menu backdrop on the
        // world-space CameraRig (view index 3 of the MainMenuBuilder switcher).
        private static Transform CreativeBrowserCanvas()
        {
            var go = GameObject.Find("UICanvas_Client_V2(Clone)/Popup/Prime_UI_LE_LevelBrowser(Clone)/Generic_UI_CreativeBackground_Prefab_Canvas");
            return go != null ? go.transform : null;
        }

        public static Transform CreativeEditorCanvas()
        {
            var go = GameObject.Find("CameraRig/VirtualCameras/MainMenu_LevelEditor/Generic_UI_CreativeBackground_Prefab_Canvas");
            return go != null ? go.transform : null;
        }

        public void ApplyCreativeBg(Transform canvas)
        {
            if (canvas == null) return;
            RecolorCreative(canvas.Find("Backdrop"), CreativeSlot.Backdrop);
            RecolorCreativeChildren(canvas.Find("Glows"), CreativeSlot.Glows);
            RecolorCreative(canvas.Find("Grid"), CreativeSlot.Drawings);
            RecolorCreativeChildren(canvas.Find("Drawings"), CreativeSlot.Drawings);
            RecolorCreative(canvas.Find("Vignette"), CreativeSlot.Vignette);
        }

        public void RevertCreativeBg(Transform canvas)
        {
            if (canvas == null) return;
            foreach (var g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (g != null && _creativeOriginals.TryGetValue(g.GetInstanceID(), out var c)) g.color = c;
        }

        // apply if enabled, else revert — for one creative canvas. the editor canvas' OnEnable applier
        // and the Screen tab both go through here.
        public void RefreshCreativeCanvas(Transform canvas)
        {
            if (canvas == null) return;
            if (SettingsService.Get(KEY_CREATIVE_ENABLED, "false") == "true") ApplyCreativeBg(canvas);
            else RevertCreativeBg(canvas);
        }

        // Screen tab Apply/Remove/toggle — hit whichever creative canvas is up right now
        public void ReapplyCreativeBgLive()
        {
            RefreshCreativeCanvas(CreativeBrowserCanvas());
            RefreshCreativeCanvas(CreativeEditorCanvas());
        }

        private void RecolorCreativeChildren(Transform parent, CreativeSlot slot)
        {
            if (parent == null) return;
            for (int i = 0; i < parent.childCount; i++) RecolorCreative(parent.GetChild(i), slot);
        }

        private void RecolorCreative(Transform t, CreativeSlot slot)
        {
            var g = t != null ? t.GetComponent<UnityEngine.UI.Graphic>() : null;
            if (g == null) return;
            int id = g.GetInstanceID();
            if (!_creativeOriginals.ContainsKey(id)) _creativeOriginals[id] = g.color;
            var c = CreativeSlotColor(slot);
            g.color = new Color(c.r, c.g, c.b, g.color.a);
        }
    }
}
