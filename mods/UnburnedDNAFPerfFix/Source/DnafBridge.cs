using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Unity.Collections;
using Unity.Jobs;
using Verse;
using Verse.AI;

namespace UnburnedDNAFPerfFix;

public static class DnafBridge
{
    private const string UpstreamOwner = "AntiHydrogen.DoNotAvoidFire.HarmonyInit";
    private static Type componentType = null!;
    private static FieldInfo fireCount = null!, cached = null!, previous = null!;

    internal static void Install(Harmony h, Assembly upstream)
    {
        componentType = upstream.GetType("DoNotAvoidFire.DNAFMapComponent", true)!;
        fireCount = AccessTools.Field(componentType, "firecount");
        cached = AccessTools.Field(componentType, "cachedGrid");
        previous = AccessTools.Field(componentType, "prevGrid");
        MapSnapshot.GroundCountChanged = (map, count) =>
        {
            if (Component(map) is { } c) fireCount.SetValue(c, count);
        };
        foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToArray())
        {
            var info = Harmony.GetPatchInfo(original);
            foreach (var patch in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                         .Where(p => p.owner == UpstreamOwner).ToArray()) h.Unpatch(original, patch.PatchMethod);
        }
        h.Patch(AccessTools.Constructor(componentType, [typeof(Map)]), postfix: HM(nameof(ComponentCreated)));
        h.Patch(AccessTools.Method(componentType, "UpdateFire"), prefix: HM(nameof(UpdateFire)));
        h.Patch(AccessTools.Method(componentType, "GridRemoveFireCost"), prefix: HM(nameof(RemoveCostPrefix)));
        h.Patch(AccessTools.Method(upstream.GetType("DoNotAvoidFire.HarmonyPrivate_Patch"), "ProcessIgnoreFire"), prefix: HM(nameof(ProcessPrefix)));
        h.Patch(AccessTools.Method(typeof(PathFinder), "ParameterizePathJob"), transpiler: HM(nameof(Parameterize)));
        h.Patch(AccessTools.Method(typeof(PathGrid), nameof(PathGrid.CalculatedCostAt)), transpiler: HM(nameof(RecordCosts)));
        h.Patch(AccessTools.Method(typeof(CostSource), nameof(CostSource.ComputeAll)), postfix: HM(nameof(CostsCopied)));
        h.Patch(AccessTools.Method(typeof(CostSource), nameof(CostSource.UpdateIncrementally)), postfix: HM(nameof(CostsUpdated)));
        h.Patch(AccessTools.Method(typeof(PersistentDangerSource), nameof(PersistentDangerSource.ComputeAll)), postfix: HM(nameof(DangersCopied)));
        h.Patch(AccessTools.Method(typeof(PersistentDangerSource), nameof(PersistentDangerSource.UpdateIncrementally)), postfix: HM(nameof(DangersUpdated)));
        h.Patch(AccessTools.Method(typeof(PathFinderMapData), nameof(PathFinderMapData.ParameterizeGridJob)), postfix: HM(nameof(GridScheduled)));
        h.Patch(AccessTools.Method(typeof(PathFinder), "RecycleGridJobData"), prefix: HM(nameof(Recycle)));
        h.Patch(AccessTools.Method(typeof(PathFinder), nameof(PathFinder.Dispose)), postfix: HM(nameof(Recycle)));
        h.Patch(AccessTools.Method(typeof(Map), nameof(Map.Dispose)), postfix: HM(nameof(MapDisposed)));
        h.Patch(AccessTools.Method(typeof(Map), nameof(Map.FinalizeInit)), postfix: HM(nameof(MapInitialized)));
    }

    private static HarmonyMethod HM(string name) => new(typeof(DnafBridge), name);
    private static object? Component(Map map) => map.components.FirstOrDefault(componentType.IsInstanceOfType);
    private static void ComponentCreated(object __instance)
    {
        // These two allocations belong to the original ctor. Dispose them before
        // prevGrid can be overwritten with a *borrowed* vanilla grid alias.
        ReleaseOwned(__instance);
    }
    private static void ReleaseOwned(object component)
    {
        NativeArray<int> a = (NativeArray<int>)cached.GetValue(component)!;
        NativeArray<int> b = (NativeArray<int>)previous.GetValue(component)!;
        if (b.IsCreated && !b.Equals(a)) b.Dispose();
        if (a.IsCreated) a.Dispose();
        cached.SetValue(component, default(NativeArray<int>)); previous.SetValue(component, default(NativeArray<int>));
    }
    private static bool UpdateFire(object __instance)
    {
        Map map = ((MapComponent)__instance).map;
        MapSnapshot.RefreshFires(map);
        fireCount.SetValue(__instance, MapSnapshot.Get(map).GroundFireCount);
        return false;
    }
    private static void MapInitialized(Map __instance)
    {
        MapSnapshot.RefreshFires(__instance);
        if (Component(__instance) is { } c) fireCount.SetValue(c, MapSnapshot.Get(__instance).GroundFireCount);
    }
    private static void MapDisposed(Map __instance)
    {
        PathGrids.Release(__instance);
        if (Component(__instance) is { } c) ReleaseOwned(c);
    }
    private static void Recycle(Map ___map) => PathGrids.Recycle(___map);

    public static bool RemoveCostPrefix(MapComponent __instance, in NativeArray<int> OriginalGrid,
        ref JobHandle handle, ref NativeArray<int> __result)
    {
        if (__instance.map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count == 0)
        {
            __result = OriginalGrid;
            return false;
        }
        handle.Complete();
        __result = PathGrids.AdjustBuffer(__instance.map, OriginalGrid);
        return false;
    }

    public static bool ProcessPrefix(in NativeArray<int> grid, PathRequest request,
        ref JobHandle handle, ref NativeArray<int>.ReadOnly __result)
    {
        __result = Process(grid, request, ref handle);
        return false;
    }

    public static NativeArray<int>.ReadOnly Process(in NativeArray<int> grid, PathRequest request, ref JobHandle handle)
    {
        Pawn? pawn = request?.pawn;
        if (pawn == null || pawn.Destroyed || !pawn.Spawned) return grid.AsReadOnly();
        Map map = pawn.Map;
        if (map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count == 0) return grid.AsReadOnly();
        if (MapSnapshot.Get(map).GroundFireCount == 0) return grid.AsReadOnly();
        if (!(pawn.GetStatValue(StatDefOf.Flammability) < 0.01f)) return grid.AsReadOnly();
        // Completing only this source job is sufficient: adjusted outputs are immutable
        // and unique per ScheduledGridJob, never overwritten under a running path job.
        handle.Complete();
        return PathGrids.Adjust(map, grid);
    }

    public static IEnumerable<CodeInstruction> Parameterize(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        var codes = instructions.ToList();
        ParameterInfo request = original.GetParameters().Single(p => p.ParameterType == typeof(PathRequest));
        ParameterInfo scheduled = original.GetParameters().Single(p => p.Name == "scheduledGridJob");
        int hits = 0;
        for (int i = 0; i < codes.Count; i++)
        {
            CodeInstruction code = codes[i];
            if (code.operand is MethodInfo m && m.Name == "AsReadOnly" && m.DeclaringType == typeof(NativeArray<int>)
                && i > 0 && codes[i - 1].operand is FieldInfo f && f.Name == "grid")
            {
                yield return new CodeInstruction(OpCodes.Ldarg, request.Position + 1).MoveLabelsFrom(code);
                yield return new CodeInstruction(OpCodes.Ldarg, scheduled.Position + 1);
                yield return new CodeInstruction(OpCodes.Ldflda, AccessTools.Field(scheduled.ParameterType.GetElementType(), "handle"));
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DnafBridge), nameof(Process)));
                hits++;
            }
            else yield return code;
        }
        if (hits != 1) throw new InvalidOperationException($"DNAF 1.7 path grid anchor: expected 1, found {hits}");
    }

    // Capture the additive fire delta at its actual producer. No secondary 9-cell
    // traversal and no assumptions about a particular numbered local.
    public static IEnumerable<CodeInstruction> RecordCosts(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codes = instructions.ToList();
        var baseline = generator.DeclareLocal(typeof(int));
        int start = codes.FindIndex(c => c.opcode == OpCodes.Ldarg_2);
        if (start < 0 || codes.Count(c => c.opcode == OpCodes.Ldarg_2) != 1)
            throw new InvalidOperationException("Expected one perceivedStatic fire block");
        var accumulator = codes[^2];
        if (!accumulator.IsLdloc()) throw new InvalidOperationException("Missing path cost accumulator");
        yield return new CodeInstruction(OpCodes.Ldc_I4_M1);
        yield return new CodeInstruction(OpCodes.Stloc, baseline);
        for (int i = 0; i < codes.Count; i++)
        {
            var code = codes[i];
            if (i == start)
            {
                yield return new CodeInstruction(accumulator.opcode, accumulator.operand).MoveLabelsFrom(code);
                yield return new CodeInstruction(OpCodes.Stloc, baseline);
            }
            if (code.opcode == OpCodes.Ret)
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code);
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Ldloc, baseline);
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PathGrids), nameof(PathGrids.Record)));
            }
            yield return code;
        }
    }
    private static void CostsCopied(PathingContext ___context, NativeArray<int> ___data) => PathGrids.CopyCosts(___context, ___data, null);
    private static void CostsUpdated(PathingContext ___context, NativeArray<int> ___data, List<IntVec3> cellDeltas) => PathGrids.CopyCosts(___context, ___data, cellDeltas);
    private static void DangersCopied(Map ___map) => PathGrids.CopyDanger(___map, null);
    private static void DangersUpdated(Map ___map, List<IntVec3> cellDeltas) => PathGrids.CopyDanger(___map, cellDeltas);
    private static void GridScheduled(Map ___map, ref PathGridJob job) => PathGrids.Capture(___map, job);
}
