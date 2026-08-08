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
    public partial class MenuCustomizationApplication : MonoBehaviour
    {
        public static MenuCustomizationApplication Instance { get; private set; }

        public event Action<string> OnStatus;

        // set true on main menu enter, consumed (set false) once fg is applied on ShowMainMenu
        public bool _pendingFgReapply = true;

        // true = next ShowMainMenu reapply hits full UICanvas_Client_V2(Clone), then flips false
        public static bool _fullCanvasReapplyPending = false;

        void Awake() { Instance = this; MigrateOldBgKeys(); MigrateBgSplit(); }

        private static float ParseF(string key, float def) =>
            float.TryParse(SettingsService.Get(key, ""), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;
    }
}
