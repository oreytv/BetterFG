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
        // �� Button textures (cached) ������������������������������������������
        private static Sprite _btnSprite;
        private static Sprite _btnShineSprite;
        private static Sprite _radialGradCornerSprite;

        public static Sprite GetButtonSprite()
        {
            if (_btnSprite != null) return _btnSprite;
            try
            {
                var asm = ResourceAssembly;
                using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.general.button.png");
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                _btnSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception ex) { Log.LogError("UGUIShip: button.png load failed: " + ex.Message); }
            return _btnSprite;
        }

        private static Sprite GetButtonShineSprite()
        {
            if (_btnShineSprite != null) return _btnShineSprite;
            try
            {
                var asm = ResourceAssembly;
                using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.general.button_shine.png");
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                _btnShineSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(16, 16, 16, 16)
                );
            }
            catch (Exception ex) { Log.LogError("UGUIShip: button_shine.png load failed: " + ex.Message); }
            return _btnShineSprite;
        }

        public static Sprite GetRadialGradCornerSprite()
        {
            if (_radialGradCornerSprite != null) return _radialGradCornerSprite;
            try
            {
                var asm = ResourceAssembly;
                using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.general.radialgradcorner128.png");
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                _radialGradCornerSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0f, 1f));
            }
            catch (Exception ex) { Log.LogError("UGUIShip: radialgradcorner128.png load failed: " + ex.Message); }
            return _radialGradCornerSprite;
        }

        public static GameObject BuildShine(GameObject parent)
        {
            var shineSprite = GetButtonShineSprite();
            if (shineSprite == null) return null;

            var shineGo = new GameObject("Shine");
            shineGo.transform.SetParent(parent.transform, false);
            var shineRt = shineGo.AddComponent<RectTransform>();
            shineRt.anchorMin = Vector2.zero;
            shineRt.anchorMax = Vector2.one;
            shineRt.offsetMin = shineRt.offsetMax = Vector2.zero;
            shineRt.localScale = Vector3.one;
            var shineImg = shineGo.AddComponent<Image>();
            shineImg.sprite = shineSprite;
            shineImg.type = Image.Type.Sliced;
            shineImg.pixelsPerUnitMultiplier = 5f;
            shineImg.color = new Color(1f, 1f, 1f, 0.4f);
            shineImg.raycastTarget = false;
            shineGo.SetActive(true);
            RegisterShine(shineImg); // tint it + track for live tint changes
            return shineGo;
        }

        public static void WireShineHover(GameObject btn, GameObject shine)
        {
            var trigger = btn.GetComponent<EventTrigger>() ?? btn.AddComponent<EventTrigger>();
            var shineImg = shine.GetComponent<Image>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<BaseEventData>(_ =>
            {
                if (shineImg != null) { var t = Tint(); shineImg.color = new Color(t.r, t.g, t.b, 1f); }
            }));
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(new Action<BaseEventData>(_ =>
            {
                if (shineImg != null) { var t = Tint(); shineImg.color = new Color(t.r, t.g, t.b, 0.4f); }
            }));
            trigger.triggers.Add(exit);
        }
    }
}
