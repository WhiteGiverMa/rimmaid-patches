using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DMSSR.Hediffs;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRNeuroglueFix
{
    /// <summary>
    /// Zero-allocation per-HediffSet snapshot driven by <see cref="HediffSet.DirtyCache"/>.
    /// Ordinary mechanoids without biogel reservoirs or bionic syndromes short-circuit in O(1)
    /// without scanning their hediff list on every ThinkTree evaluation.
    /// </summary>
    internal static class MechBiogelCache
    {
        internal const float AutoRefillThreshold = 75f;
        internal const int SearchCooldownTicks = 600;

        internal sealed class Entry
        {
            internal bool Dirty = true;
            internal int LastHediffCount = -1;
            internal Hediff_BiogelReservoir? Reservoir;
            internal bool HasNeuroglueHigh;
            internal bool HasRemovableSyndrome;
            internal bool HasBionicShock;
            internal int NextSearchTick = int.MinValue;
        }

        private static ConditionalWeakTable<HediffSet, Entry> table = new();

        internal static bool DirtyHookInstalled { get; private set; }

        internal static int RebuildCount { get; private set; }

        internal static void ApplyHarmony(Harmony harmony)
        {
            var dirtyCache = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache), System.Type.EmptyTypes);
            if (dirtyCache == null)
            {
                SafeLog.Warning("HediffSet.DirtyCache() was not found; falling back to count checks.");
                return;
            }

            var postfix = new HarmonyMethod(typeof(MechBiogelCache), nameof(DirtyCachePostfix));
            harmony.Patch(dirtyCache, postfix: postfix);
            DirtyHookInstalled = true;
        }

        private static void DirtyCachePostfix(HediffSet __instance)
        {
            if (__instance != null && table.TryGetValue(__instance, out Entry? entry))
            {
                entry.Dirty = true;
                entry.NextSearchTick = int.MinValue;
            }
        }

        internal static void ResetForTesting()
        {
            table = new ConditionalWeakTable<HediffSet, Entry>();
            RebuildCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Entry? GetSnapshot(Pawn? pawn)
        {
            HediffSet? hediffSet = pawn?.health?.hediffSet;
            if (hediffSet == null)
            {
                return null;
            }

            if (!table.TryGetValue(hediffSet, out Entry? entry))
            {
                entry = CreateAndPopulate(hediffSet);
            }
            else if (entry.Dirty || entry.LastHediffCount != hediffSet.hediffs.Count)
            {
                Rebuild(hediffSet, entry);
            }

            return entry;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Entry CreateAndPopulate(HediffSet hediffSet)
        {
            Entry entry = table.GetOrCreateValue(hediffSet);
            Rebuild(hediffSet, entry);
            return entry;
        }

        private static void Rebuild(HediffSet hediffSet, Entry entry)
        {
            NeuroglueDefs.EnsureInitialized();

            HediffDef? highDef = NeuroglueDefs.NeuroglueHigh;
            HediffDef? degenDef = NeuroglueDefs.DegenerativeSyndrome;
            HediffDef? rejectionDef = NeuroglueDefs.SyntheticRejection;
            HediffDef? phantomDef = NeuroglueDefs.BionicShockRemove;
            HediffDef? shockDef = NeuroglueDefs.BionicShock;

            Hediff_BiogelReservoir? reservoir = null;
            bool hasHigh = false;
            bool hasRemovable = false;
            bool hasShock = false;

            List<Hediff> hediffs = hediffSet.hediffs;
            int count = hediffs.Count;
            for (int i = 0; i < count; i++)
            {
                Hediff? hediff = hediffs[i];
                if (hediff == null)
                {
                    continue;
                }

                if (reservoir == null && hediff is Hediff_BiogelReservoir foundReservoir)
                {
                    reservoir = foundReservoir;
                    continue;
                }

                HediffDef def = hediff.def;
                if (highDef != null && ReferenceEquals(def, highDef))
                {
                    hasHigh = true;
                }
                else if (shockDef != null && ReferenceEquals(def, shockDef))
                {
                    hasShock = true;
                }
                else if ((degenDef != null && ReferenceEquals(def, degenDef))
                         || (rejectionDef != null && ReferenceEquals(def, rejectionDef))
                         || (phantomDef != null && ReferenceEquals(def, phantomDef)))
                {
                    hasRemovable = true;
                }
            }

            entry.Reservoir = reservoir;
            entry.HasNeuroglueHigh = hasHigh;
            entry.HasRemovableSyndrome = hasRemovable;
            entry.HasBionicShock = hasShock;
            entry.LastHediffCount = count;
            entry.Dirty = false;
            RebuildCount++;
        }

        /// <summary>
        /// True when a mechanoid has an active bionic syndrome that a dose of neuroglue will
        /// cure or step down, even if its biogel reservoir is currently at 100 or absent.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool NeedsTherapeuticDose(Entry entry)
        {
            return entry.HasBionicShock || (entry.HasRemovableSyndrome && !entry.HasNeuroglueHigh);
        }

        internal static bool NeedsTherapeuticDose(Pawn? pawn)
        {
            Entry? entry = GetSnapshot(pawn);
            return entry != null && NeedsTherapeuticDose(entry);
        }

        /// <summary>
        /// True when a colony mechanoid should autonomously seek 1 unit of neuroglue now.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool ShouldAutoUseNeuroglue(Entry entry)
        {
            if (NeedsTherapeuticDose(entry))
            {
                return true;
            }

            Hediff_BiogelReservoir? reservoir = entry.Reservoir;
            return reservoir != null && reservoir.Fuel <= AutoRefillThreshold;
        }
    }
}
