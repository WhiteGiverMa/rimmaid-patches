using System;
using System.Reflection;
using DMSE;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace WhiteGiver.DMSEWorldPerfFix;

/// <summary>
/// FIX 1 - read-only stage queries must not create alert states.
///
/// Upstream:
///   public int StageOf(MapParent parent) =&gt; StateFor(parent)?.stage ?? 0;
///   public bool RadarWarm(MapParent parent) { var s = StateFor(parent); return s == null || Now &gt;= s.radarWarmUntil; }
///
/// <c>StateFor</c> defaults to <c>create: true</c>, so <c>WorldComponent_BVRNetwork.ApplyAlertStages</c>
/// instantiates (and permanently scribes) one <see cref="AirDefenseSiteAlert"/> for every single settlement
/// on the planet that has a settlement-defense node, plus every site touched by
/// <c>AirspaceRestrictionUtility.Rebuild</c>. The created alert is always at its peaceful baseline
/// (<c>stage == baseline</c>, <c>local == regional == 0</c>, empty <c>records</c>/<c>waves</c>), which is
/// indistinguishable from "no state at all" for every consumer:
///   - StageOf / RadarWarm / DetectionFactor (baseline stage is exactly what a fresh Reset produces),
///   - AllStates (only used by the dev "LogStates" action, which then Decay()s a stateless baseline anyway),
///   - AirspaceRestrictionUtility (needs stage &gt;= 5; baselines are 1/2/3),
///   - ReportObservation / ReportHit / DevInject / DevAddPressure / DevSetStage / InspectFor, which all still
///     call StateFor(parent) with create:true and therefore materialise the state on the first real event.
///
/// This prefix answers the same queries with StateFor(parent, create: false) and falls back to the exact value
/// a fresh state would have had.
/// </summary>
internal static class AirDefenseQueryPatches
{
    internal static void Install(Harmony harmony, Type alertType)
    {
        MethodInfo stageOf = AccessTools.DeclaredMethod(alertType, "StageOf", new[] { typeof(MapParent) })
            ?? throw new MissingMethodException(alertType.FullName, "StageOf");
        MethodInfo radarWarm = AccessTools.DeclaredMethod(alertType, "RadarWarm", new[] { typeof(MapParent) })
            ?? throw new MissingMethodException(alertType.FullName, "RadarWarm");

        harmony.Patch(
            stageOf,
            prefix: new HarmonyMethod(typeof(AirDefenseQueryPatches), nameof(StageOfPrefix)));

        harmony.Patch(
            radarWarm,
            prefix: new HarmonyMethod(typeof(AirDefenseQueryPatches), nameof(RadarWarmPrefix)));
    }

    /// <summary>
    /// Same result as upstream, but never adds a site to <c>siteList</c>/<c>sites</c>.
    /// For a parent upstream would reject (null, destroyed, factionless or player) the answer is 0.
    /// For a parent without a live state the answer is the baseline a fresh Reset would assign.
    /// For a parent with a live state this still runs the same faction-change check as StateFor.
    /// </summary>
    private static bool StageOfPrefix(WorldComponent_FactionAirDefenseAlert __instance, MapParent parent, ref int __result)
    {
        if (parent == null || parent.Destroyed || parent.Faction == null || parent.Faction.IsPlayer)
        {
            __result = 0;
            return false;
        }

        AirDefenseSiteAlert? state = __instance.StateFor(parent, false);
        __result = state != null ? state.stage : WorldComponent_FactionAirDefenseAlert.BaselineFor(parent);
        return false;
    }

    /// <summary>
    /// Same result as upstream, but never creates a state. No state means "radar not warming up", which is
    /// exactly what upstream returns for a parent it rejects and what a freshly Reset state stores
    /// (<c>radarWarmUntil = -1</c>).
    /// </summary>
    private static bool RadarWarmPrefix(WorldComponent_FactionAirDefenseAlert __instance, MapParent parent, ref bool __result)
    {
        AirDefenseSiteAlert? state = __instance.StateFor(parent, false);
        __result = state == null || Find.TickManager.TicksGame >= state.radarWarmUntil;
        return false;
    }
}
