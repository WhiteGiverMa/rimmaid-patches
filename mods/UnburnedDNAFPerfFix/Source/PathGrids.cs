using System.Runtime.CompilerServices;
using RimWorld;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace UnburnedDNAFPerfFix;

public static class PathGrids
{
    private sealed class Costs { internal readonly Dictionary<int, int> Cells = []; }
    private sealed class Output
    {
        internal PathGridJob Job;
        internal Dictionary<int, int> FireCosts = [];
        internal HashSet<int> FireOnly = [];
        internal NativeArray<int> Adjusted;
    }
    private sealed class State
    {
        internal readonly object Gate = new();
        internal readonly Dictionary<NativeArray<int>.ReadOnly, Dictionary<int, int>> Sources = [];
        internal readonly Dictionary<NativeArray<int>, Output> Outputs = [];
        internal readonly HashSet<int> FireOnly = [];
    }
    private static readonly ConditionalWeakTable<PathGrid, Costs> CostMaps = new();
    private static readonly ConditionalWeakTable<Map, State> Maps = new();
    public static int LastAdjustedCells { get; private set; }
    public static int LiveAdjustedBuffers { get; private set; }

    public static int Record(int result, PathGrid grid, IntVec3 c, int baseline, bool perceivedStatic)
    {
        if (!perceivedStatic) return result;
        int delta = baseline >= 0 ? result - baseline : 0;
        if (!CostMaps.TryGetValue(grid, out Costs? costs))
        {
            if (delta <= 0) return result;
            costs = CostMaps.GetValue(grid, static _ => new());
        }
        int index = grid.map.cellIndices.CellToIndex(c);
        lock (costs)
        {
            if (delta > 0) costs.Cells[index] = delta;
            else costs.Cells.Remove(index);
        }
        return result;
    }

    public static void CopyCosts(PathingContext context, NativeArray<int> data, List<IntVec3>? deltas)
    {
        State state = Maps.GetValue(context.map, static _ => new());
        Costs costs = CostMaps.GetValue(context.pathGrid, static _ => new());
        lock (state.Gate)
        lock (costs)
        {
            if (deltas == null || !state.Sources.TryGetValue(data.AsReadOnly(), out var saved))
                state.Sources[data.AsReadOnly()] = new Dictionary<int, int>(costs.Cells);
            else
                foreach (IntVec3 cell in deltas)
                {
                    int index = context.map.cellIndices.CellToIndex(cell);
                    if (costs.Cells.TryGetValue(index, out int value)) saved[index] = value;
                    else saved.Remove(index);
                }
        }
    }

    public static void Capture(Map map, PathGridJob job)
    {
        State state = Maps.GetValue(map, static _ => new());
        lock (state.Gate)
        {
            Output output = new() { Job = job };
            if (state.Sources.TryGetValue(job.pathGridDirect, out var costs)) output.FireCosts = new(costs);
            output.FireOnly = new(state.FireOnly);
            // RecycleGridJobData owns the lifetime boundary; overwriting an output
            // without recycling would otherwise free a buffer still read by workers.
            if (!state.Outputs.TryAdd(job.grid, output))
                throw new InvalidOperationException("Path grid reused before its generation was recycled");
        }
    }

    public static void CopyDanger(Map map, List<IntVec3>? deltas)
    {
        // Called after the real source computation, including its parallel GatherData
        // workers. Capture provenance at the same generation as the danger bits.
        State state = Maps.GetValue(map, static _ => new());
        HashSet<int> ground = [];
        foreach (Thing t in map.listerThings.ThingsOfDef(ThingDefOf.Fire))
            if (t is Fire { parent: null }) ground.Add(map.cellIndices[t.Position]);
        lock (state.Gate)
        {
            if (deltas == null)
            {
                state.FireOnly.Clear();
                foreach (int index in ground) if (OnlyFire(map, index)) state.FireOnly.Add(index);
            }
            else foreach (IntVec3 c in deltas)
            {
                int index = map.cellIndices[c];
                if (ground.Contains(index) && OnlyFire(map, index)) state.FireOnly.Add(index);
                else state.FireOnly.Remove(index);
            }
        }
    }
    private static bool OnlyFire(Map map, int index)
    {
        if (map.terrainGrid.TerrainAt(index).dangerous) return false;
        foreach (Thing thing in map.thingGrid.ThingsListAtFast(index))
            if (thing.def.pathfinderDangerous && thing is not Pawn && thing is not Fire) return false;
        return true;
    }

    public static NativeArray<int>.ReadOnly Adjust(Map map, NativeArray<int> original) => AdjustBuffer(map, original).AsReadOnly();
    public static NativeArray<int> AdjustBuffer(Map map, NativeArray<int> original)
    {
        State state = Maps.GetValue(map, static _ => new());
        lock (state.Gate)
        {
            if (!state.Outputs.TryGetValue(original, out Output? output)) return original;
            if (output.Adjusted.IsCreated) return output.Adjusted;
            NativeArray<int> adjusted = new(original.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            NativeArray<int>.Copy(original, adjusted);
            var indices = new HashSet<int>(output.FireCosts.Keys);
            indices.UnionWith(output.FireOnly);
            LastAdjustedCells = indices.Count;
            PathGridJob job = output.Job;
            foreach (int i in indices)
            {
                // 10000 is the explicit impassability sentinel. Never turn a wall,
                // fence, water restriction or custom blocked cell into a walkable cell.
                if (original[i] == PathGrid.ImpassableCost) continue;
                int remove = 0;
                if (job.pathGridDirect[i] < PathGrid.ImpassableCost && !(job.tuning.costWater >= 0 && job.water.IsSet(i)))
                    output.FireCosts.TryGetValue(i, out remove);
                if (job.traverseParams.avoidPersistentDanger && job.persistentDanger.IsSet(i) && output.FireOnly.Contains(i)
                    && !(job.traverseParams.avoidDarknessDanger && job.darknessDanger.IsSet(i))) remove += job.tuning.costDanger;
                adjusted[i] = Math.Max(0, original[i] - remove);
            }
            output.Adjusted = adjusted;
            LiveAdjustedBuffers++;
            return adjusted;
        }
    }

    public static void Recycle(Map map)
    {
        if (!Maps.TryGetValue(map, out State? state)) return;
        lock (state.Gate)
        {
            foreach (Output output in state.Outputs.Values)
                if (output.Adjusted.IsCreated) { output.Adjusted.Dispose(); LiveAdjustedBuffers--; }
            state.Outputs.Clear();
        }
    }
    public static void Release(Map map) { Recycle(map); Maps.Remove(map); }
}
