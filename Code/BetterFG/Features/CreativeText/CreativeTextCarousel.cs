using System;
using System.Collections.Generic;
using FG.Common.CMS;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ScriptableObjects;
using TreeView;
using UnityEngine;

namespace BetterFG.Features.CreativeText
{
    public static class CreativeTextCarousel
    {
        public const string EntryName = "Text";
        private const string NameKey = "bfg_creative_text_name";

        private static readonly HashSet<IntPtr> _ourElements = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> _ourPods = new HashSet<IntPtr>();
        private static readonly List<PlaceableObjectData> _keepAlive = new List<PlaceableObjectData>();
        private static readonly List<PlaceableVariant_Base> _keepAliveVariants = new List<PlaceableVariant_Base>();

        private static Sprite _icon;

        private static Sprite Icon()
        {
            if (_icon != null) return _icon;
            _icon = BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.creativeobjects.text.png");
            if (_icon == null) Plugin.Log.LogWarning("text.png didn't load, TEXT keeps whatever icon the sticker had");
            return _icon;
        }

        public static bool IsOurs(VariantTreeElement element) =>
            element != null && _ourElements.Contains(element.Pointer);

        public static bool IsOurs(PlaceableObjectData pod) =>
            pod != null && _ourPods.Contains(pod.Pointer);

        public static void Inject(Il2CppReferenceArray<LevelEditorObjectList> lists)
        {
            CMSLoader.Instance._localisedStrings._localisedStrings[NameKey] = EntryName;

            var seen = new HashSet<IntPtr>();

            var theme = ThemeManager.CurrentThemeData;
            var themeList = theme != null ? theme.ObjectList : null;
            if (themeList != null && seen.Add(themeList.Pointer)) InjectInto(themeList);

            if (lists == null) return;
            for (int i = 0; i < lists.Length; i++)
            {
                var list = lists[i];
                if (list == null || !seen.Add(list.Pointer)) continue;
                InjectInto(list);
            }
        }

        private static void InjectInto(LevelEditorObjectList list)
        {
            var flat = list.m_TreeElements;
            if (flat == null || flat.Count == 0)
            {
                Plugin.Log.LogWarning($"{list.name} has no m_TreeElements to slot TEXT into");
                return;
            }

            int at = -1;
            int maxId = 0;
            VariantTreeElement sticker = null;
            for (int i = 0; i < flat.Count; i++)
            {
                var el = flat[i];
                if (el == null) continue;
                if (_ourElements.Contains(el.Pointer)) return;
                if (el.id > maxId) maxId = el.id;
                if (sticker != null) continue;
                if (!IsSticker(el)) continue;
                sticker = el;
                at = i;
            }

            if (sticker == null)
            {
                Plugin.Log.LogWarning($"no sticker entry found in {list.name}'s {flat.Count} tree elements, TEXT stays out");
                return;
            }

            var owner = sticker.Variant.Owner;
            var podGuid = Il2CppSystem.Guid.NewGuid();
            var podClone = UnityEngine.Object.Instantiate(owner);
            podClone.name = "POD_BettrFG_Text";
            podClone.objectNameKey = NameKey;
            podClone.menuSprite = Icon();
            podClone.Guid = podGuid;
            podClone.serializedGuid = podGuid.ToByteArray();
            podClone.hideFlags = HideFlags.HideAndDontSave;

            var variantGuid = Il2CppSystem.Guid.NewGuid();
            var variantClone = UnityEngine.Object.Instantiate(sticker.Variant);
            variantClone.name = "BettrFG_TextVariant";
            variantClone.Guid = variantGuid;
            variantClone.serializedGuid = variantGuid.ToByteArray();
            variantClone.hideFlags = HideFlags.HideAndDontSave;
            variantClone._owner = podClone;

            string detached = null;
            var nested = variantClone.TryCast<PlaceableVariant_POD>();
            if (nested != null && nested._POD != null)
            {
                detached = nested._POD.name;
                var innerGuid = Il2CppSystem.Guid.NewGuid();
                var innerClone = UnityEngine.Object.Instantiate(nested._POD);
                innerClone.name = "POD_BettrFG_TextDecal";
                innerClone.objectNameKey = NameKey;
                innerClone.menuSprite = Icon();
                innerClone.Guid = innerGuid;
                innerClone.serializedGuid = innerGuid.ToByteArray();
                innerClone.hideFlags = HideFlags.HideAndDontSave;
                innerClone._owner = podClone;
                nested._POD = innerClone;
                _keepAlive.Add(innerClone);
            }

            var element = new VariantTreeElement(EntryName, sticker.depth, maxId + 1);
            element.Variant = variantClone;
            element.Guid = variantGuid;
            element.serializedGuid = variantGuid.ToByteArray();
            flat.Insert(at, element);

            _ourElements.Add(element.Pointer);
            _ourPods.Add(podClone.Pointer);
            _keepAlive.Add(podClone);
            _keepAliveVariants.Add(variantClone);

            var current = LevelEditorObjectList.CurrentObjects;
            if (current != null && current.IndexOf(podClone) == -1)
                current.Insert(current.Cast<Il2CppSystem.Collections.Generic.List<PlaceableObjectData>>().Count, podClone);

            Plugin.Log.LogInfo($"TEXT into {list.name} at {at} (depth {sticker.depth}, id {maxId + 1}), ahead of {owner.name}, "
                + $"{flat.Count} elements in the list now. variant is {variantClone.GetIl2CppType().Name}, guid {variantGuid} instead of {sticker.Guid}"
                + (detached != null ? $", own copy of {detached} so the sticker keeps its own name" : ", nothing nested to detach"));
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
}
