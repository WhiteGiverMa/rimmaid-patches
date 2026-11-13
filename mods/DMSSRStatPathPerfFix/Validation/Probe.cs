using System;
using DMSSR.Patches;
using HarmonyLib;
using Verse;

namespace DMSSRStatPathPerfFix.Validation;

/// <summary>
/// Exact work counters for the upstream code paths under test. The counters are Harmony patches
/// owned by the validation harness, never by the mod.
/// </summary>
internal static class Probe
{
    internal static int ClampCalls;
    internal static int HasIgnoreTerrainPenaltyCalls;
    internal static int MechCustomizationLookupCalls;
    internal static int MechCustomizationLookupScanLength;

    private static Harmony? harmony;

    internal static void Install()
    {
        if (harmony != null)
        {
            return;
        }

        harmony = new Harmony(Program.ValidationOwner);

        harmony.Patch(
            AccessTools.Method(typeof(AbilityPathCostUtility), "ClampPathCostForAbility")!,
            postfix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(ClampPostfix))));

        harmony.Patch(
            AccessTools.Method(typeof(AbilityPathCostUtility), "HasIgnoreTerrainMovePenalty")!,
            postfix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(HasIgnorePostfix))));

        harmony.Patch(
            AccessTools.Method(typeof(HediffSet), nameof(HediffSet.GetFirstHediffOfDef))!,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(GetFirstHediffOfDefPrefix))));

        // HediffSet.DirtyCache's vanilla body walks render caches and Unity texture atlases, none of
        // which exist in a headless host. Skipping the body keeps the invalidation signal (postfixes
        // still run) without dragging rendering into the fixture.
        harmony.Patch(
            AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache), Type.EmptyTypes)!,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(DirtyCachePrefix))));
    }

    internal static void Reset()
    {
        ClampCalls = 0;
        HasIgnoreTerrainPenaltyCalls = 0;
        MechCustomizationLookupCalls = 0;
        MechCustomizationLookupScanLength = 0;
    }

    internal static void Remove()
    {
        harmony?.UnpatchAll(Program.ValidationOwner);
        harmony = null;
    }

    private static void ClampPostfix()
    {
        ClampCalls++;
    }

    private static void HasIgnorePostfix(ref bool __result)
    {
        _ = __result;
        HasIgnoreTerrainPenaltyCalls++;
    }

    private static void GetFirstHediffOfDefPrefix(HediffSet __instance, HediffDef def)
    {
        if (!ReferenceEquals(def, DMSSR.Hediffs.DMS_HediffDefOf.DMS_MechCustomization))
        {
            return;
        }

        MechCustomizationLookupCalls++;
        MechCustomizationLookupScanLength = __instance.hediffs.Count;
    }

    private static bool DirtyCachePrefix()
    {
        return false;
    }
}
