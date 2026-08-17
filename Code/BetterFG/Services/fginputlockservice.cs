using System.Collections.Generic;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;
using BetterFG.Utilities;

namespace BetterFG.Services
{
    // when any of our input fields is focused, disable rewired player maps
    // so wasd/jump don't leak into the game. leaves the UI input module alone
    // so our overlay clicks still work. Tick() must be called every frame
    // because the game re-enables maps under us.
    //
    // IMPORTANT: this must ONLY engage while typing in BettrFG's own UI. there are
    // two independent sources of "typing" in our ui and they must not stomp each
    // other, so each gets its own latch and we lock if either is set:
    //   - real unity InputFields owned by our canvas (driven by BetterFGUIMan,
    //     which verifies the selected field lives under our canvas so a focused
    //     Fall Guys input field never trips it)
    //   - our fake text fields (custom carets, no real InputField) driven by the tabs
    //   - CreativeTypeValueTweak, typing into the GAME's parameter node rather than our own ui
    public static class FGInputLockService
    {
        // kill switch for the whole background-input block (loading-screen lock, foreground check,
        // EventSystem/device disable) so a build can go out with none of it active for A/B testing.
        private const bool BackgroundGateEnabled = true;

        private static bool _realFieldLock;
        private static bool _fakeFieldLock;
        private static bool _editorUiLock;
        private static bool _controllerLock;
        private static bool _paramTypeLock;
        private static bool _loadingScreenLock;
        private static float _loadingScreenLockUntil;

        // Application.isFocused/Rewired's own focus tracking sticks "focused" from process start on
        // this build, so a boot that lands without real OS focus keeps servicing input regardless.
        // Ground-truth check against the actual foreground window catches that and plain alt-tab too.
        private static bool OutOfForeground => BackgroundGateEnabled && !Shell32Util.IsGameWindowForeground();

        public static bool IsLocked => _realFieldLock || _fakeFieldLock || _editorUiLock || _controllerLock || _paramTypeLock || _loadingScreenLock || OutOfForeground;

        // back-compat: callers that used SetLocked were the fake-field caret paths
        public static void SetLocked(bool locked) => _fakeFieldLock = locked;

        public static void SetFakeFieldLock(bool locked) => _fakeFieldLock = locked;

        public static void SetRealFieldLock(bool locked) => _realFieldLock = locked;

        // held while a number is being typed into a level editor parameter node
        public static void SetParamTypeLock(bool locked) => _paramTypeLock = locked;

        // locks the whole game input while our UI is up in the level editor — even when not
        // typing in a field — so editor hotkeys / movement don't fire under the open overlay.
        public static void SetEditorUiLock(bool locked) => _editorUiLock = locked;

        // locks game input while the player is steering our UI cursor with a controller, so the
        // same stick/buttons don't also drive menus or the camera underneath. ControllerManager
        // sets this on stick/button activity and clears it after a short idle.
        public static void SetControllerLock(bool locked) => _controllerLock = locked;

        // the game's own boot/menu loading veil doesn't stop clicks from reaching whatever's
        // already loaded underneath it, so a click during that window can land on a real menu
        // button (shop, etc). GameStatePatches pings this every LoadingScreenViewModel tick
        // while the general loading screen is up; no matching "hide" event exists so we just
        // let the lock expire if the pings stop for a moment.
        public static void RefreshLoadingScreenLock()
        {
            if (!BackgroundGateEnabled) return;
            _loadingScreenLock = true;
            _loadingScreenLockUntil = Time.unscaledTime + 0.3f;
        }

        // the other locks can be disable-only because the game restores maps on mouse/keyboard input.
        // these two can't: with their maps off nothing reaches the game to trigger that, so input stays
        // dead (pad unusable, or the editor ignoring Escape after you type until you jog the mouse).
        // re-enable ONLY the maps we turned off, a blanket one force-ons maps the game meant to be off
        // and scrambles the bindings.
        private static bool RestoreOnRelease => _controllerLock || _paramTypeLock;

        private static readonly HashSet<int> _selfDisabled = new HashSet<int>();
        private static bool _selfWasLocked;
        // reused across ticks — the controller lock holds every frame the UI is up with a pad, and a
        // fresh interop list per player per frame was constant GC churn
        private static Il2CppSystem.Collections.Generic.List<ControllerMap> _mapsBuf;

        // clicks reach Button.onClick through EventSystem/GraphicRaycaster off the raw OS cursor,
        // a path Rewired's ControllerMaps never touch. Disabling the EventSystem itself was the
        // first attempt, but EventSystem.current goes null the moment it's disabled (its own
        // OnDisable clears the singleton) and plenty of game code — including our own
        // LobbyCustomiserTweak — dereferences EventSystem.current unguarded, so that took down
        // NavigableMenuInputHandler.OnLoseFocus with cascading NREs the instant a screen opened.
        // Disabling just the input module stops all event dispatch the same way without EventSystem
        // itself, or .current, ever going null.
        private static BaseInputModule _disabledInputModule;

        // movement/jump/etc also don't all go through mapped Actions — some read the Keyboard/Mouse
        // Controller directly (GetKey/GetButton), which ControllerMap.enabled=false never touches.
        // Controller itself has an enabled flag that kills raw polling too, so hit that as well.
        private static bool _devicesDisabledByUs;

        public static void Tick()
        {
            if (_loadingScreenLock && Time.unscaledTime > _loadingScreenLockUntil)
                _loadingScreenLock = false;

            bool blockBackground = _loadingScreenLock || OutOfForeground;

            if (blockBackground)
            {
                var es = EventSystem.current;
                var module = es != null ? es.currentInputModule : null;
                if (module != null && module.enabled)
                {
                    module.enabled = false;
                    _disabledInputModule = module;
                }
            }
            else if (_disabledInputModule != null)
            {
                _disabledInputModule.enabled = true;
                _disabledInputModule = null;
            }

            if (ReInput.isReady)
            {
                var kb = ReInput.controllers.Keyboard;
                var mouse = ReInput.controllers.Mouse;
                if (blockBackground)
                {
                    if (kb != null) kb.enabled = false;
                    if (mouse != null) mouse.enabled = false;
                    _devicesDisabledByUs = true;
                }
                else if (_devicesDisabledByUs)
                {
                    if (kb != null) kb.enabled = true;
                    if (mouse != null) mouse.enabled = true;
                    _devicesDisabledByUs = false;
                }
            }

            if (!ReInput.isReady) return;
            int n = ReInput.players.playerCount;

            // must run BEFORE we re-evaluate IsLocked: a typing lock starting the same frame a stick
            // lock ends would otherwise never restore, leaving those maps off for good (and any later
            // re-enable doubles the input path, so presses register twice).
            if (_selfWasLocked && !RestoreOnRelease)
            {
                _selfWasLocked = false;
                if (_selfDisabled.Count > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var p = ReInput.players.GetPlayer(i);
                        if (p == null) continue;
                        var maps = _mapsBuf ??= new Il2CppSystem.Collections.Generic.List<ControllerMap>();
                        maps.Clear();
                        p.controllers.maps.GetAllMaps(maps);
                        for (int m = 0; m < maps.Count; m++)
                        {
                            var map = maps[m];
                            if (map != null && _selfDisabled.Contains(map.id))
                                map.enabled = true;
                        }
                    }
                    _selfDisabled.Clear();
                }
            }

            if (IsLocked)
            {
                // fresh snapshot so we don't carry stale ids from a previous lock session
                if (RestoreOnRelease && !_selfWasLocked) _selfDisabled.Clear();

                for (int i = 0; i < n; i++)
                {
                    var p = ReInput.players.GetPlayer(i);
                    if (p == null) continue;
                    var maps = _mapsBuf ??= new Il2CppSystem.Collections.Generic.List<ControllerMap>();
                    maps.Clear();
                    p.controllers.maps.GetAllMaps(maps);
                    for (int m = 0; m < maps.Count; m++)
                    {
                        var map = maps[m];
                        if (map == null || !map.enabled) continue;
                        if (RestoreOnRelease) _selfDisabled.Add(map.id);
                        map.enabled = false;
                    }
                }
                _selfWasLocked = RestoreOnRelease;
                return;
            }
        }
    }
}
