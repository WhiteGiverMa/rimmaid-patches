using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMSE;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WhiteGiver.DMSEWorldPerfFix;

/// <summary>
/// FIX 2 - stop evaluating (and stop saving) quiescent baseline alert sites.
///
/// Upstream <c>WorldComponentTick</c> runs <c>Prune(now); EvaluateAll(now);</c> every 250 ticks. Both are
/// unconditional over <c>siteList</c>:
///   - <c>Prune</c> allocates two <c>RemoveAll</c> closures per site (2,056 delegates with 1,028 idle sites),
///   - <c>EvaluateAll</c> calls <c>Evaluate</c> for every idle site, which walks ParentOf/Decay, recomputes
///     <c>BaselineFor</c> (GetComponent + RadarCapable + MissileDoctrine) and <c>RadarCapable</c>,
///   - and crucially <c>Prune</c> never removes a site that has decayed back to its peaceful baseline, so
///     every site materialised by a read-only stage query stays in the list (and in the save file) forever.
///
/// This patch replaces the tick with the same algorithm, but:
///   - <c>siteList.Count == 0</c> is an O(1) return,
///   - record/wave pruning uses allocation-free backward loops,
///   - a site that is provably at rest (see <see cref="IsQuiescent"/>) is removed from
///     <c>siteList</c>/<c>sites</c> instead of being evaluated forever,
///   - the remaining (active) sites are still evaluated exactly once per 250 ticks, in the original order,
///     so no evaluation is ever delayed or skipped.
/// </summary>
internal static class AirDefenseTickPatches
{
    private const int EvaluateIntervalTicks = 250;

    private static readonly ConditionalWeakTable<WorldComponent_FactionAirDefenseAlert, Access> AccessCache = new();

    internal static void Install(Harmony harmony, Type alertType)
    {
        MethodInfo tick = AccessTools.DeclaredMethod(alertType, "WorldComponentTick", Type.EmptyTypes)
            ?? throw new MissingMethodException(alertType.FullName, "WorldComponentTick");

        harmony.Patch(
            tick,
            prefix: new HarmonyMethod(typeof(AirDefenseTickPatches), nameof(WorldComponentTickPrefix)));

        // Defence in depth: if another patch or a future DMSE build calls Prune directly, it gets the
        // allocation-free, hibernating version as well. Both entry points share the same core.
        // Fail the install (and keep vanilla DMSE) rather than throwing every 250 ticks if a future DMSE
        // renames a member this patch has to reach.
        _ = AccessTools.DeclaredField(alertType, "siteList")
            ?? throw new MissingFieldException(alertType.FullName, "siteList");
        _ = AccessTools.DeclaredField(alertType, "sites")
            ?? throw new MissingFieldException(alertType.FullName, "sites");
        _ = AccessTools.DeclaredField(alertType, "neutralWarned")
            ?? throw new MissingFieldException(alertType.FullName, "neutralWarned");
        _ = AccessTools.DeclaredMethod(alertType, "ParentOf", new[] { typeof(AirDefenseSiteAlert) })
            ?? throw new MissingMethodException(alertType.FullName, "ParentOf");
        _ = AccessTools.DeclaredMethod(alertType, "EvaluateAll", new[] { typeof(int) })
            ?? throw new MissingMethodException(alertType.FullName, "EvaluateAll");

        MethodInfo? prune = AccessTools.DeclaredMethod(alertType, "Prune", new[] { typeof(int) });
        if (prune != null)
        {
            harmony.Patch(
                prune,
                prefix: new HarmonyMethod(typeof(AirDefenseTickPatches), nameof(PrunePrefix)));
        }
    }

    private static bool WorldComponentTickPrefix(WorldComponent_FactionAirDefenseAlert __instance)
    {
        Access access = AccessCache.GetValue(__instance, static instance => new Access(instance));

        int now = Find.TickManager.TicksGame;
        if (now % EvaluateIntervalTicks != 0)
        {
            return false;
        }

        if (!WorldComponent_FactionAirDefenseAlert.Enabled)
        {
            return false;
        }

        List<AirDefenseSiteAlert>? siteList = access.GetSiteList(__instance);
        if (siteList == null || siteList.Count == 0)
        {
            access.PruneNeutralWarned(__instance);
            return false;
        }

        PruneCore(__instance, access, now);
        if (siteList.Count > 0)
        {
            access.EvaluateAll(now);
        }

        return false;
    }

    private static bool PrunePrefix(WorldComponent_FactionAirDefenseAlert __instance, int now)
    {
        PruneCore(__instance, AccessCache.GetValue(__instance, static instance => new Access(instance)), now);
        return false;
    }

    /// <summary>
    /// Upstream Prune, minus the two per-site closures, plus hibernation of provably idle baseline sites.
    /// </summary>
    private static void PruneCore(WorldComponent_FactionAirDefenseAlert instance, Access access, int now)
    {
        List<AirDefenseSiteAlert>? siteList = access.GetSiteList(instance);
        if (siteList == null)
        {
            return;
        }

        Dictionary<int, AirDefenseSiteAlert>? sites = access.GetSites(instance);
        AirDefenseAlertTuningDef tuning = AirDefenseAlertTuningDef.Current;
        int keep = Math.Max(tuning.attackWindowTicks, tuning.salvoGroupTicks * 4) + 60000;
        int attackWindow = tuning.attackWindowTicks;

        for (int i = siteList.Count - 1; i >= 0; i--)
        {
            AirDefenseSiteAlert state = siteList[i];
            MapParent? parent = access.ParentOf(state);

            if (parent == null || state.faction == null || parent.Faction == null || parent.Faction.IsPlayer)
            {
                sites?.Remove(state.siteId);
                siteList.RemoveAt(i);
                continue;
            }

            List<AirDefenseAlertRecord>? records = state.records;
            if (records != null)
            {
                for (int r = records.Count - 1; r >= 0; r--)
                {
                    if (now - records[r].tick > keep)
                    {
                        records.RemoveAt(r);
                    }
                }
            }

            List<AirDefenseWaveRecord>? waves = state.waves;
            if (waves != null)
            {
                for (int w = waves.Count - 1; w >= 0; w--)
                {
                    if (now - waves[w].tick > attackWindow)
                    {
                        waves.RemoveAt(w);
                    }
                }
            }

            if (IsQuiescent(state, parent, now, tuning))
            {
                sites?.Remove(state.siteId);
                siteList.RemoveAt(i);
            }
        }

        access.PruneNeutralWarned(instance);
    }

    /// <summary>
    /// True when removing the state and lazily recreating it through StateFor(parent) on the next real
    /// observation produces exactly the same observable behaviour:
    ///
    ///   - faction still matches (a mismatch is Reset() by Evaluate, so let Evaluate do it first),
    ///   - no alert records and no waves left (both lists are already pruned above),
    ///   - zero local and regional pressure,
    ///   - no blockade pending (blockadeAt &lt; 0) and not blockading (stage == baseline &lt;= 4),
    ///   - no direct-threat intel and no radar warm-up window,
    ///   - no escalation warning has been shown (warnedStage == 0),
    ///   - stage and cached baseline both equal BaselineFor(parent), i.e. the state is at rest.
    ///
    /// For such a state a fresh Reset(now) is indistinguishable: same stage, same baseline, same empty
    /// lists, same -1 timers, same zero pressure. Evaluate would only re-run Decay() (no-op at 0) and
    /// NextStage() (which returns the baseline floor because threat is 0 and current == baseline).
    /// </summary>
    private static bool IsQuiescent(AirDefenseSiteAlert state, MapParent parent, int now, AirDefenseAlertTuningDef tuning)
    {
        if (state.faction != parent.Faction)
        {
            return false;
        }

        if (state.records != null && state.records.Count != 0)
        {
            return false;
        }

        if (state.waves != null && state.waves.Count != 0)
        {
            return false;
        }

        if (state.local > 0f || state.regional > 0f)
        {
            return false;
        }

        if (state.blockadeAt >= 0 || state.stage >= 5)
        {
            return false;
        }

        if (now <= state.directThreatUntil || now < state.radarWarmUntil)
        {
            return false;
        }

        if (state.warnedStage != 0)
        {
            return false;
        }

        int baseline = FastBaselineFor(parent, tuning);
        if (baseline >= 5)
        {
            return false;
        }

        return state.stage == baseline && state.baseline == baseline;
    }

    /// <summary>
    /// Allocation-free mirror of <c>WorldComponent_FactionAirDefenseAlert.BaselineFor</c>.
    /// The upstream method calls <c>WorldObject.GetComponent&lt;WorldObjectComp_AirDefense&gt;()</c>, which in
    /// RimWorld 1.7 is a LINQ query and therefore allocates one iterator per call - exactly the per-site garbage
    /// this fix exists to remove. The logic is identical; the only path that cannot be answered from cached
    /// data (a site that owns a map, i.e. the player is currently on it) falls back to the real method, so the
    /// uncommon case keeps upstream's exact behaviour. GREEN's StageOf parity sweep and the hibernation tests
    /// cover tribal (1), radar (2) and military (3) baselines.
    /// </summary>
    private static int FastBaselineFor(MapParent parent, AirDefenseAlertTuningDef tuning)
    {
        if (HasAirDefenseComponent(parent))
        {
            return tuning.baselineMilitary;
        }

        return FastRadarCapable(parent) ? tuning.baselineRadar : tuning.baselineNoRadar;
    }

    private static bool FastRadarCapable(MapParent parent)
    {
        if (parent == null || parent.Faction == null || parent.Faction.IsPlayer)
        {
            return false;
        }

        if (HasAirDefenseComponent(parent))
        {
            return true;
        }

        if (WorldComponent_SettlementDefense.PropsFor(parent.Faction) != null)
        {
            return true;
        }

        if (!parent.HasMap)
        {
            return false;
        }

        return WorldComponent_FactionAirDefenseAlert.RadarCapable(parent);
    }

    private static bool HasAirDefenseComponent(MapParent parent)
    {
        List<WorldObjectComp>? comps = parent.AllComps;
        if (comps == null)
        {
            return parent.GetComponent<WorldObjectComp_AirDefense>() != null;
        }

        for (int i = 0; i < comps.Count; i++)
        {
            if (comps[i] is WorldObjectComp_AirDefense)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Cached reflection surface for one alert component instance.</summary>
    private sealed class Access
    {
        private readonly FieldInfo siteListField;
        private readonly FieldInfo sitesField;
        private readonly FieldInfo neutralWarnedField;

        internal readonly Func<AirDefenseSiteAlert, MapParent?> ParentOf;
        internal readonly Action<int> EvaluateAll;

        internal Access(WorldComponent_FactionAirDefenseAlert instance)
        {
            Type type = instance.GetType();
            siteListField = AccessTools.DeclaredField(type, "siteList")
                ?? throw new MissingFieldException(type.FullName, "siteList");
            sitesField = AccessTools.DeclaredField(type, "sites")
                ?? throw new MissingFieldException(type.FullName, "sites");
            neutralWarnedField = AccessTools.DeclaredField(type, "neutralWarned")
                ?? throw new MissingFieldException(type.FullName, "neutralWarned");

            MethodInfo parentOf = AccessTools.DeclaredMethod(type, "ParentOf", new[] { typeof(AirDefenseSiteAlert) })
                ?? throw new MissingMethodException(type.FullName, "ParentOf");
            MethodInfo evaluateAll = AccessTools.DeclaredMethod(type, "EvaluateAll", new[] { typeof(int) })
                ?? throw new MissingMethodException(type.FullName, "EvaluateAll");

            ParentOf = Bind<Func<AirDefenseSiteAlert, MapParent?>>(parentOf, instance);
            EvaluateAll = Bind<Action<int>>(evaluateAll, instance);
        }

        /// <summary>
        /// Private instance methods stay callable through a bound delegate; if a runtime refuses that, fall
        /// back to reflection so the patch still behaves correctly, only slower.
        /// </summary>
        private static T Bind<T>(MethodInfo method, object instance) where T : Delegate
        {
            try
            {
                return (T)method.CreateDelegate(typeof(T), instance);
            }
            catch (Exception)
            {
                Delegate? bound = Delegate.CreateDelegate(typeof(T), instance, method, throwOnBindFailure: false);
                if (bound == null)
                {
                    throw new MissingMethodException(method.DeclaringType?.FullName, method.Name);
                }

                return (T)bound;
            }
        }

        internal List<AirDefenseSiteAlert>? GetSiteList(WorldComponent_FactionAirDefenseAlert instance)
        {
            return siteListField.GetValue(instance) as List<AirDefenseSiteAlert>;
        }

        internal Dictionary<int, AirDefenseSiteAlert>? GetSites(WorldComponent_FactionAirDefenseAlert instance)
        {
            return sitesField.GetValue(instance) as Dictionary<int, AirDefenseSiteAlert>;
        }

        internal void PruneNeutralWarned(WorldComponent_FactionAirDefenseAlert instance)
        {
            if (neutralWarnedField.GetValue(instance) is not List<Faction> neutralWarned)
            {
                return;
            }

            for (int i = neutralWarned.Count - 1; i >= 0; i--)
            {
                if (neutralWarned[i] == null)
                {
                    neutralWarned.RemoveAt(i);
                }
            }
        }
    }
}
