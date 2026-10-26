using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;
using WhiteGiver.ReinforcedConcreteHeavyBridge;

internal static class AutoBridgeValidation
{
    private const string Owner = "whitegiver.reinforcedconcrete.heavybridge";
    private const BindingFlags Fields = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<BuildableDef> Placed = [];

    internal static void Run(Assembly mod, Assembly? replace, Game game, TerrainDef concrete)
    {
        MethodBase[] owned = Harmony.GetAllPatchedMethods()
            .Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true).ToArray();
        if (replace == null)
        {
            Check(owned.Length == 0, "Absent Replace Stuff must not patch the game.");
            Console.WriteLine("PASS: Replace Stuff absent; Mod/settings/manual bridge remain usable without any automatic-placement patches.");
            return;
        }

        MethodInfo target = replace.GetType("Replace_Stuff.PlaceBridges.PlaceBridges", true)!.GetMethod("GetNeededBridge")!;
        Check(owned.SequenceEqual(new[] { target }) && Harmony.GetPatchInfo(target)!.Postfixes.Count(p => p.owner == Owner) == 1,
            "Actual Mod constructor must register exactly one GetNeededBridge postfix, despite settings reloads.");
        var getBridge = (Func<BuildableDef, IntVec3, Map, ThingDef?, TerrainDef?>)target.CreateDelegate(
            typeof(Func<BuildableDef, IntVec3, Map, ThingDef?, TerrainDef?>));
        TerrainDef vanilla = DefDatabase<TerrainDef>.GetNamed("HeavyBridge");
        TerrainAffordanceDef heavy = DefDatabase<TerrainAffordanceDef>.GetNamed("Heavy");
        TerrainAffordanceDef light = DefDatabase<TerrainAffordanceDef>.GetNamed("Light");
        TerrainAffordanceDef bridgeable = DefDatabase<TerrainAffordanceDef>.GetNamed("Bridgeable");
        TerrainAffordanceDefOf.Light = light;
        TerrainAffordanceDefOf.Medium = DefDatabase<TerrainAffordanceDef>.GetNamed("Medium");
        TerrainAffordanceDefOf.Heavy = heavy;
        foreach (string name in new[] { "Diggable", "GrowSoil" }) Add(new TerrainAffordanceDef { defName = name });
        var water = new TerrainDef { defName = "AutoBridgeWater", affordances = [bridgeable] };
        var dry = new TerrainDef { defName = "AutoBridgeDry", affordances = [light, heavy] };
        var wood = new TerrainDef
        {
            defName = "Bridge", layerable = true, bridge = true, isFoundation = true,
            terrainAffordanceNeeded = bridgeable, affordances = [light], costList = [new(DefDatabase<ThingDef>.GetNamed("Steel"), 12)]
        };
        Add(water);
        Add(dry);
        Add(wood);
        var building = new ThingDef
        {
            defName = "AutoBridgeHeavyBuilding", category = ThingCategory.Building,
            building = new BuildingProperties(), terrainAffordanceNeeded = heavy
        };
        Add(building);
        var lightBuilding = new ThingDef
        {
            defName = "AutoBridgeLightBuilding", category = ThingCategory.Building,
            building = new BuildingProperties(), terrainAffordanceNeeded = light
        };
        Add(lightBuilding);

        var map = new Map();
        map.info.Size = new IntVec3(4, 1, 4);
        map.cellIndices = new CellIndices(map);
        map.terrainGrid = new TerrainGrid(map);
        Array.Fill(map.terrainGrid.topGrid, water);
        map.thingGrid = new ThingGrid(map);
        map.resourceCounter = new ResourceCounter(map);
        foreach (ThingDef material in new[] { vanilla.costList[0].thingDef, concrete.costList[0].thingDef })
        {
            material.resourceReadoutPriority = ResourceCountPriority.Middle;
            map.resourceCounter.AllCountedAmounts[material] = 0;
        }
        SetField(typeof(Game), "maps", game, new List<Map> { map });

        Type bridgesType = replace.GetType("Replace_Stuff.PlaceBridges.BridgelikeTerrain", true)!;
        RuntimeHelpers.RunClassConstructor(bridgesType.TypeHandle);
        var bridges = (List<TerrainDef>)bridgesType.GetField("allBridgeTerrains")!.GetValue(null)!;
        Check(bridges.Contains(concrete) && bridges.Contains(vanilla), "Replace Stuff did not discover both heavy bridges from real definitions.");
        MethodInfo findMethod = bridgesType.GetMethod("FindBridgeFor")!;
        var findBridge = (Func<TerrainDef, TerrainAffordanceDef, Map, TerrainDef?>)findMethod.CreateDelegate(
            typeof(Func<TerrainDef, TerrainAffordanceDef, Map, TerrainDef?>));
        BridgeSettings settings = (BridgeSettings)mod.GetType("WhiteGiver.ReinforcedConcreteHeavyBridge.BridgeMod", true)!
            .GetProperty("Settings", Fields)!.GetValue(null)!;
        var progress = (Dictionary<ResearchProjectDef, float>)GetField(typeof(ResearchManager), "progress", game.researchManager)!;
        IntVec3 cell = new(1, 0, 1);
        foreach ((bool heavyDone, bool concreteDone) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            progress[concrete.researchPrerequisites[0]] = heavyDone ? 800 : 0;
            progress[concrete.researchPrerequisites[1]] = concreteDone ? 250 : 0;
            foreach (bool choice in new[] { false, true })
            {
                settings.AutoUseReinforcedConcrete = choice;
                TerrainDef? expected = !heavyDone ? null : choice && concreteDone ? concrete : vanilla;
                Check(ReferenceEquals(getBridge(building, cell, map, null), expected), "Automatic choice bypassed research or ignored live settings.");
            }
        }
        settings.AutoUseReinforcedConcrete = false;
        map.resourceCounter.AllCountedAmounts[concrete.costList[0].thingDef] = 1000;
        Check(findBridge(water, heavy, map) == concrete && getBridge(building, cell, map, null) == vanilla,
            "Vanilla choice did not override Replace Stuff choosing concrete from stock.");
        settings.AutoUseReinforcedConcrete = true;
        map.resourceCounter.AllCountedAmounts[concrete.costList[0].thingDef] = 0;
        map.resourceCounter.AllCountedAmounts[vanilla.costList[0].thingDef] = 1000;
        Check(findBridge(water, heavy, map) == vanilla && getBridge(building, cell, map, null) == concrete,
            "Concrete choice did not override Replace Stuff choosing steel from stock.");
        Check(getBridge(building, new IntVec3(-1, 0, 0), map, null) == null, "Out-of-bounds result changed.");
        map.terrainGrid.topGrid[0] = dry;
        Check(getBridge(building, IntVec3.Zero, map, null) == null, "Dry supported terrain unexpectedly acquired a bridge.");
        map.terrainGrid.foundationGrid[map.cellIndices.CellToIndex(cell)] = vanilla;
        Check(getBridge(building, cell, map, null) == null, "Built bridge unexpectedly acquired another foundation.");
        map.terrainGrid.foundationGrid[map.cellIndices.CellToIndex(cell)] = null!;

        var candidates = (Dictionary<(TerrainDef, TerrainAffordanceDef), HashSet<TerrainDef>>)bridgesType
            .GetField("bridgesForTerrain", Fields)!.GetValue(null)!;
        HashSet<TerrainDef> lightCandidates = candidates[(water, light)];
        candidates[(water, light)] = [wood];
        Check(getBridge(lightBuilding, cell, map, null) == wood, "Wooden bridge selection changed.");
        candidates[(water, light)] = lightCandidates;
        var unrelatedBridge = new TerrainDef { defName = "OtherHeavyBridge", affordances = [heavy] };
        bridges.Add(unrelatedBridge);
        HashSet<TerrainDef> heavyCandidates = candidates[(water, heavy)];
        candidates[(water, heavy)] = [unrelatedBridge];
        Check(getBridge(building, cell, map, null) == unrelatedBridge, "Third-party bridge selection changed.");
        candidates[(water, heavy)] = heavyCandidates;
        bridges.Remove(unrelatedBridge);

        ValidatePreviewCalls(replace, target);
        ValidatePlacement(replace, getBridge, map, cell, building, concrete, vanilla, settings);
        Console.WriteLine("PASS: real Replace Stuff discovery/selection and actual single Harmony postfix; both choices, four research states, stock independence, wood/null/dry/third-party and existing-site boundaries.");
    }

    private static void ValidatePreviewCalls(Assembly replace, MethodInfo target)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(replace.Location);
        MethodDefinition preview = assembly.MainModule.GetType("Replace_Stuff.PlaceBridges.DesignatorBuildCostCountsBridges")
            .Methods.Single(m => m.Name == "Postfix");
        MethodDefinition ensure = assembly.MainModule.GetType("Replace_Stuff.PlaceBridges.EnsureBridge")
            .Methods.Single(m => m.Name == "PlaceBridgeIfNeeded");
        foreach (MethodDefinition method in new[] { preview, ensure })
        {
            Check(method.Body.Instructions.Count(i => i.Operand is MethodReference reference &&
                reference.DeclaringType.FullName == target.DeclaringType!.FullName && reference.Name == target.Name) == 1,
                "Material preview and actual placement no longer share exactly one selection call.");
        }
        Console.WriteLine("PASS: installed Replace Stuff IL confirms mouse material preview and actual placement share the patched selector; rendered GUI not tested.");
    }

    private static void ValidatePlacement(Assembly replace, Func<BuildableDef, IntVec3, Map, ThingDef?, TerrainDef?> getBridge,
        Map map, IntVec3 cell, ThingDef building, TerrainDef concrete, TerrainDef vanilla, BridgeSettings settings)
    {
        MethodInfo placeBlueprint = AccessTools.DeclaredMethod(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForBuild));
        new Harmony("whitegiver.bridge.validation").Patch(placeBlueprint,
            prefix: new HarmonyMethod(typeof(AutoBridgeValidation), nameof(CaptureBlueprint)));
        var ensure = (Action<BuildableDef, IntVec3, Map, Rot4, Faction?, ThingDef?>)replace
            .GetType("Replace_Stuff.PlaceBridges.EnsureBridge", true)!.GetMethod("PlaceBridgeIfNeeded")!
            .CreateDelegate(typeof(Action<BuildableDef, IntVec3, Map, Rot4, Faction?, ThingDef?>));
        foreach (bool choice in new[] { false, true, false })
        {
            settings.AutoUseReinforcedConcrete = choice;
            TerrainDef expected = choice ? concrete : vanilla;
            Placed.Clear();
            TerrainDef preview = getBridge(building, cell, map, null)!;
            ensure(building, cell, map, Rot4.North, null, null);
            Check(preview == expected && Placed.SequenceEqual(new[] { expected }) &&
                preview.costList[0].thingDef == expected.costList[0].thingDef, "Preview and actual requested blueprint differ.");
        }
        foreach (bool frame in new[] { false, true })
        {
            settings.AutoUseReinforcedConcrete = true;
            Thing existing = frame ? new Frame { def = new ThingDef { entityDefToBuild = vanilla } }
                : new Blueprint_Build { def = new ThingDef { entityDefToBuild = vanilla } };
            map.thingGrid.ThingsListAt(cell).Add(existing);
            Placed.Clear();
            Check(getBridge(building, cell, map, null) == vanilla, "Existing vanilla site was not preserved.");
            ensure(building, cell, map, Rot4.North, null, null);
            Check(Placed.Count == 0, "Existing vanilla site acquired a second concrete blueprint.");
            existing.def.entityDefToBuild = concrete;
            settings.AutoUseReinforcedConcrete = false;
            Check(getBridge(building, cell, map, null) == concrete, "Existing concrete site was not preserved.");
            ensure(building, cell, map, Rot4.North, null, null);
            Check(Placed.Count == 0, "Existing concrete site acquired a second steel blueprint.");
            map.thingGrid.ThingsListAt(cell).Remove(existing);
        }
        Console.WriteLine("PASS: actual EnsureBridge requests the chosen blueprint, previews match material, existing steel/concrete blueprints and frames prevent duplicates; final GenConstruct spawn isolated.");
    }

    private static bool CaptureBlueprint(BuildableDef sourceDef, ref Blueprint_Build? __result)
    {
        Placed.Add(sourceDef);
        __result = null;
        return false;
    }

    private static void Add<T>(T def) where T : Def, new()
    {
        def.ResolveDefNameHash();
        DefDatabase<T>.Add(def);
    }
    private static object? GetField(Type type, string name, object? target) => type.GetField(name, Fields)!.GetValue(target);
    private static void SetField(Type type, string name, object? target, object value) => type.GetField(name, Fields)!.SetValue(target, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
