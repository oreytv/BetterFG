using BetterFG.UI.Windows.Creative;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ScriptableObjects;

namespace BetterFG.Features.CreativeText
{
    [HarmonyPatch(typeof(LevelEditorCarrouselViewModel), nameof(LevelEditorCarrouselViewModel.ShowCarrousel))]
    internal static class CreativeTextCarouselInjectPatch
    {
        [HarmonyPrefix]
        public static void Prefix([HarmonyArgument(0)] Il2CppReferenceArray<LevelEditorObjectList> objectLists)
            => CreativeTextCarousel.Inject(objectLists);
    }

    [HarmonyPatch(typeof(LevelEditorCarrouselItemViewModel), nameof(LevelEditorCarrouselItemViewModel.OnClicked))]
    internal static class CreativeTextCarouselClickPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(LevelEditorCarrouselItemViewModel __instance)
        {
            if (!CreativeTextCarousel.IsOurs(__instance._itemData)) return true;
            Plugin.Log.LogInfo("TEXT picked out of the carousel, opening the text tool instead of placing anything");
            BatchEditWindow.OpenTextTool();
            return false;
        }
    }

}
