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
        // held out of the game entirely until HandleServerStartRound, and pulled again on the way
        // back to the menu. for patches whose target is only ever called inside a live round.
        public readonly bool RoundOnly;

        public BfgPatchGateAttribute(string key, bool defaultOn = false, bool roundOnly = false)
        {
            Key = key;
            DefaultOn = defaultOn;
            RoundOnly = roundOnly;
        }
    }

    public static class PatchGate
    {
        private static readonly Dictionary<string, List<Type>> _byKey = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> _defaults = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly HashSet<string> _roundOnly = new HashSet<string>(StringComparer.Ordinal);
        private static bool _roundLive;
        private static readonly Dictionary<Type, List<MethodBase>> _live = new Dictionary<Type, List<MethodBase>>();

        public static void ResetForRepatch()
        {
            _byKey.Clear();
            _defaults.Clear();
            _roundOnly.Clear();
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
            if (gate.RoundOnly) _roundOnly.Add(gate.Key);
            list.Add(type);
            return true;
        }

        public static void ApplyInitial()
        {
            int held = 0, applied = 0;
            foreach (var kv in _byKey)
            {
                bool on = Enabled(kv.Key) && (!_roundOnly.Contains(kv.Key) || _roundLive);
                if (!on) { held += kv.Value.Count; continue; }
                foreach (var t in kv.Value) Install(t);
                applied += kv.Value.Count;
            }
            Plugin.Log.LogInfo($"gated patches: {applied} in, {held} left out until their toggle flips");
        }

        private static bool Enabled(string key) =>
            Services.SettingsService.Get(key, _defaults[key] ? "true" : "false") == "true";

        // driven off HandleServerStartRound / OnMainMenuEntered. a round-only patch is not merely
        // inert outside a round, it is not installed at all, so its target keeps the game's own
        // native code and costs no trampoline.
        public static void SetRoundActive(bool on)
        {
            if (_roundLive == on) return;
            _roundLive = on;

            int moved = 0;
            foreach (var key in _roundOnly)
            {
                if (!_byKey.TryGetValue(key, out var types)) continue;
                bool want = on && Enabled(key);
                foreach (var t in types)
                {
                    if (want) Install(t); else Remove(t);
                    moved++;
                }
            }
            if (moved > 0) Plugin.Log.LogInfo($"round-only patches {(on ? "in" : "out")}: {moved}");
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
