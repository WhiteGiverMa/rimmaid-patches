using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DMS_Legion;
using Verse;

namespace WhiteGiver.DMSL.CommanderPerfFix;

/// <summary>
/// Immutable ThingDef marker sets plus a weak per-pawn comp cache. Published snapshots are never mutated,
/// so Waylith reader threads can share them without locks; only a DefCount change enters the rebuild lock.
/// </summary>
internal static class CommanderMarkerCache
{
    private sealed class MarkerSnapshot
    {
        internal readonly int DefCount;
        internal readonly HashSet<ThingDef> MarkerDefs;

        internal MarkerSnapshot(int defCount, HashSet<ThingDef> markerDefs)
        {
            DefCount = defCount;
            MarkerDefs = markerDefs;
        }
    }

    private static readonly object RebuildLock = new();
    private static readonly ConditionalWeakTable<Pawn, CompMechCommanderMarker> Markers = new();
    private static MarkerSnapshot? snapshot;

    internal static CompMechCommanderMarker? GetMarker(Pawn? pawn)
    {
        if (pawn == null || pawn.Destroyed)
        {
            return null;
        }

        MarkerSnapshot current = GetSnapshot();
        if (!current.MarkerDefs.Contains(pawn.def))
        {
            return null;
        }

        if (Markers.TryGetValue(pawn, out CompMechCommanderMarker? marker))
        {
            return marker;
        }

        // The first lookup still uses the real 1.7 GetComp implementation. A missing marker cannot be
        // cached as a CWT null value, but ordinary initialized commander pawns always have this comp.
        marker = pawn.GetComp<CompMechCommanderMarker>();
        if (marker == null)
        {
            return null;
        }

        // The non-capturing callback has one process-lifetime delegate. CWT arbitrates racing cold readers;
        // every callback returns the same already-initialized comp from this pawn.
        return Markers.GetValue(pawn, static p => p.GetComp<CompMechCommanderMarker>()!);
    }

    internal static bool PawnHasMarker(Pawn? pawn)
    {
        return GetMarker(pawn) != null;
    }

    private static MarkerSnapshot GetSnapshot()
    {
        int defCount = DefDatabase<ThingDef>.DefCount;
        MarkerSnapshot? current = Volatile.Read(ref snapshot);
        if (current != null && current.DefCount == defCount)
        {
            return current;
        }

        lock (RebuildLock)
        {
            defCount = DefDatabase<ThingDef>.DefCount;
            current = snapshot;
            if (current != null && current.DefCount == defCount)
            {
                return current;
            }

            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            HashSet<ThingDef> markerDefs = new();
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef def = allDefs[i];
                List<CompProperties>? comps = def.comps;
                if (comps == null)
                {
                    continue;
                }

                for (int j = 0; j < comps.Count; j++)
                {
                    if (comps[j] is CompProperties_MechCommanderMarker)
                    {
                        markerDefs.Add(def);
                        break;
                    }
                }
            }

            MarkerSnapshot rebuilt = new(defCount, markerDefs);
            Volatile.Write(ref snapshot, rebuilt);
            return rebuilt;
        }
    }
}
