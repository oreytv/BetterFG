using System;
using System.Collections.Generic;
using BetterFG.UI.Windows.Creative;
using BetterFG.Utilities;
using FG.Common.CMS;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ScriptableObjects;
using TreeView;
using UnityEngine;

namespace BetterFG.Features.CreativeObjects
{
    public static class CreativeObjects
    {
        private sealed class Entry
        {
            public string Name;
            public string Description;
            public string Icon;
            public Action Picked;
            public Sprite Sprite;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static readonly Dictionary<IntPtr, Entry> _byElement = new Dictionary<IntPtr, Entry>();
        private static readonly List<UnityEngine.Object> _keepAlive = new List<UnityEngine.Object>();

        static CreativeObjects()
        {
            Register("Text", "Spell something out in blocks or stickers, a placed object per stroke",
                "BetterFG.assets.creativeobjects.text.png", BatchEditWindow.OpenTextTool);
        }

        public static void Register(string name, string description, string iconResource, Action picked) =>
            _entries.Add(new Entry { Name = name, Description = description, Icon = iconResource, Picked = picked });

        public static bool TakeOver(VariantTreeElement element, string how)
        {
            if (element == null || !_byElement.TryGetValue(element.Pointer, out var entry)) return false;
            Plugin.Log.LogInfo($"{entry.Name} picked out of the carousel ({how}), running its tool instead of placing anything");
            entry.Picked();
            return true;
        }

        public static void Inject(Il2CppReferenceArray<LevelEditorObjectList> lists)
        {
            if (_entries.Count == 0) return;

            var strings = CMSLoader.Instance._localisedStrings._localisedStrings;
            foreach (var entry in _entries)
            {
                strings[Key(entry, 'n')] = entry.Name;
                strings[Key(entry, 'd')] = entry.Description;
            }

            var seen = new HashSet<IntPtr>();

            var theme = ThemeManager.CurrentThemeData;
            var themeList = theme != null ? theme.ObjectList : null;
            if (themeList != null && seen.Add(themeList.Pointer)) InjectInto(themeList);

            for (int i = 0; lists != null && i < lists.Length; i++)
            {
                var list = lists[i];
                if (list != null && seen.Add(list.Pointer)) InjectInto(list);
            }
        }

        private static void InjectInto(LevelEditorObjectList list)
        {
            var flat = list.m_TreeElements;
            if (flat == null || flat.Count == 0)
            {
                Plugin.Log.LogWarning($"{list.name} has no m_TreeElements, nothing to slot our objects into");
                return;
            }

            int at = -1;
            int maxId = 0;
            VariantTreeElement sticker = null;
            for (int i = 0; i < flat.Count; i++)
            {
                var el = flat[i];
                if (el == null) continue;
                if (_byElement.ContainsKey(el.Pointer)) return;
                if (el.id > maxId) maxId = el.id;
                if (sticker == null && IsSticker(el)) { sticker = el; at = i; }
            }

            if (sticker == null)
            {
                Plugin.Log.LogWarning($"no sticker entry in {list.name}'s {flat.Count} tree elements, our objects stay out");
                return;
            }

            for (int i = 0; i < _entries.Count; i++)
                flat.Insert(at + i, Build(_entries[i], sticker, maxId + 1 + i));

            Plugin.Log.LogInfo($"{_entries.Count} custom object(s) into {list.name} at {at}, depth {sticker.depth}, "
                + $"{flat.Count} elements in the list now");
        }

        private static VariantTreeElement Build(Entry entry, VariantTreeElement sticker, int id)
        {
            var source = sticker.Variant.Owner;

            var podGuid = Il2CppSystem.Guid.NewGuid();
            var pod = ScriptableObject.CreateInstance(Il2CppType.Of<PlaceableObjectData>()).Cast<PlaceableObjectData>();
            pod.name = $"POD_BettrFG_{entry.Name}";
            pod.objectNameKey = Key(entry, 'n');
            pod.objectDescriptionKey = Key(entry, 'd');
            pod.category = source.category;
            pod.costHandler = source.costHandler;
            pod.rotationHandler = source.rotationHandler;
            pod.matchingCriteria = source.matchingCriteria;
            var icon = Icon(entry);
            pod.menuSprite = icon != null ? icon : source.menuSprite;
            pod.Guid = podGuid;
            pod.serializedGuid = podGuid.ToByteArray();
            pod.hideFlags = HideFlags.HideAndDontSave;

            var proto = UnityEngine.Object.Instantiate(sticker.Variant.Prefab);
            proto.SetActive(false);
            proto.name = $"Placeable_BettrFG_{entry.Name}";
            proto.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(proto);

            var variantGuid = Il2CppSystem.Guid.NewGuid();
            var variant = ScriptableObject.CreateInstance(Il2CppType.Of<PlaceableVariant_Prefab>())
                .Cast<PlaceableVariant_Prefab>();
            variant.name = $"BettrFG_{entry.Name}Variant";
            variant.prefab = proto;
            variant.Guid = variantGuid;
            variant.serializedGuid = variantGuid.ToByteArray();
            variant.hideFlags = HideFlags.HideAndDontSave;
            variant.SetOwner(pod);

            pod.objectVariants = new Il2CppReferenceArray<PlaceableVariant_Base>(1);
            pod.objectVariants[0] = variant;
            pod.defaultVariant = variant;

            var element = new VariantTreeElement(entry.Name, sticker.depth, id);
            element.Variant = variant;
            element.Guid = variantGuid;
            element.serializedGuid = variantGuid.ToByteArray();

            _byElement[element.Pointer] = entry;
            _keepAlive.Add(pod);
            _keepAlive.Add(variant);
            _keepAlive.Add(proto);

            var current = LevelEditorObjectList.CurrentObjects;
            if (current != null && current.IndexOf(pod) == -1)
                current.Insert(current.Cast<Il2CppSystem.Collections.Generic.List<PlaceableObjectData>>().Count, pod);

            return element;
        }

        private static string Key(Entry entry, char kind) => $"bfg_co_{entry.Name.ToLowerInvariant()}_{kind}";

        private static Sprite Icon(Entry entry)
        {
            if (entry.Icon == null) return entry.Sprite;
            entry.Sprite = EmbeddedResourceandUnity.LoadSprite(entry.Icon);
            if (entry.Sprite == null)
                Plugin.Log.LogWarning($"{entry.Icon} didn't load, {entry.Name} keeps whatever icon the sticker had");
            entry.Icon = null;
            return entry.Sprite;
        }

        private static bool IsSticker(VariantTreeElement element)
        {
            var variant = element.Variant;
            var owner = variant != null ? variant.Owner : null;
            if (owner == null) return false;
            if (owner.Category != LevelEditorPlaceableObject.Category.Decoration) return false;
            try
            {
                var prefab = owner.GetDefaultObject();
                return prefab != null && prefab.GetComponentInChildren<LevelEditorDecal>(true) != null;
            }
            catch { return false; }
        }
    }

    [HarmonyPatch(typeof(LevelEditorCarrouselViewModel), nameof(LevelEditorCarrouselViewModel.ShowCarrousel))]
    internal static class CreativeObjectsInjectPatch
    {
        [HarmonyPrefix]
        public static void Prefix([HarmonyArgument(0)] Il2CppReferenceArray<LevelEditorObjectList> objectLists)
            => CreativeObjects.Inject(objectLists);
    }

    [HarmonyPatch(typeof(FG.Common.LevelEditorManagerUI), nameof(FG.Common.LevelEditorManagerUI.OnItemSelected))]
    internal static class CreativeObjectsSelectPatch
    {
        [HarmonyPrefix]
        public static bool Prefix([HarmonyArgument(0)] CarrouselItemSelectedEvent evt)
            => evt == null || !CreativeObjects.TakeOver(evt.treeElement, "carousel select");
    }
}
