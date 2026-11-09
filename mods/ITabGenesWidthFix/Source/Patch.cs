using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WhiteGiver.ITabGenesWidthFix;

public static class Patch
{
    private static readonly AccessTools.FieldRef<InspectTabBase, Vector2> SizeField =
        AccessTools.FieldRefAccess<InspectTabBase, Vector2>("size");

    [HarmonyPatch(typeof(InspectTabBase), "UpdateSize")]
    public static class UpdateSize_Postfix
    {
        [HarmonyPostfix]
        public static void Postfix(InspectTabBase __instance)
        {
            if (__instance is not ITab_Genes) return;
            ref var size = ref SizeField(__instance);
            if (size.x > 0f) return;
            size.x = Mathf.Min(736f, UI.screenWidth);
        }
    }
}
