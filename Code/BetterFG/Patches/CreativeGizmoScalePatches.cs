using BetterFG.Customization.UI;
using HarmonyLib;

namespace BetterFG.Patches
{
    [HarmonyPatch(typeof(LevelEditorWorldSpaceUI), nameof(LevelEditorWorldSpaceUI.Update))]
    internal static class CreativeGizmoScaleUpdatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorWorldSpaceUI __instance) => CreativeGizmoScale.Rescale(__instance);
    }
}
