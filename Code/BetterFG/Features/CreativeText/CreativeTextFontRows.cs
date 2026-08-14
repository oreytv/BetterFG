using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Features.CreativeText
{
    public static class CreativeTextFontRows
    {
        private const int BuildsPerPass = 6;

        private static readonly Dictionary<string, SystemFont> _byLabel =
            new Dictionary<string, SystemFont>(StringComparer.Ordinal);

        private static readonly HashSet<IntPtr> _hooked = new HashSet<IntPtr>();

        public static void Watch(MonoBehaviour host, Dropdown dropdown, List<SystemFont> fonts)
        {
            if (host == null || dropdown == null) return;

            _byLabel.Clear();
            foreach (var font in fonts) _byLabel[font.Display] = font;

            StyleOne(dropdown.captionText);

            var trigger = dropdown.gameObject.GetComponent<EventTrigger>() ?? dropdown.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(new Action<BaseEventData>(_ =>
                host.StartCoroutine(HookList(dropdown).WrapToIl2Cpp())));
            trigger.triggers.Add(entry);

            dropdown.onValueChanged.AddListener(new Action<int>(_ => StyleOne(dropdown.captionText)));
        }

        private static IEnumerator HookList(Dropdown dropdown)
        {
            yield return null;
            if (dropdown == null) yield break;

            var scroll = dropdown.GetComponentInChildren<ScrollRect>(true);
            if (scroll == null) yield break;

            _hooked.Clear();
            if (_hooked.Add(scroll.Pointer))
                scroll.onValueChanged.AddListener(new Action<Vector2>(_ => StyleVisible(scroll)));

            StyleVisible(scroll);
        }

        private static void StyleVisible(ScrollRect scroll)
        {
            if (scroll == null) return;
            var viewport = scroll.viewport;
            if (viewport == null) return;

            var labels = scroll.GetComponentsInChildren<Text>(true);
            if (labels == null) return;

            float reach = viewport.rect.height * 0.5f + 24f;
            int built = 0;
            for (int i = 0; i < labels.Length && built < BuildsPerPass; i++)
            {
                var label = labels[i];
                if (label == null || !_byLabel.ContainsKey(label.text)) continue;
                if (label.transform.Find("TmpLabel") != null) continue;
                if (Mathf.Abs(viewport.InverseTransformPoint(label.transform.position).y) > reach) continue;

                StyleOne(label);
                built++;
            }
        }

        private static void StyleOne(Text label)
        {
            if (label == null) return;
            var entry = Lookup(label.text);
            if (entry == null) return;

            var asset = CreativeText.FontAssetFor(entry.Path);
            if (asset == null)
            {
                Plugin.Log.LogWarning($"{entry.Display} wouldn't build into a TMP asset, that row stays plain");
                return;
            }

            UGUIShip.ReplaceTextWithTmp(label, label.text, asset);
        }

        private static SystemFont Lookup(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            return _byLabel.TryGetValue(label, out var font) ? font : null;
        }
    }
}
