using System.Collections.Generic;
using CeleTech.Base;
using RimWorld;
using Verse;

namespace WhiteGiver.CeleTechTurretUpgradePerfFix;

/// <summary>
/// Harmony patch bodies. Every fast path only answers "no job" when the tracker proves that the upstream
/// validator would reject every candidate on the map; all other calls fall through to the original method
/// unchanged, so job choice, reservation, resource lookup and upgrade order remain upstream behaviour.
/// </summary>
internal static class TurretUpgradePatches
{
    /// <summary>
    /// <c>JobGiver_Work.PawnCanUseWorkGiver</c> runs once per work giver before
    /// <c>NonScanJob</c>/<c>PotentialWorkThingsGlobal</c>/<c>GenClosest</c>. When the map has no queued turret
    /// upgrade, <c>WorkGiver_TurretUpgrade</c> cannot produce a job, so returning false here skips the entire
    /// scan (and <c>WhileYouAreNearby</c> gets the same answer).
    /// </summary>
    internal static void PawnCanUseWorkGiverPostfix(Pawn pawn, WorkGiver giver, ref bool __result)
    {
        if (!__result || giver is not WorkGiver_TurretUpgrade || pawn == null)
        {
            return;
        }

        Map? map = pawn.Map;
        if (map == null)
        {
            // Preserve the upstream unspawned-pawn path instead of silently answering "no job".
            return;
        }

        if (!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(map))
        {
            __result = false;
        }
    }

    /// <summary>
    /// Replaces the unbounded <c>null</c> search set with the exact list of spawned turrets that have a queued
    /// upgrade. <c>JobGiver_Work</c> then passes this list to <c>GenClosest.ClosestThingReachable</c> as
    /// <c>customGlobalSearchSet</c>, so the global phase examines 0..n turrets instead of every wall, conduit,
    /// door, shelf and building on the map.
    ///
    /// Returning <c>true</c> (run original) when the map cannot be enumerated preserves the upstream answer
    /// in that edge case.
    /// </summary>
    internal static bool PotentialWorkThingsGlobalPrefix(WorkGiver_Scanner __instance, Pawn pawn, ref IEnumerable<Thing> __result)
    {
        if (__instance is not WorkGiver_TurretUpgrade || pawn == null)
        {
            return true;
        }

        IReadOnlyList<Thing>? queued = MapTurretUpgradeTracker.GetQueuedTurrets(pawn.Map);
        if (queued == null)
        {
            return true;
        }

        __result = queued;
        return false;
    }

    /// <summary>
    /// Covers direct callers that do not go through <c>JobGiver_Work.PawnCanUseWorkGiver</c>: the right-click
    /// float menu, emergency prioritised work and other mods. If no turret on the map has a queued upgrade,
    /// the upstream body can only return false, so answer it without touching the comp or the reservation
    /// manager.
    /// </summary>
    internal static bool HasJobOnThingPrefix(Pawn pawn, ref bool __result)
    {
        if (pawn == null)
        {
            return true;
        }

        Map? map = pawn.Map;
        if (map == null)
        {
            return true;
        }

        if (!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(map))
        {
            __result = false;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Registers newly spawned/loaded turret comps on their map. This is the incremental half of the registry;
    /// the lazy lister scan in <see cref="MapTurretUpgradeTracker"/> handles pre-existing maps.
    /// </summary>
    internal static void PostSpawnSetupPostfix(Comp_TurretUpgrade __instance)
    {
        MapTurretUpgradeTracker.Register(__instance);
    }
}
