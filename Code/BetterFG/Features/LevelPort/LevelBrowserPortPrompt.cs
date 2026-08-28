using System;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using UnityEngine;
using LB = Wushu.LevelEditor.Runtime.UI.LevelBrowser;

namespace BetterFG.Features.LevelPort
{
    // Watches the creative Level Browser. While a real level tile is highlighted it floats a nav
    // prompt in the game's own NavigationOverlay row — Options on a pad, O on keyboard. Pressing it
    // opens the game's UGC report popup repurposed as an Import / Export chooser
    // (LevelPortReportMenu). Persistent singleton spawned from Plugin.InitGameObjects.
    public class LevelBrowserPortPrompt : MonoBehaviour
    {
        public LevelBrowserPortPrompt(IntPtr ptr) : base(ptr) { }

        public static LevelBrowserPortPrompt Instance { get; private set; }

        private LB.LevelBrowserTileViewModel _tileVm;
        private NavPromptHandle _prompt;

        void Awake()
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // LevelBrowserTileViewModel.OnSelected postfix routes here
        public static void OnTileSelected(LB.LevelBrowserTileViewModel vm)
        {
            var inst = Instance;
            if (inst == null) return;
            inst._tileVm = vm;
        }

        void Update()
        {
            BetterFG.UI.WinDialogs.Tick();

            bool live = _tileVm != null && _tileVm.gameObject.activeInHierarchy && _tileVm.HasLevel;
            if (!live)
            {
                DestroyPrompt();
                return;
            }

            if (_prompt == null || !_prompt.IsAlive)
            {
                // NavPrompt.Report is just the prefab source; OwnGlyph swaps in our own key/pad
                // glyphs. trigger is the pad's pause/menu button (Options / Menu / Start) or O.
                _prompt = NavPromptCore.From(NavPrompt.Report)
                    .WithLabel("Import / Export", "bfg_levelport_prompt")
                    .InGameOverlay()
                    .AllowWhileUnfocused()
                    .OwnGlyph()
                    .PollActions(RewiredConsts.Action.Default_OpenInGameMenu)
                    .JoystickOnly()
                    .AlsoAcceptKey(KeyCode.O)
                    .SpawnOn(null);
            }

            if (_prompt != null && _prompt.IsPressed() && !LevelPortReportMenu.AnyOpen)
                StartCoroutine(LevelPortReportMenu.OpenRoutine(_tileVm.TileData).WrapToIl2Cpp());
        }

        private void DestroyPrompt()
        {
            _prompt?.Destroy();
            _prompt = null;
        }
    }
}
