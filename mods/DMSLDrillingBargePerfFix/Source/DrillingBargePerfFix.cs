using System;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSL.DrillingBargePerfFix;

/// <summary>
/// Entry point for the patch. Applies every Harmony patch once and reports failures instead of
/// throwing, so a renamed upstream member only disables the affected optimization.
/// </summary>
public static class DrillingBargePerfFix
{
    public const string HarmonyId = "whitegiver.dmsl.drillingbargeperffix";

    /// <summary>Race defName used by the Legion drilling barge (kept in sync with DMS_Legion).</summary>
    internal const string DrillingBargeDefName = "DMSL_Mech_DrillingBarge";

    internal const string LogPrefix = "[DMS Legion Drilling Barge Perf Fix]";

    private static bool applied;

    public static void Apply()
    {
        if (applied)
        {
            return;
        }

        applied = true;

        Type? overlayType = AccessTools.TypeByName("DMS_Legion.CompDrillingBargeDeepResourceOverlay");
        Type? workGiverType = AccessTools.TypeByName("DMS_Legion.WorkGiver_DrillingBargeDeepDrill");
        Type? cellCacheType = AccessTools.TypeByName("DMS_Legion.DrillingBargeValidDrillCellsCacheComponent");

        if (overlayType == null || workGiverType == null)
        {
            Log.Error(LogPrefix + " could not resolve DMS_Legion types. Is 'The Dead Man's Switch - Legion' loaded? Patch is inactive.");
            return;
        }

        Harmony harmony = new(HarmonyId);
        int patched = 0;

        if (Patch(
                harmony,
                AccessTools.Method(overlayType, "IsExcludedDeepResource", new[] { typeof(ThingDef) }),
                prefix: new HarmonyMethod(typeof(DrillingBargePatches), nameof(DrillingBargePatches.IsExcludedDeepResourcePrefix))))
        {
            patched++;
        }

        if (!MapDrillCacheSet.EnsureTrackingAvailable())
        {
            Log.Error(LogPrefix + " cannot observe DeepResourceGrid edits; only the exclusion cache is active and the full-map scan is left untouched.");
            Log.Message(LogPrefix + " partially active (" + patched + " patches).");
            return;
        }

        bool workCellsPatched = Patch(
            harmony,
            AccessTools.Method(workGiverType, "PotentialWorkCellsGlobal", new[] { typeof(Pawn) }),
            prefix: new HarmonyMethod(typeof(DrillingBargePatches), nameof(DrillingBargePatches.PotentialWorkCellsGlobalPrefix)));
        if (workCellsPatched)
        {
            patched++;
        }

        bool cellCheckPatched = Patch(
            harmony,
            AccessTools.Method(
                workGiverType,
                "CellHasValuableDeepResource",
                new[] { typeof(IntVec3), typeof(Map), typeof(Pawn) }),
            prefix: new HarmonyMethod(typeof(DrillingBargePatches), nameof(DrillingBargePatches.CellHasValuableDeepResourcePrefix)));

        if (cellCheckPatched)
        {
            patched++;
        }

        // Only disable the upstream 4-cells-per-tick scan when the two replacements above are live.
        // Without them the component is still the only source of experimental work cells.
        if (workCellsPatched && cellCheckPatched && cellCacheType != null)
        {
            if (Patch(
                    harmony,
                    AccessTools.Method(cellCacheType, "MapComponentTick", Type.EmptyTypes),
                    prefix: new HarmonyMethod(typeof(DrillingBargePatches), nameof(DrillingBargePatches.MapComponentTickPrefix))))
            {
                patched++;
            }
        }

        if (Patch(
                harmony,
                AccessTools.Method(typeof(DeepResourceGrid), "SetAt", new[] { typeof(IntVec3), typeof(ThingDef), typeof(int) }),
                postfix: new HarmonyMethod(typeof(DrillingBargePatches), nameof(DrillingBargePatches.DeepResourceGridSetAtPostfix))))
        {
            patched++;
        }

        if (Patch(
                harmony,
                AccessTools.Method(typeof(DeepResourceGrid), "ExposeData", Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(DrillingBargePatches), nameof(DrillingBargePatches.DeepResourceGridExposeDataPostfix))))
        {
            patched++;
        }

        Log.Message(LogPrefix + " active (" + patched + " patches): cached drilling barge deep-resource lookups.");
    }

    private static bool Patch(Harmony harmony, System.Reflection.MethodInfo? target, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
    {
        if (target == null)
        {
            Log.Error(LogPrefix + " a target method was not found; one optimization is disabled. The upstream mod may have changed.");
            return false;
        }

        harmony.Patch(target, prefix: prefix, postfix: postfix);
        return true;
    }
}

public sealed class DMSLDrillingBargePerfFixMod : Mod
{
    public DMSLDrillingBargePerfFixMod(ModContentPack content) : base(content)
    {
        DrillingBargePerfFix.Apply();
    }
}
