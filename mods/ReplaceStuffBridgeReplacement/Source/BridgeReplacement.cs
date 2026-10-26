using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.ReplaceStuffBridgeReplacement;

public sealed class ReplaceStuffBridgeReplacementMod : Mod
{
    public ReplaceStuffBridgeReplacementMod(ModContentPack content) : base(content)
    {
        BridgeReplacement.Apply();
    }
}

public static class BridgeReplacement
{
    public const string HarmonyId = "whitegiver.replacestuff.bridgereplacement";
    private const string ReplaceStuffHarmonyId = "Uuugggg.rimworld.Replace_Stuff.main";
    private const string LogPrefix = "[Replace Stuff Bridge Replacement]";
    private static bool applied;
    private static int mapLocalIndex;

    private static MethodInfo? foundationAt;
    private static MethodInfo? getTerrain;
    private static MethodInfo? getAffordances;
    private static MethodInfo? setFoundation;
    private static MethodInfo? foundationForPlacement;
    private static MethodInfo? terrainForBuild;
    private static MethodInfo? affordancesForBuild;
    private static MethodInfo? completeFoundation;

    public static void Apply()
    {
        if (applied) return;

        var harmony = new Harmony(HarmonyId);
        MethodInfo[] targets = [];
        try
        {
            MethodInfo place = Required(AccessTools.DeclaredMethod(typeof(GenConstruct),
                nameof(GenConstruct.CanPlaceBlueprintAt_NewTemp),
                [
                    typeof(BuildableDef), typeof(IntVec3), typeof(Rot4), typeof(Map), typeof(bool), typeof(Thing),
                    typeof(Thing), typeof(ThingDef), typeof(bool), typeof(bool), typeof(bool), typeof(Predicate<Thing>)
                ]), nameof(GenConstruct.CanPlaceBlueprintAt_NewTemp));
            MethodInfo build = Required(AccessTools.DeclaredMethod(typeof(GenConstruct), nameof(GenConstruct.CanBuildOnTerrain),
                [typeof(BuildableDef), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(Thing), typeof(ThingDef)]),
                nameof(GenConstruct.CanBuildOnTerrain));
            MethodInfo complete = Required(AccessTools.DeclaredMethod(typeof(Frame), nameof(Frame.CompleteConstruction),
                [typeof(Pawn)]), nameof(Frame.CompleteConstruction));
            targets = [place, build, complete];

            foundationAt = Required(AccessTools.DeclaredMethod(typeof(TerrainGrid), nameof(TerrainGrid.FoundationAt),
                [typeof(IntVec3)]), nameof(TerrainGrid.FoundationAt));
            getTerrain = Required(AccessTools.DeclaredMethod(typeof(GridsUtility), nameof(GridsUtility.GetTerrain),
                [typeof(IntVec3), typeof(Map)]), nameof(GridsUtility.GetTerrain));
            getAffordances = Required(AccessTools.DeclaredMethod(typeof(GridsUtility), nameof(GridsUtility.GetAffordances),
                [typeof(IntVec3), typeof(Map)]), nameof(GridsUtility.GetAffordances));
            setFoundation = Required(AccessTools.DeclaredMethod(typeof(TerrainGrid), nameof(TerrainGrid.SetFoundation),
                [typeof(IntVec3), typeof(TerrainDef)]), nameof(TerrainGrid.SetFoundation));
            foundationForPlacement = Required(AccessTools.DeclaredMethod(typeof(BridgeReplacement), nameof(FoundationForPlacement)),
                nameof(FoundationForPlacement));
            terrainForBuild = Required(AccessTools.DeclaredMethod(typeof(BridgeReplacement), nameof(TerrainForBuild)),
                nameof(TerrainForBuild));
            affordancesForBuild = Required(AccessTools.DeclaredMethod(typeof(BridgeReplacement), nameof(AffordancesForBuild)),
                nameof(AffordancesForBuild));
            completeFoundation = Required(AccessTools.DeclaredMethod(typeof(BridgeReplacement), nameof(CompleteFoundation)),
                nameof(CompleteFoundation));

            RequireCalls(place, foundationAt, 2);
            RequireCalls(build, getTerrain, 1);
            RequireCalls(build, getAffordances, 1);
            RequireCalls(complete, setFoundation, 1);
            LocalVariableInfo? mapLocal = complete.GetMethodBody()?.LocalVariables
                .SingleOrDefault(local => local.LocalType == typeof(Map));
            if (mapLocal == null) throw new InvalidOperationException($"{LogPrefix} CompleteConstruction must have exactly one Map local.");
            mapLocalIndex = mapLocal.LocalIndex;

            harmony.Patch(place, transpiler: new HarmonyMethod(typeof(BridgeReplacement), nameof(PlaceTranspiler)));
            var buildTranspiler = new HarmonyMethod(typeof(BridgeReplacement), nameof(BuildTranspiler))
            {
                after = [ReplaceStuffHarmonyId]
            };
            harmony.Patch(build, transpiler: buildTranspiler);
            harmony.Patch(complete, transpiler: new HarmonyMethod(typeof(BridgeReplacement), nameof(CompleteTranspiler)));
            applied = true;
            Log.Message($"{LogPrefix} Patched bridge placement, terrain validation and successful-completion refunds.");
        }
        catch (Exception exception)
        {
            foreach (MethodInfo target in targets)
            {
                try
                {
                    harmony.Unpatch(target, HarmonyPatchType.Transpiler, HarmonyId);
                }
                catch (Exception rollbackException)
                {
                    Log.Error($"{LogPrefix} Could not roll back {target.DeclaringType?.Name}.{target.Name}: {rollbackException}");
                }
            }
            Log.Error($"{LogPrefix} Could not apply bridge replacement patches. Rollback was attempted; " +
                $"any rollback failure is logged above and may leave the group incompletely reverted: {exception}");
        }
    }

    private static MethodInfo Required(MethodInfo? method, string name) =>
        method ?? throw new InvalidOperationException($"{LogPrefix} Expected method is missing: {name}.");

    private static void RequireCalls(MethodInfo target, MethodInfo original, int expected)
    {
        int count = PatchProcessor.GetCurrentInstructions(target).Count(instruction => instruction.Calls(original));
        if (count != expected)
        {
            throw new InvalidOperationException($"{LogPrefix} {target.DeclaringType?.Name}.{target.Name} must contain " +
                $"{expected} {original.DeclaringType?.Name}.{original.Name} call(s), found {count}.");
        }
    }

    private static IEnumerable<CodeInstruction> PlaceTranspiler(IEnumerable<CodeInstruction> instructions) =>
        ReplaceCalls(instructions, foundationAt!, foundationForPlacement!, 2, () => new CodeInstruction(OpCodes.Ldarg_0));

    private static IEnumerable<CodeInstruction> BuildTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        IEnumerable<CodeInstruction> terrain = ReplaceCalls(instructions, getTerrain!, terrainForBuild!, 1,
            () => new CodeInstruction(OpCodes.Ldarg_0));
        return ReplaceCalls(terrain, getAffordances!, affordancesForBuild!, 1, () => new CodeInstruction(OpCodes.Ldarg_0));
    }

    private static IEnumerable<CodeInstruction> CompleteTranspiler(IEnumerable<CodeInstruction> instructions) =>
        ReplaceCalls(instructions, setFoundation!, completeFoundation!, 1,
            () => new CodeInstruction(OpCodes.Ldloc, mapLocalIndex));

    private static IEnumerable<CodeInstruction> ReplaceCalls(IEnumerable<CodeInstruction> instructions,
        MethodInfo original, MethodInfo replacement, int expected, Func<CodeInstruction> loadArgument)
    {
        List<CodeInstruction> codes = instructions.ToList();
        List<int> matches = codes.Select((instruction, index) => (instruction, index))
            .Where(pair => pair.instruction.Calls(original)).Select(pair => pair.index).ToList();
        if (matches.Count != expected)
        {
            throw new InvalidOperationException($"{LogPrefix} Expected {expected} {original.DeclaringType?.Name}." +
                $"{original.Name} call(s), found {matches.Count}.");
        }

        foreach (int index in matches.OrderByDescending(index => index))
        {
            CodeInstruction call = codes[index];
            CodeInstruction load = loadArgument();
            load.labels.AddRange(call.labels);
            load.blocks.AddRange(call.blocks);
            call.labels.Clear();
            call.blocks.Clear();
            call.opcode = OpCodes.Call;
            call.operand = replacement;
            codes.Insert(index, load);
        }
        return codes;
    }

    private static bool IsFoundationBridge(TerrainDef? terrain) => terrain is { isFoundation: true, bridge: true };

    private static bool IsReplacement(BuildableDef buildable, TerrainDef? oldFoundation, TerrainGrid grid, IntVec3 cell) =>
        buildable is TerrainDef next && IsFoundationBridge(next) && IsFoundationBridge(oldFoundation) &&
        !ReferenceEquals(oldFoundation, next) && grid.UnderTerrainAt(cell) == null;

    private static TerrainDef BareTerrain(TerrainGrid grid, IntVec3 cell) =>
        grid.TempTerrainAt(cell) ?? grid.TopTerrainAt(cell);

    private static TerrainDef? FoundationForPlacement(TerrainGrid grid, IntVec3 cell, BuildableDef entDef)
    {
        TerrainDef? oldFoundation = grid.FoundationAt(cell);
        return IsReplacement(entDef, oldFoundation, grid, cell) ? null : oldFoundation;
    }

    private static TerrainDef TerrainForBuild(IntVec3 cell, Map map, BuildableDef entDef)
    {
        TerrainGrid grid = map.terrainGrid;
        TerrainDef? oldFoundation = grid.FoundationAt(cell);
        return IsReplacement(entDef, oldFoundation, grid, cell) ? BareTerrain(grid, cell) : cell.GetTerrain(map);
    }

    private static List<TerrainAffordanceDef> AffordancesForBuild(IntVec3 cell, Map map, BuildableDef entDef)
    {
        TerrainGrid grid = map.terrainGrid;
        TerrainDef? oldFoundation = grid.FoundationAt(cell);
        return IsReplacement(entDef, oldFoundation, grid, cell)
            ? BareTerrain(grid, cell).affordances
            : cell.GetAffordances(map);
    }

    private static void CompleteFoundation(TerrainGrid grid, IntVec3 cell, TerrainDef next, Map map)
    {
        TerrainDef? oldFoundation = grid.FoundationAt(cell);
        bool replacing = IsReplacement(next, oldFoundation, grid, cell);
        grid.SetFoundation(cell, next);
        if (replacing && oldFoundation != null && ReferenceEquals(grid.FoundationAt(cell), next))
        {
            GenLeaving.DoLeavingsFor(oldFoundation, cell, map);
        }
    }
}
