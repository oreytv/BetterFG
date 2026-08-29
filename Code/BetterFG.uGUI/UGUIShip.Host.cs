using System;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using Text = UnityEngine.UI.Text;

namespace BettrFG.uGUI
{
    public static partial class UGUIShip
    {
        public static ManualLogSource Log;
        public static Assembly ResourceAssembly;
        public static Func<string, Texture2D> LoadTexture;
        public static Func<string, Sprite> LoadSprite;
        public static Action PlayClick;
        public static Action PlayHover;
        public static Action<Canvas> RegisterCanvas;
        public static Action<TMPro.TMP_Text> ProtectFont;
        public static Action<Image> RegisterShine;
        public static Action<Image, Color> RegisterFill;
        public static Func<Color> Tint;
        public static Action<GameObject, string> AddTooltip;
    }
}
