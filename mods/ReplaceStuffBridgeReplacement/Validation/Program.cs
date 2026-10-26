using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;
using Verse.AI;

internal static class Program
{
    private static readonly string HarmonyDirectory = Environment.GetEnvironmentVariable("HARMONY_DIRECTORY")
        ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    internal static readonly string ReplaceStuffAssembly = Environment.GetEnvironmentVariable("REPLACE_STUFF_ASSEMBLY")
        ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\3526354009\1.7\Assemblies\Replace_Stuff.dll";

    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Usage: Validation bind GAME_ROOT QA_ROOT | run GAME_ROOT MOD_DLL QA_ROOT");
            return 0;
        }
        bool bind = args.Length == 3 && args[0] == "bind";
        bool run = args.Length == 4 && args[0] == "run";
        if (!bind && !run)
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }

        string gameRoot = Path.GetFullPath(args[1]);
        string modDll = run ? Path.GetFullPath(args[2]) : "";
        string qaRoot = Path.GetFullPath(args[^1]);
        string bound = Path.Combine(qaRoot, "bound");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { bound, gameRoot, HarmonyDirectory })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            if (bind) Bind(gameRoot, bound);
            else Assembly.GetExecutingAssembly().GetType("Driver")!.GetMethod("Run")!.Invoke(null, [modDll, qaRoot]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind(string gameRoot, string bound)
    {
        Directory.CreateDirectory(bound);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(HarmonyDirectory);
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(gameRoot, "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = resolver });
        foreach (string name in new[] { "Verse.ModsConfig", "RimWorld.DesignationDefOf", "RimWorld.MapMeshFlagDefOf" })
        {
            MethodDefinition initializer = game.MainModule.GetType(name).Methods.Single(method => method.Name == ".cctor");
            initializer.Body.Instructions.Clear();
            initializer.Body.ExceptionHandlers.Clear();
            initializer.Body.Variables.Clear();
            initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
        }

        // 仅替代最终地图落物与配置/静态 Def 初始化；地形、放置校验和返料计算保持真实。
        MethodDefinition drop = game.MainModule.GetType("Verse.GenDrop").Methods.Single(method => method.Name == "TryDropSpawn");
        var validation = new AssemblyNameReference("Validation", new Version(1, 0, 0, 0));
        game.MainModule.AssemblyReferences.Add(validation);
        var hook = new MethodReference("Drop", game.MainModule.TypeSystem.Boolean,
            new TypeReference("", "ValidationHooks", game.MainModule, validation));
        foreach (ParameterDefinition parameter in drop.Parameters) hook.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
        drop.Body.Instructions.Clear();
        drop.Body.ExceptionHandlers.Clear();
        drop.Body.Variables.Clear();
        ILProcessor il = drop.Body.GetILProcessor();
        foreach (ParameterDefinition parameter in drop.Parameters) il.Emit(OpCodes.Ldarg, parameter);
        il.Emit(OpCodes.Call, hook);
        il.Emit(OpCodes.Ret);
        game.Write(Path.Combine(bound, "Assembly-CSharp.dll"));

        using var replace = AssemblyDefinition.ReadAssembly(ReplaceStuffAssembly, new ReaderParameters { AssemblyResolver = resolver });
        foreach (AssemblyNameReference reference in replace.MainModule.AssemblyReferences)
        {
            if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
            reference.Name = "UnityStubs";
            reference.Version = new Version(1, 0, 0, 0);
        }
        replace.Write(Path.Combine(bound, "Replace_Stuff.dll"));
        Console.WriteLine("PASS: QA-only assemblies bound; only config/static-def startup and final item-drop placement are isolated.");
    }
}

internal static class Driver
{
    private const string Owner = "whitegiver.replacestuff.bridgereplacement";
    private const string ReplaceStuffOwner = "Uuugggg.rimworld.Replace_Stuff.main";
    private const string ValidationOwner = "whitegiver.bridge.replacement.validation";
    private const BindingFlags Fields = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(string modDll, string qaRoot)
    {
        Assembly replace = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(qaRoot, "bound", "Replace_Stuff.dll"));
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(modDll);
        Check(patch.GetReferencedAssemblies().Any(reference => reference.Name == "0Harmony" && reference.Version == new Version(3, 0, 0, 0)),
            "Harmony 3 reference missing.");
        Check(!patch.GetReferencedAssemblies().Any(reference => reference.Name == "Replace_Stuff"),
            "Sidecar unexpectedly acquired a hard Replace Stuff assembly reference.");

        MethodInfo build = AccessTools.DeclaredMethod(typeof(GenConstruct), nameof(GenConstruct.CanBuildOnTerrain))!;
        MethodInfo place = AccessTools.DeclaredMethod(typeof(GenConstruct), nameof(GenConstruct.CanPlaceBlueprintAt_NewTemp))!;
        MethodInfo complete = AccessTools.DeclaredMethod(typeof(Frame), nameof(Frame.CompleteConstruction))!;
        var validationHarmony = new Harmony(ValidationOwner);
        validationHarmony.Patch(AccessTools.DeclaredMethod(typeof(Translator), nameof(Translator.TryTranslate),
            [typeof(string), typeof(TaggedString).MakeByRefType()]),
            prefix: new HarmonyMethod(typeof(ValidationHooks), nameof(ValidationHooks.NoTranslation)));
        validationHarmony.Patch(AccessTools.DeclaredMethod(typeof(GenGrid), nameof(GenGrid.InNoBuildEdgeArea),
            [typeof(IntVec3), typeof(Map)]), prefix: new HarmonyMethod(typeof(ValidationHooks), nameof(ValidationHooks.NoBuildEdge)));
        MethodInfo upstreamTranspiler = replace.GetType("Replace_Stuff.PlaceBridges.CanPlaceBlueprint", true)!
            .GetMethod("Transpiler", Fields)!;
        new Harmony(ReplaceStuffOwner).Patch(build, transpiler: new HarmonyMethod(upstreamTranspiler));

        SetupStaticDefs();
        var light = new TerrainAffordanceDef { defName = "Light" };
        var medium = new TerrainAffordanceDef { defName = "Medium" };
        var heavySupport = new TerrainAffordanceDef { defName = "Heavy" };
        var walkable = new TerrainAffordanceDef { defName = "Walkable" };
        var bridgeable = new TerrainAffordanceDef { defName = "Bridgeable" };
        ThingDef woodMaterial = Material("WoodLog");
        ThingDef steel = Material("Steel");
        ThingDef concrete = Material("FT_ReinforcedConcreteBag");
        var water = new TerrainDef
        {
            defName = "ValidationWater", changeable = true, holdSnowOrSand = true, affordances = [bridgeable]
        };
        var dry = new TerrainDef
        {
            defName = "ValidationDry", changeable = true, holdSnowOrSand = true, affordances = [light]
        };
        TerrainDef wood = Bridge("Bridge", bridgeable, [light], woodMaterial, 12);
        TerrainDef heavy = Bridge("HeavyBridge", bridgeable, [light, medium, heavySupport, walkable], steel, 14);
        TerrainDef custom = Bridge("WhiteGiver_ReinforcedConcreteHeavyBridge", bridgeable,
            [light, medium, heavySupport, walkable], concrete, 14);
        var otherFoundation = new TerrainDef
        {
            defName = "ValidationSubstructure", isFoundation = true, changeable = true, holdSnowOrSand = true,
            affordances = [heavySupport]
        };
        Map map = NewMap(water);
        IntVec3 cell = new(1, 0, 1);
        int index = map.cellIndices.CellToIndex(cell);
        map.terrainGrid.foundationGrid[index] = wood;

        Check(!GenConstruct.CanBuildOnTerrain(heavy, cell, map, Rot4.North),
            "RED: upstream unexpectedly allowed heavy bridge construction against wooden-bridge affordances.");
        Check(!CanPlace(heavy, cell, map), "RED: upstream unexpectedly accepted a second foundation.");
        Console.WriteLine("RED: real vanilla + Replace Stuff rejects wood -> heavy before the sidecar, as in the game chain.");

        Type modType = patch.GetType("WhiteGiver.ReplaceStuffBridgeReplacement.ReplaceStuffBridgeReplacementMod", true)!;
        Activator.CreateInstance(modType, new object?[] { null });
        Activator.CreateInstance(modType, new object?[] { null });
        MethodBase[] owned = Harmony.GetAllPatchedMethods()
            .Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true).ToArray();
        Check(owned.Length == 3 && owned.All(method => Harmony.GetPatchInfo(method)!.Transpilers.Count(item => item.owner == Owner) == 1),
            "Actual Mod constructor did not register exactly three idempotent transpilers.");
        Check(owned.Contains(place) && owned.Contains(build) && owned.Contains(complete), "A patch landed on an unintended method.");
        Patches buildPatches = Harmony.GetPatchInfo(build)!;
        Patch[] buildTranspilers = buildPatches.Transpilers.ToArray();
        Check(buildTranspilers.Single(item => item.owner == Owner).after.Contains(ReplaceStuffOwner),
            "Bridge transpiler lacks its explicit Replace Stuff ordering constraint.");
        MethodInfo myBuildTranspiler = patch.GetType("WhiteGiver.ReplaceStuffBridgeReplacement.BridgeReplacement", true)!
            .GetMethod("BuildTranspiler", Fields)!;
        Check(PatchProcessor.GetSortedPatchMethods(build, buildTranspilers)
            .SequenceEqual(new[] { upstreamTranspiler, myBuildTranspiler }),
            "Actual patch sorting does not run Replace Stuff before the bridge transpiler.");
        ValidatePatchedIl(patch, replace, place, build, complete);

        Check(GenConstruct.CanBuildOnTerrain(heavy, cell, map, Rot4.North), "Wood -> heavy terrain validation still fails.");
        Check(CanPlace(heavy, cell, map), "Wood -> vanilla heavy blueprint is not accepted.");
        Check(CanPlace(custom, cell, map), "Wood -> reinforced concrete heavy blueprint is not accepted.");
        map.terrainGrid.foundationGrid[index] = heavy;
        Check(!CanPlace(heavy, cell, map), "Same bridge was incorrectly treated as a replacement.");
        map.terrainGrid.foundationGrid[index] = otherFoundation;
        Check(!CanPlace(heavy, cell, map), "Non-bridge foundation was incorrectly replaceable.");
        map.terrainGrid.foundationGrid[index] = wood;
        UnderGrid(map.terrainGrid)[index] = water;
        Check(!CanPlace(heavy, cell, map), "Under-terrain state unsupported by SetFoundation was allowed.");
        UnderGrid(map.terrainGrid)[index] = null!;
        map.terrainGrid.topGrid[index] = dry;
        Check(!GenConstruct.CanBuildOnTerrain(heavy, cell, map, Rot4.North),
            "A dry/non-bridgeable underlying cell was incorrectly accepted.");
        map.terrainGrid.topGrid[index] = water;
        water.changeable = false;
        Check(!GenConstruct.CanBuildOnTerrain(heavy, cell, map, Rot4.North), "Unchangeable underlying terrain was ignored.");
        water.changeable = true;
        Console.WriteLine("GREEN: both heavy targets accepted; same-def, other foundation, under-terrain, dry and unchangeable boundaries preserved.");

        ValidateQueriesArePure(map, cell, heavy, custom, water, wood);
        ValidateBuildingSupport(map, cell, heavy, custom, wood, heavySupport);
        ValidateUpstreamBridgeFallback(map, cell, heavy, wood, water, heavySupport);
        ValidateConstructionSurvivesTerrainRefresh(map, cell, heavy, wood);
        ValidateCompletionRefunds(patch, map, cell, heavy, custom, wood, water, woodMaterial, steel);
        ValidateModIl(modDll);
        Console.WriteLine("PASS: real placement/terrain methods, construction refresh, support fallback and successful-completion refunds all passed.");
        Console.WriteLine("NOT TESTED: full rendered gameplay, pawn job execution, manual QA of material drops, or an in-progress RemoveFoundation command.");
    }

    private static TerrainDef Bridge(string name, TerrainAffordanceDef needed, TerrainAffordanceDef[] affordances,
        ThingDef material, int cost) => new()
    {
        defName = name, isFoundation = true, bridge = true, layerable = true, changeable = true, holdSnowOrSand = true,
        terrainAffordanceNeeded = needed, affordances = [.. affordances], costList = [new(material, cost)],
        resourcesFractionWhenDeconstructed = 0.5f
    };

    private static ThingDef Material(string name)
    {
        var def = new ThingDef
        {
            defName = name, thingClass = typeof(Thing), category = ThingCategory.Item, stackLimit = 100, useHitPoints = false
        };
        Add(def);
        return def;
    }

    private static void SetupStaticDefs()
    {
        foreach (string name in new[] { "SmoothFloor", "RemoveFloor", "PaintFloor", "RemovePaintFloor" })
        {
            var def = new DesignationDef { defName = name, targetType = TargetType.Cell };
            Add(def);
            typeof(DesignationDefOf).GetField(name, Fields)!.SetValue(null, def);
        }
        var terrainFlag = new MapMeshFlagDef { defName = "Terrain" };
        terrainFlag.PostSetIndices();
        MapMeshFlagDefOf.Terrain = terrainFlag;
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Current.Game = game;
        game.uniqueIDsManager = new UniqueIDsManager();
        game.storyteller = (Storyteller)RuntimeHelpers.GetUninitializedObject(typeof(Storyteller));
        game.storyteller.difficulty = new Difficulty();
        Current.ProgramState = ProgramState.Playing;
    }

    private static Map NewMap(TerrainDef top)
    {
        var map = new Map();
        map.info.Size = new IntVec3(4, 1, 4);
        map.cellIndices = new CellIndices(map);
        map.terrainGrid = new TerrainGrid(map);
        Array.Fill(map.terrainGrid.topGrid, top);
        map.thingGrid = new ThingGrid(map);
        map.fogGrid = (FogGrid)RuntimeHelpers.GetUninitializedObject(typeof(FogGrid));
        map.designationManager = new DesignationManager(map);
        map.mapDrawer = new MapDrawer(map);
        map.pathing = new Pathing(map);
        map.events = new MapEvents(map);
        map.fertilityGrid = new FertilityGrid(map);
        map.regionGrid = (RegionGrid)RuntimeHelpers.GetUninitializedObject(typeof(RegionGrid));
        SetField(typeof(RegionGrid), "map", map.regionGrid, map);
        SetField(typeof(RegionGrid), "regionGrid", map.regionGrid, new Region[map.cellIndices.NumGridCells]);
        return map;
    }

    private static bool CanPlace(BuildableDef def, IntVec3 cell, Map map) => GenConstruct.CanPlaceBlueprintAt(def, cell,
        Rot4.North, map, godMode: false, ignoreEdgeArea: true, ignoreInteractionSpots: true).Accepted;

    private static void ValidateQueriesArePure(Map map, IntVec3 cell, TerrainDef heavy, TerrainDef custom,
        TerrainDef water, TerrainDef wood)
    {
        int index = map.cellIndices.CellToIndex(cell);
        map.terrainGrid.foundationGrid[index] = wood;
        TerrainDef[] top = (TerrainDef[])map.terrainGrid.topGrid.Clone();
        TerrainDef[] foundation = (TerrainDef[])map.terrainGrid.foundationGrid.Clone();
        TerrainDef[] under = (TerrainDef[])UnderGrid(map.terrainGrid).Clone();
        TerrainDef[] temp = (TerrainDef[])TempGrid(map.terrainGrid).Clone();
        ColorDef[] colors = (ColorDef[])map.terrainGrid.colorGrid.Clone();
        int events = 0;
        map.events.TerrainChanged += _ => events++;
        for (int i = 0; i < 250; i++)
        {
            Check(CanPlace(i % 2 == 0 ? heavy : custom, cell, map), "Repeated bridge placement query failed.");
        }
        SetField(typeof(BuildableDef), "placeWorkers", heavy, new List<Type> { typeof(ThrowPlaceWorker) });
        bool threw = false;
        try { CanPlace(heavy, cell, map); }
        catch (InvalidOperationException exception) when (exception.Message == ThrowPlaceWorker.Message) { threw = true; }
        finally
        {
            SetField(typeof(BuildableDef), "placeWorkers", heavy, null!);
            SetField(typeof(BuildableDef), "placeWorkersInstantiatedInt", heavy, null!);
        }
        Check(threw, "PlaceWorker exception probe did not run.");
        Check(top.SequenceEqual(map.terrainGrid.topGrid) && foundation.SequenceEqual(map.terrainGrid.foundationGrid) &&
            under.SequenceEqual(UnderGrid(map.terrainGrid)) && temp.SequenceEqual(TempGrid(map.terrainGrid)) &&
            colors.SequenceEqual(map.terrainGrid.colorGrid) && events == 0,
            "Placement queries mutated terrain or emitted terrain events, including across an exception.");
        Console.WriteLine("PASS: 250 queries and a PlaceWorker exception leave every terrain layer and terrain event stream untouched.");
    }

    private static void ValidateBuildingSupport(Map map, IntVec3 cell, TerrainDef heavy, TerrainDef custom,
        TerrainDef wood, TerrainAffordanceDef heavySupport)
    {
        int index = map.cellIndices.CellToIndex(cell);
        map.terrainGrid.foundationGrid[index] = heavy;
        var buildingDef = new ThingDef
        {
            defName = "ValidationHeavyFloorBuilding", category = ThingCategory.Building,
            building = new BuildingProperties(), terrainAffordanceNeeded = heavySupport
        };
        var building = new Thing { def = buildingDef };
        SetField(typeof(Thing), "positionInt", building, cell);
        map.thingGrid.ThingsListAt(cell).Add(building);
        Check(CanPlace(custom, cell, map), "A compatible coexisting building blocked heavy -> reinforced heavy replacement.");
        Check(!CanPlace(wood, cell, map), "Old heavy bridge illegally lent support to a wooden-bridge downgrade.");
        map.thingGrid.ThingsListAt(cell).Clear();
        map.terrainGrid.foundationGrid[index] = wood;
        Console.WriteLine("PASS: coexisting buildings retain vanilla rules; old heavy support cannot authorize a wooden downgrade.");
    }

    private static void ValidateUpstreamBridgeFallback(Map map, IntVec3 cell, TerrainDef heavy, TerrainDef wood,
        TerrainDef water, TerrainAffordanceDef heavySupport)
    {
        ResetCell(map, cell, water, wood);
        int index = map.cellIndices.CellToIndex(cell);
        map.terrainGrid.foundationGrid[index] = null!;
        var buildingDef = new ThingDef
        {
            defName = "ValidationUpstreamHeavyBuilding", category = ThingCategory.Building,
            building = new BuildingProperties(), terrainAffordanceNeeded = heavySupport
        };
        var blueprintDef = new ThingDef
        {
            defName = "Blueprint_ValidationUpstreamHeavy", category = ThingCategory.Ethereal,
            thingClass = typeof(Blueprint_Build), entityDefToBuild = heavy
        };
        var blueprint = new Blueprint_Build { def = blueprintDef };
        SetField(typeof(Thing), "positionInt", blueprint, cell);
        map.thingGrid.ThingsListAt(cell).Add(blueprint);
        Check(GenConstruct.CanBuildOnTerrain(buildingDef, cell, map, Rot4.North),
            "Replace Stuff's existing-heavy-blueprint fallback broke after bridge replacement patching.");
        ResetCell(map, cell, water, wood);
        Console.WriteLine("PASS: a normal building on bare water still passes through Replace Stuff's existing heavy-bridge blueprint fallback.");
    }

    private static void ValidateConstructionSurvivesTerrainRefresh(Map map, IntVec3 cell, TerrainDef heavy, TerrainDef wood)
    {
        int index = map.cellIndices.CellToIndex(cell);
        map.terrainGrid.foundationGrid[index] = wood;
        var frameDef = new ThingDef
        {
            defName = "Frame_ValidationHeavy", category = ThingCategory.Ethereal, thingClass = typeof(Frame),
            isFrameInt = true, entityDefToBuild = heavy
        };
        var frame = new Frame { def = frameDef };
        SetField(typeof(Thing), "positionInt", frame, cell);
        map.thingGrid.ThingsListAt(cell).Add(frame);
        Check(GenConstruct.CanBuildOnTerrain(heavy, cell, map, Rot4.North, frame),
            "Current replacement frame cannot survive on the old wooden bridge.");
        map.terrainGrid.SetTerrainColor(cell, null);
        Check(!frame.Destroyed && map.thingGrid.ThingsListAt(cell).Contains(frame),
            "Real terrain-change effects cancelled the replacement frame.");
        map.thingGrid.ThingsListAt(cell).Clear();
        Console.WriteLine("PASS: real TerrainGrid.DoTerrainChangedEffects preserves the active replacement frame and ignores it correctly.");
    }

    private static void ValidateCompletionRefunds(Assembly patch, Map map, IntVec3 cell, TerrainDef heavy,
        TerrainDef custom, TerrainDef wood, TerrainDef water, ThingDef woodMaterial, ThingDef steel)
    {
        Type rules = patch.GetType("WhiteGiver.ReplaceStuffBridgeReplacement.BridgeReplacement", true)!;
        var setAndRefund = (Action<TerrainGrid, IntVec3, TerrainDef, Map>)rules.GetMethod("CompleteFoundation", Fields)!
            .CreateDelegate(typeof(Action<TerrainGrid, IntVec3, TerrainDef, Map>));
        int index = map.cellIndices.CellToIndex(cell);
        ValidationHooks.Dropped.Clear();

        ResetCell(map, cell, water, wood);
        setAndRefund(map.terrainGrid, cell, custom, map);
        Check(map.terrainGrid.FoundationAt(cell) == custom && ValidationHooks.Dropped.Count == 1 &&
            ValidationHooks.Dropped[0].def == woodMaterial && ValidationHooks.Dropped[0].stackCount == 6,
            "Wood -> reinforced heavy did not refund exactly six wood after successful replacement.");
        setAndRefund(map.terrainGrid, cell, custom, map);
        Check(ValidationHooks.Dropped.Count == 1, "Repeated same-foundation completion refunded twice.");

        ResetCell(map, cell, water, heavy);
        setAndRefund(map.terrainGrid, cell, custom, map);
        Check(map.terrainGrid.FoundationAt(cell) == custom && ValidationHooks.Dropped.Count == 2 &&
            ValidationHooks.Dropped[^1].def == steel && ValidationHooks.Dropped[^1].stackCount == 7,
            "Heavy -> reinforced heavy did not refund exactly seven steel.");

        ResetCell(map, cell, water, wood);
        setAndRefund(map.terrainGrid, cell, heavy, map);
        Check(map.terrainGrid.FoundationAt(cell) == heavy && ValidationHooks.Dropped.Count == 3 &&
            ValidationHooks.Dropped[^1].def == woodMaterial && ValidationHooks.Dropped[^1].stackCount == 6 &&
            map.terrainGrid.UnderTerrainAt(index) == null,
            "Wood -> vanilla heavy completion or refund differs.");
        Console.WriteLine("PASS: real SetFoundation + GenLeaving refunds 6 wood/7 steel only after success; repeated completion adds nothing.");
    }

    private static void ResetCell(Map map, IntVec3 cell, TerrainDef top, TerrainDef foundation)
    {
        int index = map.cellIndices.CellToIndex(cell);
        map.terrainGrid.topGrid[index] = top;
        map.terrainGrid.foundationGrid[index] = foundation;
        UnderGrid(map.terrainGrid)[index] = null!;
        TempGrid(map.terrainGrid)[index] = null!;
        map.terrainGrid.colorGrid[index] = null;
        map.thingGrid.ThingsListAt(cell).Clear();
    }

    private static void ValidatePatchedIl(Assembly patch, Assembly replace, MethodInfo place, MethodInfo build, MethodInfo complete)
    {
        Type rules = patch.GetType("WhiteGiver.ReplaceStuffBridgeReplacement.BridgeReplacement", true)!;
        MethodInfo placement = rules.GetMethod("FoundationForPlacement", Fields)!;
        MethodInfo terrain = rules.GetMethod("TerrainForBuild", Fields)!;
        MethodInfo affordances = rules.GetMethod("AffordancesForBuild", Fields)!;
        MethodInfo refund = rules.GetMethod("CompleteFoundation", Fields)!;
        Check(PatchProcessor.GetCurrentInstructions(place).Count(instruction => instruction.Calls(placement)) == 2,
            "Both placement FoundationAt reads were not redirected.");
        List<CodeInstruction> buildIl = PatchProcessor.GetCurrentInstructions(build);
        Check(buildIl.Count(instruction => instruction.Calls(terrain)) == 1 &&
            buildIl.Count(instruction => instruction.Calls(affordances)) == 1 &&
            buildIl.Count(instruction => instruction.Calls(replace.GetType("Replace_Stuff.PlaceBridges.CanPlaceBlueprint", true)!
                .GetMethod("TerrainOrBridgesCanDo", Fields)!)) == 1,
            "Combined vanilla/Replace Stuff/bridge replacement IL is incomplete.");
        Check(PatchProcessor.GetCurrentInstructions(complete).Count(instruction => instruction.Calls(refund)) == 1,
            "Frame completion does not call the successful-set wrapper exactly once.");
    }

    private static void ValidateModIl(string modDll)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(modDll);
        MethodDefinition wrapper = assembly.MainModule.GetType("WhiteGiver.ReplaceStuffBridgeReplacement.BridgeReplacement")
            .Methods.Single(method => method.Name == "CompleteFoundation");
        var calls = wrapper.Body.Instructions.Where(instruction => instruction.Operand is MethodReference).ToList();
        int set = calls.FindIndex(instruction => ((MethodReference)instruction.Operand).Name == "SetFoundation");
        int refund = calls.FindIndex(instruction => ((MethodReference)instruction.Operand).Name == "DoLeavingsFor");
        Check(set >= 0 && refund > set && calls.Count(instruction => ((MethodReference)instruction.Operand).Name is
            "RemoveFoundation" or "Notify_FoundationDestroyed") == 0,
            "Completion wrapper uses an unsafe removal chain or refunds before setting the foundation.");
        Console.WriteLine("PASS: deployed IL has one SetFoundation -> later DoLeavingsFor chain and no removal/collapse call.");
    }

    private static TerrainDef[] UnderGrid(TerrainGrid grid) =>
        (TerrainDef[])typeof(TerrainGrid).GetField("underGrid", Fields)!.GetValue(grid)!;
    private static TerrainDef[] TempGrid(TerrainGrid grid) =>
        (TerrainDef[])typeof(TerrainGrid).GetField("tempGrid", Fields)!.GetValue(grid)!;
    private static void Add<T>(T def) where T : Def, new()
    {
        def.ResolveDefNameHash();
        DefDatabase<T>.Add(def);
    }
    private static void SetField(Type type, string name, object? target, object value) =>
        type.GetField(name, Fields)!.SetValue(target, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public static class ValidationHooks
{
    public static readonly List<Thing> Dropped = [];

    public static bool NoTranslation(string key, out TaggedString result, ref bool __result)
    {
        result = key;
        __result = true;
        return false;
    }

    public static bool NoBuildEdge(ref bool __result)
    {
        __result = false;
        return false;
    }

    public static bool Drop(Thing thing, IntVec3 cell, Map map, ThingPlaceMode mode, out Thing resultingThing,
        Action<Thing, int>? placedAction, Predicate<IntVec3>? validator, bool playDropSound)
    {
        Dropped.Add(thing);
        resultingThing = thing;
        return true;
    }
}

public sealed class ThrowPlaceWorker : PlaceWorker
{
    public const string Message = "Validation PlaceWorker failure";
    public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
        Thing? thingToIgnore = null, Thing? thing = null) => throw new InvalidOperationException(Message);
}
