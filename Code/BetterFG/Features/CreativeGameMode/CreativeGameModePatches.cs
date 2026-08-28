using FG.Common;
using HarmonyLib;

namespace BetterFG.Features.CreativeGameMode
{
    // A level's game mode lives in its JSON. When the game builds a loader from that JSON, swap the
    // mode id if this level has a queued change (set from the rulebook row, kept per share code).
    [HarmonyPatch(typeof(LevelLoader), nameof(LevelLoader.CreateLevelLoaderFromDownloadedJSON))]
    internal static class CreativeGameModeJsonLoadPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string jsonString, LevelAggregateDto dto)
        {
            CreativeGameModeRulebook.RewriteJsonForLoad(ref jsonString, dto?.ShareCode);
            BetterFG.Features.LevelPort.LevelPortImport.RewriteJsonForLoad(ref jsonString, dto?.ShareCode);
        }
    }

    // The Rulebook (Settings) screen rebuilds its row list through HandleChanged. Every rebuild we
    // slot our "Game Mode" row in just after the row it was cloned from.
    [HarmonyPatch(typeof(RulebookMenuCollectionBinding), nameof(RulebookMenuCollectionBinding.HandleChanged))]
    internal static class RulebookGameModeRowPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RulebookMenuCollectionBinding __instance)
            => CreativeGameModeRulebook.InjectRow(__instance);
    }

    // Left/right on a horizontal-list row (mouse arrows or carousel nav) both land here. On our
    // cloned row we cycle the selection ourselves and skip the native handler.
    [HarmonyPatch(typeof(LevelEditorRulebookEntryHorizontalListViewModel), nameof(LevelEditorRulebookEntryHorizontalListViewModel.OnIncrement))]
    internal static class RulebookGameModeIncrementPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(LevelEditorRulebookEntryHorizontalListViewModel __instance)
        {
            if (!CreativeGameModeRulebook.IsOurVm(__instance.GetInstanceID())) return true;
            CreativeGameModeRulebook.Cycle(1);
            return false;
        }
    }

    [HarmonyPatch(typeof(LevelEditorRulebookEntryHorizontalListViewModel), nameof(LevelEditorRulebookEntryHorizontalListViewModel.OnDecrement))]
    internal static class RulebookGameModeDecrementPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(LevelEditorRulebookEntryHorizontalListViewModel __instance)
        {
            if (!CreativeGameModeRulebook.IsOurVm(__instance.GetInstanceID())) return true;
            CreativeGameModeRulebook.Cycle(-1);
            return false;
        }
    }

    // Rulebook closed - if the game mode row was moved off the level's real mode, ask whether to
    // apply it (queues the swap + leaves the editor). Both the explicit CloseScreen() and the
    // OnClosed() lifecycle hook route here; OnRulebookClosed de-dupes per frame.
    [HarmonyPatch(typeof(LevelEditorRulebookViewModel), nameof(LevelEditorRulebookViewModel.CloseScreen))]
    internal static class RulebookGameModeCloseScreenPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => CreativeGameModeRulebook.OnRulebookClosed("CloseScreen");
    }

    [HarmonyPatch(typeof(LevelEditorRulebookViewModel), nameof(LevelEditorRulebookViewModel.OnClosed))]
    internal static class RulebookGameModeOnClosedPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => CreativeGameModeRulebook.OnRulebookClosed("OnClosed");
    }
}
