using System;
using System.Collections.Generic;
using System.Reflection;
using DMSE;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WhiteGiver.DMSEWorldPerfFix;

/// <summary>
/// FIX 3a - cache <c>MissileDoctrine.For(FactionDef, IEnumerable&lt;MissileDoctrineDef&gt;)</c>.
///
/// Upstream re-runs <c>def.GetModExtension&lt;MissileDoctrineExtension&gt;()</c> and then
/// <c>candidates.FirstOrDefault(d =&gt; d.IsDefaultFor(def.techLevel))</c> on every call. That method is on the
/// hot path of <c>SettlementAirDefenseProps.FireControl</c>, <c>WorldComponent_SettlementDefense.PropsFor</c>,
/// <c>WorldComponent_FactionAirDefenseAlert.RadarCapable</c>/<c>BaselineFor</c> and therefore runs hundreds of
/// times per BVR rebuild, each time allocating a closure for the predicate.
///
/// The cache only answers the canonical <c>MissileDoctrine.For(Faction)</c> call shape (the
/// <c>AllDefsListForReading</c> instance). Any other candidate sequence falls through to the untouched original
/// method, so callers that pass a filtered list keep their exact behaviour. Cache misses call the original
/// method (with a re-entrancy guard) so the doctrine selection logic is never duplicated here.
/// </summary>
internal static class DoctrineCachePatches
{
    [ThreadStatic]
    private static bool resolving;

    private static readonly Dictionary<FactionDef, MissileDoctrineDef?> Cache = new();
    private static int cachedDefCount = -1;
    private static World? cachedWorld;

    internal static void Install(Harmony harmony, Type doctrineType)
    {
        MethodInfo target = AccessTools.DeclaredMethod(
            doctrineType,
            "For",
            new[] { typeof(FactionDef), typeof(IEnumerable<MissileDoctrineDef>) })
            ?? throw new MissingMethodException(doctrineType.FullName, "For(FactionDef, IEnumerable<MissileDoctrineDef>)");

        harmony.Patch(
            target,
            prefix: new HarmonyMethod(typeof(DoctrineCachePatches), nameof(ForPrefix)));
    }

    private static bool ForPrefix(FactionDef def, IEnumerable<MissileDoctrineDef> candidates, ref MissileDoctrineDef? __result)
    {
        if (resolving || !ReferenceEquals(candidates, DefDatabase<MissileDoctrineDef>.AllDefsListForReading))
        {
            return true;
        }

        if (def == null)
        {
            __result = null;
            return false;
        }

        World? world = Find.World;
        int defCount = DefDatabase<MissileDoctrineDef>.DefCount;
        if (defCount != cachedDefCount || !ReferenceEquals(world, cachedWorld))
        {
            Cache.Clear();
            cachedDefCount = defCount;
            cachedWorld = world;
        }

        if (Cache.TryGetValue(def, out MissileDoctrineDef? cached))
        {
            __result = cached;
            return false;
        }

        resolving = true;
        try
        {
            __result = MissileDoctrine.For(def, candidates);
        }
        finally
        {
            resolving = false;
        }

        Cache[def] = __result;
        return false;
    }
}
