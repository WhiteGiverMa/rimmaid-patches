using System;
using System.Collections.Generic;
using DMS_Legion;
using Verse;

namespace WhiteGiver.DMSL.DrillingBargePerfFix;

/// <summary>
/// Harmony patch bodies. Every prefix that answers a query by itself only does so on the exact fast path
/// that upstream would have handled with a full-map or 21-cell scan; every other case falls through to the
/// original method so behaviour can never drift.
/// </summary>
internal static class DrillingBargePatches
{
    /// <summary>
    /// Replaces the per-cell <c>DefDatabase</c> lookup and comps walk of
    /// <c>CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource</c> with an O(1) set probe.
    /// </summary>
    internal static bool IsExcludedDeepResourcePrefix(ThingDef? resourceDef, ref bool __result)
    {
        __result = OverlayExclusionCache.IsExcluded(resourceDef);
        return false;
    }

    /// <summary>
    /// Returns the cached candidate list instead of yielding all <c>NumGridCells</c> cells.
    /// The upstream guard chain is evaluated first; on any deviation the original method runs unchanged.
    /// </summary>
    internal static bool PotentialWorkCellsGlobalPrefix(Pawn pawn, ref IEnumerable<IntVec3> __result)
    {
        if (pawn == null)
        {
            return true;
        }

        Map? map = pawn.Map;
        if (map == null || pawn.def == null || pawn.def.defName != DrillingBargePerfFix.DrillingBargeDefName)
        {
            return true;
        }

        DMSL_ModSettings? settings = DMSL_ModSettings.settings;
        if (settings == null || !settings.enableDrillingBargeDeepDrill)
        {
            return true;
        }

        if (map.Biome == null || !map.Biome.hasBedrock)
        {
            return true;
        }

        // Upstream's ShouldSkip already gates every job search on this tracker, so an empty answer here
        // matches upstream's effective result while skipping the pointless full-map enumeration.
        if (!DeepResourceGridTrackerComponent.HasAnyDeepResource(map))
        {
            __result = Array.Empty<IntVec3>();
            return false;
        }

        // List<IntVec3> makes JobGiver_Work take its IList<IntVec3> fast path (no enumerator allocation).
        __result = MapDrillCacheSet.Get(map).GetValuableCells();
        return false;
    }

    /// <summary>
    /// O(1) replacement for the 21-cell radial scan. The cached answer was produced by calling the upstream
    /// <c>TryGetDeepResource</c>, so this is the same boolean for every in-bounds and out-of-bounds cell.
    /// </summary>
    internal static bool CellHasValuableDeepResourcePrefix(IntVec3 center, Map map, Pawn? pawn, ref bool __result)
    {
        if (map == null || map.deepResourceGrid == null)
        {
            __result = false;
            return false;
        }

        bool applyExclusions = pawn != null
            && pawn.def != null
            && pawn.def.defName == DrillingBargePerfFix.DrillingBargeDefName;

        __result = MapDrillCacheSet.Get(map).HasValuable(center, applyExclusions);
        return false;
    }

    /// <summary>
    /// Disables the upstream 4-cells-per-tick cache rebuild. It only feeds
    /// <c>PotentialWorkCellsGlobal</c>, which is patched above and no longer reads it.
    /// </summary>
    internal static bool MapComponentTickPrefix()
    {
        return false;
    }

    internal static void DeepResourceGridSetAtPostfix(DeepResourceGrid __instance, IntVec3 c)
    {
        MapDrillCacheSet.NotifyCellChanged(__instance, c);
    }

    internal static void DeepResourceGridExposeDataPostfix(DeepResourceGrid __instance)
    {
        // Only a load can replace the grid contents behind our back. Saving calls ExposeData too and must
        // not throw the index away.
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            return;
        }

        MapDrillCacheSet.Invalidate(__instance);
    }
}
