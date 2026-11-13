using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
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
    /// Both questions only change when the hediff set changes, and RimWorld funnels every hediff
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
    /// - Reads are lock free. A race can only cause a duplicate scan, never a wrong answer.
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
            Snapshot? snapshot = holder.Value;
            if (snapshot == null
                || snapshot.Generation != currentGeneration
                || snapshot.Count != set.hediffs.Count
                || !ReferenceEquals(snapshot.Set, set))
            {
                snapshot = Scan(set, currentGeneration);
                holder.Value = snapshot;
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

                if (ignoreTerrainPenalty && mechCustomizationResolved)
                {
                    break;
                }
            }

            return new Snapshot(set, hediffs.Count, currentGeneration, ignoreTerrainPenalty, mechCustomization);
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
            internal static readonly Snapshot Empty = new(null, 0, int.MinValue, false, null);

            internal readonly HediffSet? Set;
            internal readonly int Count;
            internal readonly int Generation;
            internal readonly bool IgnoreTerrainPenalty;
            internal readonly HediffComp_MechCustomization? MechCustomization;

            internal Snapshot(
                HediffSet? set,
                int count,
                int snapshotGeneration,
                bool ignoreTerrainPenalty,
                HediffComp_MechCustomization? mechCustomization)
            {
                Set = set;
                Count = count;
                Generation = snapshotGeneration;
                IgnoreTerrainPenalty = ignoreTerrainPenalty;
                MechCustomization = mechCustomization;
            }
        }
    }
}
