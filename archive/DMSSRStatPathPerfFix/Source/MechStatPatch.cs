using System;
using System.Collections.Generic;
using System.Reflection;
using DMSSR.Hediffs;
using DMSSR.Patches;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    /// <summary>
    /// DMSSR's StatWorker.GetValueUnfinalized postfix does its work in this order:
    ///
    ///   1. req.HasThing
    ///   2. req.Thing is Pawn
    ///   3. RaceProps.IsMechanoid
    ///   4. TryGetCompFast  -> HediffSet.GetFirstHediffOfDef(DMS_MechCustomization)  [O(hediff count)]
    ///   5. read StatWorker.stat
    ///   6. compare it against nine HashSet&lt;StatDef&gt; sets
    ///
    /// So every stat query for every mechanoid (armour, market value, work speed, ...) walks the whole
    /// hediff list before it even looks at which stat is being asked for. We keep the same work but
    /// move the cheap questions first and make the hediff lookup a cache read:
    ///
    ///   * a prefix on Postfix returns false unless the stat is one of the customization stats and the
    ///     thing is a mechanoid pawn. In every skipped case upstream's body was a pure no-op, so the
    ///     result is identical, just without the scan.
    ///   * a prefix on TryGetCompFast answers from HediffFlagCache, which is invalidated by
    ///     HediffSet.DirtyCache. This keeps the remaining (rare) relevant-stat path O(1) too.
    ///
    /// The set of "customization stats" is read from DMSSR's own private HashSet fields at first use
    /// (after the game's DefOfs are bound), so a future DMSSR that adds a stat keeps working. If those
    /// sets cannot be read we fall back to the same ten StatDefOf entries this build uses.
    /// </summary>
    internal static class MechStatPatch
    {
        private static readonly AccessTools.FieldRef<StatWorker, StatDef> StatField =
            AccessTools.FieldRefAccess<StatWorker, StatDef>("stat");

        internal static bool GateInstalled { get; private set; }

        internal static bool CompCacheInstalled { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            MethodInfo? postfix = AccessTools.Method(typeof(Patch_MechCustomization_ApplyStatPreFinalize), "Postfix");
            if (postfix != null && HasExpectedSignature(postfix))
            {
                try
                {
                    harmony.Patch(postfix, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(MechStatPatch), nameof(PostfixGate))));
                    GateInstalled = true;
                }
                catch (Exception exception)
                {
                    SafeLog.Warning("stat gate could not be installed; the hediff lookup cache is still active.\n" + exception);
                }
            }
            else
            {
                SafeLog.Warning("DMSSR's StatWorker postfix signature changed; the stat gate was not installed.");
            }

            MethodInfo? tryGetComp = AccessTools.Method(
                typeof(Patch_MechCustomization_ApplyStatPreFinalize),
                "TryGetCompFast",
                new[] { typeof(Pawn), typeof(HediffComp_MechCustomization).MakeByRefType() });
            if (tryGetComp == null)
            {
                SafeLog.Warning("DMSSR's TryGetCompFast was not found; the mech customization lookup stays O(hediffs).");
                return;
            }

            try
            {
                harmony.Patch(tryGetComp, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(MechStatPatch), nameof(TryGetCompFastPrefix))));
                CompCacheInstalled = true;
            }
            catch (Exception exception)
            {
                SafeLog.Warning("mech customization lookup cache could not be installed.\n" + exception);
            }
        }

        private static bool HasExpectedSignature(MethodInfo postfix)
        {
            ParameterInfo[] parameters = postfix.GetParameters();
            return parameters.Length >= 2
                && parameters[0].ParameterType == typeof(StatWorker)
                && parameters[1].ParameterType == typeof(StatRequest);
        }

        /// <summary>
        /// __0/__1 bind by position to the patched postfix's parameters (StatWorker, StatRequest).
        /// __instance cannot be used here because the patched method itself is static.
        /// </summary>
        private static bool PostfixGate(StatWorker __0, StatRequest __1)
        {
            if (__0 == null)
            {
                return false;
            }

            StatDef? stat = StatField(__0);
            if (stat == null || !MechStatSet.MayAffect(stat))
            {
                return false;
            }

            if (!__1.HasThing)
            {
                return false;
            }

            if (!(__1.Thing is Pawn pawn))
            {
                return false;
            }

            RaceProperties? race = pawn.RaceProps;
            return race != null && race.IsMechanoid;
        }

        private static bool TryGetCompFastPrefix(
            Pawn pawn,
            ref HediffComp_MechCustomization? comp,
            ref bool __result)
        {
            comp = HediffFlagCache.GetMechCustomization(pawn);
            __result = comp != null;
            return false;
        }
    }

    /// <summary>
    /// The set of stats DMSSR's mech customization actually modifies, resolved without duplicating
    /// the list: it is read from the private HashSet&lt;StatDef&gt; fields of DMSSR's own postfix class.
    /// </summary>
    internal static class MechStatSet
    {
        private static readonly object Sync = new();

        private static HashSet<StatDef>? stats;

        internal static bool Ready => System.Threading.Volatile.Read(ref stats) != null;

        internal static bool FromUpstream { get; private set; }

        internal static int Count => System.Threading.Volatile.Read(ref stats)?.Count ?? 0;

        internal static bool MayAffect(StatDef stat)
        {
            HashSet<StatDef>? set = System.Threading.Volatile.Read(ref stats);
            if (set == null)
            {
                Build();
                set = System.Threading.Volatile.Read(ref stats);
                if (set == null)
                {
                    // DefOfs are not bound yet (or the sets are unreadable): do not skip anything,
                    // upstream then behaves exactly as it does without this mod.
                    return true;
                }
            }

            return set.Contains(stat);
        }

        private static void Build()
        {
            // StatDefOf is bound right after all defs load. DMSSR's sets are initialized from
            // StatDefOf, so touching them before that would freeze them at null forever.
            if (StatDefOf.MoveSpeed == null
                || StatDefOf.MeleeDodgeChance == null
                || StatDefOf.AimingDelayFactor == null)
            {
                return;
            }

            lock (Sync)
            {
                if (System.Threading.Volatile.Read(ref stats) != null)
                {
                    return;
                }

                HashSet<StatDef>? upstream = BuildFromUpstream();
                FromUpstream = upstream != null;
                HashSet<StatDef> built = upstream ?? BuildFallback();
                if (built.Count == 0)
                {
                    return;
                }

                System.Threading.Volatile.Write(ref stats, built);
            }
        }

        private static HashSet<StatDef>? BuildFromUpstream()
        {
            FieldInfo[] fields = typeof(Patch_MechCustomization_ApplyStatPreFinalize).GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            HashSet<StatDef> union = new();
            int sets = 0;
            foreach (FieldInfo field in fields)
            {
                if (!typeof(HashSet<StatDef>).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                if (!(field.GetValue(null) is HashSet<StatDef> set))
                {
                    continue;
                }

                sets++;
                foreach (StatDef? stat in set)
                {
                    if (stat != null)
                    {
                        union.Add(stat);
                    }
                }
            }

            return sets > 0 && union.Count > 0 ? union : null;
        }

        private static HashSet<StatDef> BuildFallback()
        {
            HashSet<StatDef> set = new();
            Add(set, StatDefOf.MoveSpeed);
            Add(set, StatDefOf.ShootingAccuracyPawn);
            Add(set, StatDefOf.MeleeDodgeChance);
            Add(set, StatDefOf.AimingDelayFactor);
            Add(set, StatDefOf.MeleeCooldownFactor);
            Add(set, StatDefOf.MeleeDamageFactor);
            Add(set, StatDefOf.MechEnergyUsageFactor);
            Add(set, StatDefOf.MechEnergyLossPerHP);
            Add(set, StatDefOf.WastepacksPerRecharge);
            Add(set, StatDefOf.BandwidthCost);
            return set;
        }

        private static void Add(HashSet<StatDef> set, StatDef? stat)
        {
            if (stat != null)
            {
                set.Add(stat);
            }
        }
    }
}
