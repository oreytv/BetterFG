using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace BetterFG.Utilities
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BfgPatchGateAttribute : Attribute
    {
        public readonly string Key;
        public readonly bool DefaultOn;

        public BfgPatchGateAttribute(string key, bool defaultOn = false)
        {
            Key = key;
            DefaultOn = defaultOn;
        }
    }

    public static class PatchGate
    {
        private static readonly Dictionary<string, List<Type>> _byKey = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> _defaults = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, List<MethodBase>> _live = new Dictionary<Type, List<MethodBase>>();

        public static void ResetForRepatch()
        {
            _byKey.Clear();
            _defaults.Clear();
            _live.Clear();
        }

        public static bool Claim(Type type)
        {
            var gate = type.GetCustomAttribute<BfgPatchGateAttribute>();
            if (gate == null) return false;

            if (!_byKey.TryGetValue(gate.Key, out var list))
            {
                list = new List<Type>();
                _byKey[gate.Key] = list;
                _defaults[gate.Key] = gate.DefaultOn;
            }
            list.Add(type);
            return true;
        }

        public static void ApplyInitial()
        {
            int held = 0, applied = 0;
            foreach (var kv in _byKey)
            {
                bool on = Services.SettingsService.Get(kv.Key, _defaults[kv.Key] ? "true" : "false") == "true";
                if (!on) { held += kv.Value.Count; continue; }
                foreach (var t in kv.Value) Install(t);
                applied += kv.Value.Count;
            }
            Plugin.Log.LogInfo($"gated patches: {applied} in, {held} left out until their toggle flips");
        }

        public static void SetActive(string key, bool on)
        {
            if (!_byKey.TryGetValue(key, out var types)) return;
            foreach (var t in types)
            {
                if (on) Install(t);
                else Remove(t);
            }
        }

        private static void Install(Type type)
        {
            if (_live.ContainsKey(type)) return;
            try
            {
                var originals = new PatchClassProcessor(Plugin.HarmonyInstance, type).Patch();
                var list = new List<MethodBase>();
                if (originals != null)
                    foreach (var m in originals) if (m != null) list.Add(m);
                _live[type] = list;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"gated patch {type.FullName} wouldn't go in: {ex.Message}");
            }
        }

        private static void Remove(Type type)
        {
            if (!_live.TryGetValue(type, out var originals)) return;
            _live.Remove(type);

            foreach (var original in originals)
                foreach (var patch in PatchMethodsOf(type))
                    try { Plugin.HarmonyInstance.Unpatch(original, patch); } catch { }
        }

        private static IEnumerable<MethodInfo> PatchMethodsOf(Type type)
        {
            foreach (var m in type.GetMethods(AccessTools.all))
            {
                if (m.GetCustomAttribute<HarmonyPrefix>() != null
                    || m.GetCustomAttribute<HarmonyPostfix>() != null
                    || m.GetCustomAttribute<HarmonyTranspiler>() != null
                    || m.GetCustomAttribute<HarmonyFinalizer>() != null)
                    yield return m;
            }

            foreach (var nested in type.GetNestedTypes(AccessTools.all))
                foreach (var m in PatchMethodsOf(nested))
                    yield return m;
        }
    }
}
