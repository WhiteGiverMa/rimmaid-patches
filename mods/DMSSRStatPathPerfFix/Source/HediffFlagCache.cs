using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DMSSR;
using DMSSR.Extensions;
using DMSSR.Hediffs;
using Verse;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    /// <summary>
    /// Per-pawn answers that DMSSR otherwise recomputes by walking every Hediff.
    ///
    /// Upstream, on every path step, asks "does this pawn have any hediff that ignores terrain
    /// move penalty?" by calling <c>Def.GetModExtension&lt;DefModExt_IgnoreTerrainMovePenalty&gt;</c>
    /// and <c>HediffUtility.TryGetComp&lt;HediffComp_IgnoreTerrainMovePenalty&gt;</c> for every
    /// hediff of every pawn, twice per step (see PathCostPatches). On every stat query it asks
    /// "is there a DMS_MechCustomization hediff?" by linearly scanning the same list.
    ///
    /// The snapshot also holds the first schizophrenia-link hediff and dynamic capacity providers.
    /// Membership only changes when the hediff set changes, and RimWorld funnels every hediff
    /// add/remove through <see cref="HediffSet.DirtyCache"/>. So we cache the answers and drop
    /// them on that signal.
    ///
    /// Correctness notes
    /// - A snapshot is only trusted while the generation, the HediffSet instance and the hediff
    ///   count all still match. Additions, removals and save loads all bump the generation.
    /// - The per-definition "can this hediff def carry the flag?" answer is cached separately and
    ///   keyed by HediffDef; it mirrors upstream exactly: <c>def.GetModExtension&lt;T&gt;()?.enabled</c>
    ///   plus a compClass assignable to <c>HediffComp_IgnoreTerrainMovePenalty</c> on a
    ///   HediffWithComps (a plain Hediff never builds comps, so it can never match upstream).
    /// - Fully constructed immutable snapshots are published with Volatile.Write/Read. Concurrent
    ///   readers may duplicate a rebuild; no mutable Dictionary or partially built array is shared.
    ///   As with upstream, hediff list mutation belongs to the main simulation phase, not pre-draw.
    /// </summary>
    internal static class HediffFlagCache
    {
        private const byte FlagExtension = 1;
        private const byte FlagComp = 2;
        private const byte FlagAny = FlagExtension | FlagComp;

        private static readonly ConcurrentDictionary<HediffDef, byte> DefFlags = new();
        private static readonly Func<HediffDef, byte> ComputeDefFlagsFunc = ComputeDefFlags;

        private static readonly ConditionalWeakTable<Pawn, SnapshotHolder> Table = new();

        private static int generation;

        /// <summary>Number of full hediff scans performed. Used by the validation harness.</summary>
        private static int scans;

        [ThreadStatic] private static int memoPawnId;
        [ThreadStatic] private static int memoGeneration;
        [ThreadStatic] private static Snapshot? memo;

        internal static int ScanCount => Volatile.Read(ref scans);

        internal static int Generation => Volatile.Read(ref generation);

        internal static bool HasIgnoreTerrainMovePenalty(Pawn? pawn)
        {
            return Get(pawn).IgnoreTerrainPenalty;
        }

        internal static HediffComp_MechCustomization? GetMechCustomization(Pawn? pawn)
        {
            return Get(pawn).MechCustomization;
        }

        internal static Hediff? GetSchizophreniaLinked(Pawn? pawn) => Get(pawn).SchizophreniaLinked;

        internal static IDynamicCapacityProvider[] GetCapacityProviders(Pawn? pawn) => Get(pawn).CapacityProviders;

        internal static void Invalidate()
        {
            Interlocked.Increment(ref generation);
        }

        /// <summary>Test hook: forget everything, including the per-thread memo.</summary>
        internal static void ResetForValidation()
        {
            Invalidate();
            memo = null;
            memoPawnId = 0;
            memoGeneration = int.MinValue;
            Volatile.Write(ref scans, 0);
        }

        private static Snapshot Get(Pawn? pawn)
        {
            if (pawn == null)
            {
                return Snapshot.Empty;
            }

            HediffSet? set = pawn.health?.hediffSet;
            if (set == null)
            {
                return Snapshot.Empty;
            }

            int currentGeneration = Generation;
            Snapshot? memoized = memo;
            if (memoized != null
                && memoGeneration == currentGeneration
                && memoPawnId == pawn.thingIDNumber
                && ReferenceEquals(memoized.Set, set)
                && memoized.Count == set.hediffs.Count)
            {
                return memoized;
            }

            SnapshotHolder holder = Table.GetValue(pawn, static _ => new SnapshotHolder());
            Snapshot? snapshot = Volatile.Read(ref holder.Value);
            if (snapshot == null
                || snapshot.Generation != currentGeneration
                || snapshot.Count != set.hediffs.Count
                || !ReferenceEquals(snapshot.Set, set))
            {
                snapshot = Scan(set, currentGeneration);
                Volatile.Write(ref holder.Value, snapshot);
            }

            memoPawnId = pawn.thingIDNumber;
            memoGeneration = currentGeneration;
            memo = snapshot;
            return snapshot;
        }

        private static Snapshot Scan(HediffSet set, int currentGeneration)
        {
            Interlocked.Increment(ref scans);

            List<Hediff> hediffs = set.hediffs;
            bool ignoreTerrainPenalty = false;
            bool mechCustomizationResolved = false;
            HediffComp_MechCustomization? mechCustomization = null;
            Hediff? schizophreniaLinked = null;
            List<IDynamicCapacityProvider>? providers = null;

            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff? hediff = hediffs[i];
                if (hediff == null)
                {
                    continue;
                }

                HediffDef? def = hediff.def;
                if (def == null)
                {
                    continue;
                }

                if (!ignoreTerrainPenalty && IgnoresTerrainPenalty(def))
                {
                    ignoreTerrainPenalty = true;
                }

                if (!mechCustomizationResolved && ReferenceEquals(def, DMS_HediffDefOf.DMS_MechCustomization))
                {
                    // Upstream uses GetFirstHediffOfDef + TryGetComp, i.e. the FIRST hediff of that
                    // def decides; if that one has no comp the answer is "no comp" even if a later
                    // hediff of the same def would have one. Mirror that exactly.
                    mechCustomizationResolved = true;
                    mechCustomization = hediff.TryGetComp<HediffComp_MechCustomization>();
                }

                if (schizophreniaLinked == null && ReferenceEquals(def, DMS_HediffDefOf.DMS_Schizophrenia_Linked))
                {
                    schizophreniaLinked = hediff;
                }

                // Preserve upstream's per-def provider eligibility, but pay its lock only when a
                // snapshot is rebuilt. Keep provider REFERENCES: their adjustment values are live.
                if (hediff is HediffWithComps withComps && withComps.comps != null
                    && CapacityPatches.DefHasProvider(withComps))
                {
                    foreach (HediffComp comp in withComps.comps)
                    {
                        if (comp is IDynamicCapacityProvider provider)
                            (providers ??= new List<IDynamicCapacityProvider>()).Add(provider);
                    }
                }
            }

            return new Snapshot(set, hediffs.Count, currentGeneration, ignoreTerrainPenalty,
                mechCustomization, schizophreniaLinked, providers?.ToArray() ?? Array.Empty<IDynamicCapacityProvider>());
        }

        private static bool IgnoresTerrainPenalty(HediffDef def)
        {
            return (DefFlags.GetOrAdd(def, ComputeDefFlagsFunc) & FlagAny) != 0;
        }

        private static byte ComputeDefFlags(HediffDef def)
        {
            byte flags = 0;

            if (def.GetModExtension<DefModExt_IgnoreTerrainMovePenalty>()?.enabled == true)
            {
                flags |= FlagExtension;
            }

            Type? hediffClass = def.hediffClass;
            if (hediffClass != null && typeof(HediffWithComps).IsAssignableFrom(hediffClass))
            {
                List<HediffCompProperties>? comps = def.comps;
                if (comps != null)
                {
                    for (int i = 0; i < comps.Count; i++)
                    {
                        Type? compClass = comps[i]?.compClass;
                        if (compClass != null && typeof(HediffComp_IgnoreTerrainMovePenalty).IsAssignableFrom(compClass))
                        {
                            flags |= FlagComp;
                            break;
                        }
                    }
                }
            }

            return flags;
        }

        private sealed class SnapshotHolder
        {
            internal Snapshot? Value;
        }

        private sealed class Snapshot
        {
            internal static readonly Snapshot Empty = new(null, 0, int.MinValue, false, null, null,
                Array.Empty<IDynamicCapacityProvider>());

            internal readonly HediffSet? Set;
            internal readonly int Count;
            internal readonly int Generation;
            internal readonly bool IgnoreTerrainPenalty;
            internal readonly HediffComp_MechCustomization? MechCustomization;
            internal readonly Hediff? SchizophreniaLinked;
            internal readonly IDynamicCapacityProvider[] CapacityProviders;

            internal Snapshot(
                HediffSet? set,
                int count,
                int snapshotGeneration,
                bool ignoreTerrainPenalty,
                HediffComp_MechCustomization? mechCustomization,
                Hediff? schizophreniaLinked,
                IDynamicCapacityProvider[] capacityProviders)
            {
                Set = set;
                Count = count;
                Generation = snapshotGeneration;
                IgnoreTerrainPenalty = ignoreTerrainPenalty;
                MechCustomization = mechCustomization;
                SchizophreniaLinked = schizophreniaLinked;
                CapacityProviders = capacityProviders;
            }
        }
    }
}
