using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnburnedDNAFPerfFix;
using Unity.Collections;
using Unity.Jobs;
using Verse;
using Verse.AI;

namespace Validation;

internal static class GridTests
{
    internal static void Run(bool green)
    {
        var f = new Fixture();
        Pawn p = f.Pawn(new IntVec3(2, 0, 2));
        IntVec3 cell = new(60, 0, 60);
        Fire fire = f.Fire(cell); f.Tick();
        int index = f.Map.cellIndices[cell];
        if (!green) f.Component!.GetType().GetMethod("UpdateFire")!.Invoke(f.Component, [1, cell]);
        if (!green)
        {
            NativeArray<int> allocated = (NativeArray<int>)AccessTools.Field(f.Component!.GetType(), "cachedGrid").GetValue(f.Component)!;
            NativeArray<int> prev = (NativeArray<int>)AccessTools.Field(f.Component.GetType(), "prevGrid").GetValue(f.Component)!;
            Tests.Check(allocated.Length == 105625 && prev.Length == 1, "RED: upstream constructor owns 422500+4 native bytes");
        }
        int count = f.Map.cellIndices.NumGridCells;
        NativeArray<int> actualGrid = f.Grid.Grid_Unsafe;
        for (int i = 0; i < count; i++) actualGrid[i] = 13;
        foreach (IntVec3 offset in GenAdj.AdjacentCellsAndInside)
        {
            IntVec3 c = cell + offset;
            actualGrid[f.Map.cellIndices[c]] = f.Grid.CalculatedCostAt(c, true, IntVec3.Invalid);
        }
        Tests.Check(actualGrid[index] == 1013, "real CalculatedCostAt adds 1000 at ground fire");
        using CostSource source = new(f.Map, new PathingContext(f.Map, f.Grid));
        source.ComputeAll([]);
        using PersistentDangerSource dangers = new(f.Map);
        dangers.ComputeAll([]);
        if (!green)
        {
            bool[] only = (bool[])AccessTools.Field(f.Component!.GetType(), "DangerGridOnlyFire").GetValue(f.Component)!;
            Tests.Check(only[index], "upstream danger transpiler executes with correct local 0");
            dangers.UpdateIncrementally([], [cell]);
            Tests.Check(only[index], "upstream incremental danger transpiler executes with correct local 2");
            f.Ground.dangerous = true;
            dangers.UpdateIncrementally([], [cell]);
            Tests.Check(only[index], "RED: dangerous terrain incorrectly still classified fire-only");
            f.Ground.dangerous = false;
        }
        using NativeArray<int> output = new(count, Allocator.Persistent);
        using NativeArray<ushort> shorts = new(count, Allocator.Persistent);
        using NativeArray<ushort> emptyShorts = new(0, Allocator.Persistent);
        using NativeArray<byte> emptyBytes = new(0, Allocator.Persistent);
        using NativeBitArray bits = new(count, Allocator.Persistent);
        using NativeBitArray emptyBits = new(0, Allocator.Persistent);
        PathGridJob job = new()
        {
            indicies = f.Map.cellIndices, pathGridDirect = source.Data, grid = output,
            tuning = PathFinderCostTuning.DefaultTuning,
            traverseParams = new(TraverseMode.ByPawn, false, false, true, false, false, CellRect.Empty),
            persistentDanger = dangers.Data, darknessDanger = bits.AsReadOnly(),
            building = bits.AsReadOnly(), buildingDestroyable = bits.AsReadOnly(), player = bits.AsReadOnly(),
            water = bits.AsReadOnly(), fence = bits.AsReadOnly(), fogged = bits.AsReadOnly(),
            allowedGrid = emptyBits.AsReadOnly(), lordGrid = emptyBits.AsReadOnly(),
            perceptualCost = shorts.AsReadOnly(), factionCosts = shorts.AsReadOnly(), buildingHitPoints = shorts.AsReadOnly(),
            custom = emptyShorts.AsReadOnly(), avoidGrid = emptyBytes.AsReadOnly()
        };
        for (int i = 0; i < count; i++) job.Execute(i);
        Tests.Check(output[index] == 1313, "real PathGridJob adds danger once");
        var req = f.Request(p);
        var handle = default(JobHandle);
        NativeArray<int>.ReadOnly adjusted;
        if (green)
        {
            PathGrids.Capture(f.Map, job);
            adjusted = DnafBridge.Process(output, req, ref handle);
            Tests.Flammability = float.NaN;
            Tests.Check(DnafBridge.Process(output, req, ref handle)[index] == 1313, "NaN flammability retains upstream strict less-than semantics");
            Tests.Flammability = 0f;
        }
        else adjusted = UpstreamProcess(output, req);
        Tests.Check(adjusted[index] == 13 && adjusted[index - 1] == 13, "fire and all eight adjacent costs removed");
        if (green) Tests.Check(PathGrids.LastAdjustedCells == 9, "sparse patch visits 9 of 105625 cells");
        Parameterize(f, req, output, index, green);

        // Replay the actual recycling operation with the SAME native output buffer.
        // Vanilla queues output and writes new PathGridJob costs into it next time.
        if (green) PathGrids.Recycle(f.Map);
        actualGrid[0] = 10000;
        source.UpdateIncrementally([], [new IntVec3(0, 0, 0)]);
        for (int i = 0; i < count; i++) job.Execute(i);
        if (green) PathGrids.Capture(f.Map, job);
        adjusted = green ? DnafBridge.Process(output, req, ref handle) : UpstreamProcess(output, req);
        Tests.Check(adjusted[0] == (green ? 10000 : 13), $"same-buffer wall cost = {adjusted[0]}");
        if (!green)
        {
            Tests.Check(((NativeArray<int>)AccessTools.Field(f.Component!.GetType(), "prevGrid").GetValue(f.Component)!).Equals(output), "RED: prevGrid allocation overwritten by borrowed vanilla buffer");
            f.Map.pathing = null; f.Map.snowGrid = null; f.Map.fogGrid = null;
            f.Map.Dispose();
            Tests.Check(((NativeArray<int>)AccessTools.Field(f.Component.GetType(), "cachedGrid").GetValue(f.Component)!).IsCreated,
                "RED: cached native buffer still allocated after real Map.Dispose");
        }
        if (green)
        {
            Tests.Check(output[index] == 1313, "vanilla output remains untouched");
            // Keep two generations live simultaneously (different path tunings):
            // the old read-only result must remain stable under later requests.
            using NativeArray<int> second = new(count, Allocator.Persistent);
            PathGridJob secondJob = job;
            secondJob.grid = second; secondJob.tuning.costDanger = 700;
            for (int i = 0; i < count; i++) secondJob.Execute(i);
            PathGrids.Capture(f.Map, secondJob);
            NativeArray<int>.ReadOnly secondAdjusted = DnafBridge.Process(second, req, ref handle);
            Tests.Check(secondAdjusted[index] == 13 && adjusted[index] == 13 && second[index] == 1713, "independent tuned outputs; first reader stays unchanged");
            PathGrids.Recycle(f.Map);
            // Both darkness and dangerous terrain must survive fire removal.
            f.Ground.dangerous = true;
            dangers.UpdateIncrementally([], [cell]);
            for (int i = 0; i < count; i++) job.Execute(i);
            PathGrids.Capture(f.Map, job);
            adjusted = DnafBridge.Process(output, req, ref handle);
            Tests.Check(adjusted[index] == 313, "dangerous terrain cost preserved");
            PathGrids.Recycle(f.Map);
            f.Ground.dangerous = false;
            dangers.UpdateIncrementally([], [cell]);
            bits.Set(index, true);
            job.traverseParams = new(TraverseMode.ByPawn, false, false, true, true, false, CellRect.Empty);
            for (int i = 0; i < count; i++) job.Execute(i);
            PathGrids.Capture(f.Map, job);
            adjusted = DnafBridge.Process(output, req, ref handle);
            Tests.Check(adjusted[index] == 313, "darkness cost preserved when fire danger removed");
            PathGrids.Recycle(f.Map);
            bits.Set(index, false);
            job.traverseParams = new(TraverseMode.ByPawn, false, false, false, false, false, CellRect.Empty);
            for (int i = 0; i < count; i++) job.Execute(i);
            PathGrids.Capture(f.Map, job);
            adjusted = DnafBridge.Process(output, req, ref handle);
            Tests.Check(adjusted[index] == 13, "no danger subtraction when avoidance disabled");
            PathGrids.Recycle(f.Map);
            Thing hazard = new() { def = new ThingDef { defName = "Hazard", pathfinderDangerous = true, thingClass = typeof(Thing) } };
            Fixture.Set(hazard, "positionInt", cell); Fixture.Set(hazard, "mapIndexOrState", (sbyte)0);
            f.Map.thingGrid.Register(hazard); dangers.UpdateIncrementally([], [cell]);
            job.traverseParams = new(TraverseMode.ByPawn, false, false, true, false, false, CellRect.Empty);
            for (int i = 0; i < count; i++) job.Execute(i);
            PathGrids.Capture(f.Map, job); adjusted = DnafBridge.Process(output, req, ref handle);
            Tests.Check(adjusted[index] == 313, "mixed non-fire dangerous thing retains danger cost");
            PathGrids.Recycle(f.Map);
            f.Map.thingGrid.Deregister(hazard);
            Thing wall = new() { def = new ThingDef { defName = "Wall", passability = Traversability.Impassable, thingClass = typeof(Thing) } };
            Fixture.Set(wall, "positionInt", cell); Fixture.Set(wall, "mapIndexOrState", (sbyte)0); f.Map.thingGrid.Register(wall);
            actualGrid[index] = f.Grid.CalculatedCostAt(cell, true, IntVec3.Invalid);
            source.UpdateIncrementally([], [cell]); dangers.UpdateIncrementally([], [cell]);
            for (int i = 0; i < count; i++) job.Execute(i);
            PathGrids.Capture(f.Map, job); adjusted = DnafBridge.Process(output, req, ref handle);
            Tests.Check(adjusted[index] == 10000, "wall on burning cell remains impassable (early-return stale fire delta cleared)");
            PathGrids.Recycle(f.Map);
            f.Map.thingGrid.Deregister(wall);
            actualGrid[index] = f.Grid.CalculatedCostAt(cell, true, IntVec3.Invalid);
            source.UpdateIncrementally([], [cell]);
            job.tuning.costWater = 70; bits.Set(index, true);
            for (int i = 0; i < count; i++) job.Execute(i);
            PathGrids.Capture(f.Map, job); adjusted = DnafBridge.Process(output, req, ref handle);
            Tests.Check(adjusted[index] == 70, "water-cost override is not reduced by nonexistent direct fire penalty");
            NativeArray<int> componentCached = (NativeArray<int>)AccessTools.Field(f.Component!.GetType(), "cachedGrid").GetValue(f.Component)!;
            NativeArray<int> componentPrevious = (NativeArray<int>)AccessTools.Field(f.Component.GetType(), "prevGrid").GetValue(f.Component)!;
            Tests.Check(!componentCached.IsCreated && !componentPrevious.IsCreated, "both original persistent allocations released at construction");
            AssertDispose(f);
        }
    }

    private static void Parameterize(Fixture f, PathRequest request, NativeArray<int> grid, int index, bool green)
    {
        MethodInfo m = AccessTools.Method(typeof(PathFinder), "ParameterizePathJob");
        var args = m.GetParameters().Select(p => p.ParameterType.IsByRef ? Activator.CreateInstance(p.ParameterType.GetElementType()!) : null).ToArray();
        object scheduled = args[5]!;
        Fixture.Set(scheduled, "output", new PathFinder.GridJobOutput { grid = grid });
        using NativeList<int> excluded = new(4, Allocator.Persistent);
        Fixture.Set(args[3]!, "excludedDestIndices", excluded);
        args[1] = PathFinder.MapGridRequest.For(request);
        args[6] = request;
        PathFinder finder = Fixture.Raw<PathFinder>();
        Fixture.Set(finder, "map", f.Map);
        Audit.Hooks.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.TicksPerMoveCardinal)), prefix: new HarmonyMethod(typeof(GridTests), nameof(MoveTicks)));
        Audit.Hooks.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.TicksPerMoveDiagonal)), prefix: new HarmonyMethod(typeof(GridTests), nameof(MoveTicks)));
        m.Invoke(finder, args);
        PathFinderJob result = (PathFinderJob)args[0]!;
        Tests.Check(result.grid[index] == 13, $"{(green ? "replacement" : "upstream")} actual ParameterizePathJob emitted IL executes");
        var codes = PatchProcessor.GetCurrentInstructions(m);
        bool badByref = codes.Any(c => c.opcode == System.Reflection.Emit.OpCodes.Ldarga_S && Convert.ToInt32(c.operand) is 1 or 2);
        Tests.Check(badByref != green, green ? "no double-address ref arguments in replacement IL" : "RED: ldarga on ref arguments 1/2 confirmed; unused upstream currently tolerates it");
    }
    private static bool MoveTicks(ref float __result) { __result = 13; return false; }

    private static void AssertDispose(Fixture f)
    {
        // Real Map.Dispose executes its ownership boundary; headless fixture only
        // omits rendering-owned subsystems. Track disposal through actual native pointer.
        object table = AccessTools.Field(typeof(PathGrids), "Maps").GetValue(null)!;
        object?[] lookup = [f.Map, null];
        bool existed = (bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, lookup)!;
        Tests.Check(existed, "native output ownership registered for map");
        f.Map.pathing = null; f.Map.snowGrid = null; f.Map.fogGrid = null;
        f.Map.Dispose();
        Tests.Check(PathGrids.LiveAdjustedBuffers == 0, "map teardown disposes every adjusted native buffer");
        lookup = [f.Map, null];
        Tests.Check(!(bool)table.GetType().GetMethod("TryGetValue")!.Invoke(table, lookup)!, "real Map.Dispose releases sidecar output ownership");
    }
    private static NativeArray<int>.ReadOnly UpstreamProcess(NativeArray<int> grid, PathRequest req)
    {
        return (NativeArray<int>.ReadOnly)Program.Dnaf!.GetType("DoNotAvoidFire.HarmonyPrivate_Patch")!.GetMethod("ProcessIgnoreFire")!.Invoke(null,
            [grid, req, default(PathFinder.MapGridRequest), default(PathFinderJob), default(NativeArray<ushort>), default(NativeArray<bool>), default(JobHandle)])!;
    }
}
