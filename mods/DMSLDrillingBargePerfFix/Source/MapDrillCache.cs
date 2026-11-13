using System;
using System.Collections.Generic;
using DMS_Legion;
using Verse;

namespace WhiteGiver.DMSL.DrillingBargePerfFix;

/// <summary>
/// Per-map, incrementally maintained index of every cell for which
/// <c>WorkGiver_DrillingBargeDeepDrill.TryGetDeepResource(cell, map, ..., applyExclusions)</c> returns true.
///
/// Why this is exact
/// -----------------
/// <c>TryGetDeepResource</c> only inspects <c>GenRadial.RadialPattern[0..20]</c> around its center, and only a
/// cell with <c>CountAt &gt; 0</c> and a non-null <c>ThingDefAt</c> can make it return true. So the set of cells
/// that can possibly return true is exactly <c>{ vein - pattern[k] }</c> over all vein cells, which is what
/// <see cref="offsets"/> enumerates (both signs, so the result does not depend on the radial pattern being
/// symmetric). Every candidate is then evaluated by calling the upstream <c>TryGetDeepResource</c> itself, so
/// the stored answer is the upstream answer, not a re-implementation of it. Cells outside the candidate set
/// have no vein in range and can only return false, which is what the zeroed flags mean.
///
/// The set is indexed by <c>CellIndices.CellToIndex</c>, which is also the iteration order of upstream
/// <c>GetPotentialDrillCells</c>, so the emitted <see cref="List{T}"/> keeps the exact upstream ordering.
///
/// Single-threaded by design: RimWorld runs map work on the main thread.
/// </summary>
internal sealed class MapDrillCache
{
    private const int PatternLength = 21;

    /// <summary>
    /// Deduplicated union of <c>RadialPattern[0..20]</c> and its negation. A vein at <c>v</c> makes
    /// <c>v + offset</c> a possible work cell for every offset here.
    /// </summary>
    private static readonly IntVec3[] offsets = BuildOffsets();

    private readonly Map map;
    private readonly CellIndices indices;
    private readonly int numGridCells;

    /// <summary>Per-cell answer bits: 1 = valuable with exclusions, 2 = valuable without exclusions.</summary>
    private readonly byte[] flags;

    /// <summary>Per-cell "this cell itself holds a deep resource" flag.</summary>
    private readonly bool[] veinCell;

    /// <summary>Short hash of the deep resource def while <see cref="veinCell"/> is set, otherwise 0.</summary>
    private readonly ushort[] veinDefHash;

    /// <summary>
    /// Number of vein cells that <c>TryGetDeepResource</c> would encounter for this cell.
    /// Bounded by <c>offsets.Length</c> (42), so a byte is enough.
    /// </summary>
    private readonly byte[] veinNeighborCount;

    private readonly SortedSet<int> candidateIndices = new();

    /// <summary>Cells whose exclusions-aware answer is true, i.e. exactly the work cells of the barge.</summary>
    private readonly SortedSet<int> valuableIndices = new();

    private List<IntVec3> front = new();

    private bool built;
    private bool building;
    private bool listDirty;

    internal MapDrillCache(Map map)
    {
        this.map = map;
        indices = map.cellIndices;
        numGridCells = indices.NumGridCells;

        flags = new byte[numGridCells];
        veinCell = new bool[numGridCells];
        veinDefHash = new ushort[numGridCells];
        veinNeighborCount = new byte[numGridCells];
    }

    /// <summary>
    /// O(1) equivalent of <c>WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(center, map, ...)</c>.
    /// </summary>
    internal bool HasValuable(IntVec3 center, bool applyExclusions)
    {
        if (map.deepResourceGrid == null)
        {
            return false;
        }

        EnsureBuilt();

        if (!GenGrid.InBounds(center, map))
        {
            return false;
        }

        int index = indices.CellToIndex(center);
        if ((uint)index >= (uint)numGridCells)
        {
            return false;
        }

        byte mask = applyExclusions ? (byte)1 : (byte)2;
        return (flags[index] & mask) != 0;
    }

    /// <summary>
    /// Exactly the cells for which upstream <c>TryGetDeepResource(_, map, ..., applyExclusions: true)</c> is
    /// true, in upstream index order. This is the set that <c>CellHasValuableDeepResource(_, map, bargePawn)</c>
    /// accepts, so returning it from <c>PotentialWorkCellsGlobal</c> cannot change which jobs are issued.
    /// Rebuilds allocate a fresh list and swap the reference, so a list handed to a caller is never mutated
    /// afterwards. Rebuilds only happen when the grid actually changed, never per query.
    /// </summary>
    internal List<IntVec3> GetValuableCells()
    {
        EnsureBuilt();
        if (listDirty)
        {
            RebuildList();
        }

        return front;
    }

    /// <summary>
    /// Called after <c>DeepResourceGrid.SetAt</c>. Only the edited cell and its 21 radial neighbours can
    /// change their answer, so this is O(21 * 21) grid reads and O(log n) set updates, independent of map size.
    /// </summary>
    internal void NotifyCellChanged(IntVec3 cell)
    {
        if (!built || map.deepResourceGrid == null || !GenGrid.InBounds(cell, map))
        {
            return;
        }

        int index = indices.CellToIndex(cell);
        if ((uint)index >= (uint)numGridCells)
        {
            return;
        }

        bool nowVein = IsVeinCell(cell, out ushort nowHash);
        bool wasVein = veinCell[index];
        ushort wasHash = veinDefHash[index];

        // A count change above zero cannot alter any answer: the check is "count > 0" only.
        if (nowVein == wasVein && nowHash == wasHash)
        {
            return;
        }

        veinCell[index] = nowVein;
        veinDefHash[index] = nowHash;

        int delta = (nowVein ? 1 : 0) - (wasVein ? 1 : 0);

        for (int k = 0; k < offsets.Length; k++)
        {
            IntVec3 neighbor = cell + offsets[k];
            if (!GenGrid.InBounds(neighbor, map))
            {
                continue;
            }

            int neighborIndex = indices.CellToIndex(neighbor);

            if (delta != 0)
            {
                int before = veinNeighborCount[neighborIndex];
                int after = before + delta;
                veinNeighborCount[neighborIndex] = (byte)after;

                if (before == 0 && after > 0)
                {
                    candidateIndices.Add(neighborIndex);
                }
                else if (before > 0 && after == 0)
                {
                    candidateIndices.Remove(neighborIndex);
                }
            }

            ComputeFlags(neighborIndex);
        }

        listDirty = true;
    }

    private static IntVec3[] BuildOffsets()
    {
        List<IntVec3> result = new(PatternLength * 2);
        for (int k = 0; k < PatternLength; k++)
        {
            IntVec3 forward = GenRadial.RadialPattern[k];
            if (!result.Contains(forward))
            {
                result.Add(forward);
            }

            IntVec3 backward = new(-forward.x, 0, -forward.z);
            if (!result.Contains(backward))
            {
                result.Add(backward);
            }
        }

        return result.ToArray();
    }

    private void EnsureBuilt()
    {
        if (built || building)
        {
            return;
        }

        building = true;
        try
        {
            if (map.deepResourceGrid == null)
            {
                built = true;
                return;
            }

            for (int i = 0; i < numGridCells; i++)
            {
                if (IsVeinCell(indices.IndexToCell(i), out ushort hash))
                {
                    veinCell[i] = true;
                    veinDefHash[i] = hash;
                }
            }

            for (int i = 0; i < numGridCells; i++)
            {
                if (!veinCell[i])
                {
                    continue;
                }

                IntVec3 vein = indices.IndexToCell(i);
                for (int k = 0; k < offsets.Length; k++)
                {
                    IntVec3 candidate = vein + offsets[k];
                    if (!GenGrid.InBounds(candidate, map))
                    {
                        continue;
                    }

                    int candidateIndex = indices.CellToIndex(candidate);
                    if (veinNeighborCount[candidateIndex]++ == 0)
                    {
                        candidateIndices.Add(candidateIndex);
                    }
                }
            }

            foreach (int candidateIndex in candidateIndices)
            {
                ComputeFlags(candidateIndex);
            }

            RebuildList();
            built = true;
        }
        finally
        {
            building = false;
        }
    }

    private void ComputeFlags(int cellIndex)
    {
        IntVec3 cell = indices.IndexToCell(cellIndex);
        byte value = 0;

        if (WorkGiver_DrillingBargeDeepDrill.TryGetDeepResource(cell, map, out _, out _, out _, true))
        {
            value |= 1;
        }

        if (WorkGiver_DrillingBargeDeepDrill.TryGetDeepResource(cell, map, out _, out _, out _, false))
        {
            value |= 2;
        }

        flags[cellIndex] = value;

        if ((value & 1) != 0)
        {
            valuableIndices.Add(cellIndex);
        }
        else
        {
            valuableIndices.Remove(cellIndex);
        }
    }

    private void RebuildList()
    {
        List<IntVec3> rebuilt = new(valuableIndices.Count);

        foreach (int valuableIndex in valuableIndices)
        {
            rebuilt.Add(indices.IndexToCell(valuableIndex));
        }

        front = rebuilt;
        listDirty = false;
    }

    private bool IsVeinCell(IntVec3 cell, out ushort defHash)
    {
        defHash = 0;

        DeepResourceGrid? grid = map.deepResourceGrid;
        if (grid == null || grid.CountAt(cell) <= 0)
        {
            return false;
        }

        ThingDef? resourceDef = grid.ThingDefAt(cell);
        if (resourceDef == null)
        {
            return false;
        }

        defHash = resourceDef.shortHash;
        return true;
    }
}
