using System;
using BetterFG.UI;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Features.Replay
{
    internal static class ReplayWindowKit
    {
        public const float ROW = 22f;
        public const float PAD = 8f;
        public const float ARROW = 22f;
        public const float HEAD = 26f;
        public const float LABEL_W = 86f;

        public static readonly Color PANEL_BG = new Color(0.06f, 0.07f, 0.09f, 0.94f);
        public static readonly Color HEADER_BG = new Color(0.13f, 0.15f, 0.19f, 1f);
        public static readonly Color BTN_DARK = new Color(0.18f, 0.18f, 0.2f, 1f);
        public static readonly Color BTN_GREEN = new Color(0.22f, 0.42f, 0.26f, 1f);
        public static readonly Color BTN_RED = new Color(0.45f, 0.18f, 0.18f, 1f);
        public static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        public static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        public static readonly Color PICKABLE = new Color(1f, 1f, 1f, 0.07f);

        public static RectTransform Content(RectTransform window)
        {
            var go = new GameObject("Content");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(window, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static void Title(RectTransform content, float width, string text, Vector3 titlePosition)
        {
            var label = UGUIShip.CreateLabel(content, new Rect(PAD, 0f, width - PAD * 2f, HEAD), text,
                UIScale.FS, Color.white);
            label.fontStyle = FontStyle.Bold;
            label.transform.localScale = new Vector3(1.5f, 1.5953f, 1f);
            label.transform.localPosition = titlePosition;
        }

        public static void MainBackdrop(RectTransform window) =>
            Backdrop(window, "BetterFG.assets.ui.replay.mainwindow.png", 0.040f, 0.040f, 0.865f, 0.8067f);

        public static void EditBackdrop(RectTransform window) =>
            Backdrop(window, "BetterFG.assets.ui.replay.editwindow.png", 0.030f, 0.020f, 0.875f, 0.8067f);

        static void Backdrop(RectTransform window, string resource, float u0, float v0, float u1, float v1)
        {
            var tex = EmbeddedResourceandUnity.LoadTexture(resource);
            if (tex == null) return;

            var go = new GameObject("Backdrop");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(window, false);

            float du = u1 - u0;
            float dv = v1 - v0;
            rt.anchorMin = new Vector2(-u0 / du, 1f - (1f - v0) / dv);
            rt.anchorMax = new Vector2((1f - u0) / du, 1f + v0 / dv);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var raw = go.AddComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            rt.SetAsFirstSibling();
        }

        public const float SECTION_H = 26f;

        public static void Section(RectTransform content, float y, float width, string title)
        {
            var label = UGUIShip.CreateLabel(content, new Rect(PAD, y + 10f, width - PAD * 2f, 16f), title, UIScale.FS_SM, Color.white);
            label.fontStyle = FontStyle.Bold;
            label.transform.localScale = new Vector3(1.15f, 1.15f, 1f);
        }

        public static void Stripe(RectTransform content, float y, float width, int row) =>
            UGUIShip.CreatePanel(content, new Rect(0f, y, width, ROW), row % 2 == 0 ? ROW_EVEN : ROW_ODD, "Row");

        public static void Carousel(RectTransform content, float y, float width, int row,
            string label, string value, Action prev, Action next, Action centre = null)
        {
            Stripe(content, y, width, row);
            UGUIShip.CreateLabel(content, new Rect(PAD, y, LABEL_W, ROW), label, UIScale.FS_SM, HINT);

            float x = PAD + LABEL_W;
            float valueW = width - x - PAD - ARROW * 2f - 4f;

            UGUIShip.CreateButton(content, new Rect(x, y, ARROW, ROW), "<", BTN_DARK, Color.white, UIScale.FS_SM, prev);

            if (centre == null)
                UGUIShip.CreateLabel(content, new Rect(x + ARROW + 2f, y, valueW, ROW), value,
                    UIScale.FS_SM, Color.white, TextAnchor.MiddleCenter);
            else
                UGUIShip.CreateButton(content, new Rect(x + ARROW + 2f, y, valueW, ROW), value,
                    PICKABLE, Color.white, UIScale.FS_SM, centre);

            UGUIShip.CreateButton(content, new Rect(x + ARROW + valueW + 4f, y, ARROW, ROW), ">", BTN_DARK, Color.white, UIScale.FS_SM, next);
        }

        public static int Step(int index, int dir, int count, bool wrap)
        {
            if (count <= 0) return 0;
            index += dir;
            if (!wrap) return Mathf.Clamp(index, 0, count - 1);
            while (index < 0) index += count;
            return index % count;
        }

        public static float Step(float v, int dir, float step, float min, float max, bool wrap)
        {
            float nv = v + dir * step;
            float span = max - min;
            if (!wrap || span <= 0f) return Mathf.Clamp(nv, min, max);
            return min + Mathf.Repeat(nv - min, span);
        }
    }
}
