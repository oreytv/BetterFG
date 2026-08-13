using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Features.CreativeText
{
    public static class CreativeTextFontRows
    {
        private static readonly Dictionary<string, SystemFont> _byLabel =
            new Dictionary<string, SystemFont>(StringComparer.Ordinal);

        public static void Watch(MonoBehaviour host, Dropdown dropdown, List<SystemFont> fonts)
        {
            if (host == null || dropdown == null) return;

            _byLabel.Clear();
            foreach (var font in fonts) _byLabel[font.Display] = font;

            var caption = dropdown.captionText;
            if (caption != null)
            {
                var current = SystemFonts.Preview(Lookup(caption.text));
                if (current != null) caption.font = current;
            }

            var trigger = dropdown.gameObject.GetComponent<EventTrigger>() ?? dropdown.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(new Action<BaseEventData>(_ =>
                host.StartCoroutine(StyleNextFrame(dropdown).WrapToIl2Cpp())));
            trigger.triggers.Add(entry);
        }

        private static IEnumerator StyleNextFrame(Dropdown dropdown)
        {
            yield return null;
            if (dropdown == null) yield break;

            var labels = dropdown.GetComponentsInChildren<Text>(true);
            if (labels == null) yield break;

            for (int i = 0; i < labels.Length; i++)
            {
                var label = labels[i];
                if (label == null) continue;
                var font = SystemFonts.Preview(Lookup(label.text));
                if (font != null) label.font = font;
            }
        }

        private static SystemFont Lookup(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            return _byLabel.TryGetValue(label, out var font) ? font : null;
        }
    }
}
