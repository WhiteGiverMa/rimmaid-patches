using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;
using Verse.AI;
using WhiteGiver.ReinforcedConcreteHeavyBridge;

internal static class Program
{
    private static readonly string HarmonyDirectory = Environment.GetEnvironmentVariable("HARMONY_DIRECTORY")
        ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    private static readonly string ReplaceStuffAssembly = Environment.GetEnvironmentVariable("REPLACE_STUFF_ASSEMBLY")
        ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\3526354009\1.7\Assemblies\Replace_Stuff.dll";

    private static int Main(string[] args)
    {
        if (args is ["--help"])
        {
            Console.WriteLine("Usage: Validation bind|run|absent GAME_ROOT MOD_ROOT MOD_DLL QA_ROOT");
            Console.WriteLine("Uses real game assemblies. QA_ROOT must be disposable, outside game/save/config folders.");
            return 0;
        }
        if (args.Length != 5 || args[0] is not ("bind" or "run" or "absent"))
        {
            Console.Error.WriteLine("Expected a mode and four paths; use --help.");
            return 1;
        }
        string gameRoot = Path.GetFullPath(args[1]);
        string modRoot = Path.GetFullPath(args[2]);
        string modDll = Path.GetFullPath(args[3]);
        string qaRoot = Path.GetFullPath(args[4]);
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
            if (args[0] == "bind") Bind(gameRoot, bound);
            else Assembly.GetExecutingAssembly().GetType("Driver")!.GetMethod("Run")!.Invoke(null, [gameRoot, modRoot, modDll, qaRoot, args[0] == "run"]);
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
        foreach (string name in new[] { "Verse.ModsConfig", "Verse.BaseContent", "RimWorld.Frame" })
        {
            MethodDefinition initializer = game.MainModule.GetType(name).Methods.Single(m => m.Name == ".cctor");
            initializer.Body.Instructions.Clear();
            initializer.Body.ExceptionHandlers.Clear();
            initializer.Body.Variables.Clear();
            initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
        }
        // 仅替代地图预约/UI清理与物品落地外壳，真实 Destroy/Cancel、返料计算和容器转移保留。
        MethodDefinition cleanup = game.MainModule.GetType("Verse.Thing").Methods.Single(m => m.Name == "RemoveAllReservationsAndDesignationsOnThis");
        cleanup.Body.Instructions.Clear();
        cleanup.Body.GetILProcessor().Emit(OpCodes.Ret);
        MethodDefinition drop = game.MainModule.GetType("Verse.GenDrop").Methods.Single(m => m.Name == "TryDropSpawn");
        var validation = new AssemblyNameReference("Validation", new Version(1, 0, 0, 0));
        game.MainModule.AssemblyReferences.Add(validation);
        var hook = new MethodReference("Drop", game.MainModule.TypeSystem.Boolean,
            new TypeReference("", "ValidationHooks", game.MainModule, validation));
        foreach (ParameterDefinition parameter in drop.Parameters)
            hook.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
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
        Console.WriteLine("PASS: QA-only copy isolates config/rendering, map cleanup and drop placement; conversion, cancellation, refunds and Scribe unchanged.");
    }
}

internal static class Driver
{
    private const string BridgeName = "WhiteGiver_ReinforcedConcreteHeavyBridge";
    private static readonly BindingFlags Fields = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(string gameRoot, string modRoot, string modDll, string qaRoot, bool includeReplaceStuff)
    {
        Assembly? replace = includeReplaceStuff
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(qaRoot, "bound", "Replace_Stuff.dll")) : null;
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(modDll);
        string sandbox = Path.Combine(qaRoot, "save-data");
        Directory.CreateDirectory(Path.Combine(sandbox, "Config"));
        SetField(typeof(GenFilePaths), "saveDataPath", null, sandbox);
        SetField(typeof(Prefs), "data", null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        var root = (Root_Play)RuntimeHelpers.GetUninitializedObject(typeof(Root_Play));
        var ui = (UIRoot_Play)RuntimeHelpers.GetUninitializedObject(typeof(UIRoot_Play));
        ui.mapUI = (MapInterface)RuntimeHelpers.GetUninitializedObject(typeof(MapInterface));
        ui.mapUI.selector = (Selector)RuntimeHelpers.GetUninitializedObject(typeof(Selector));
        SetField(typeof(Selector), "selected", ui.mapUI.selector, new List<object>());
        root.uiRoot = ui;
        SetField(typeof(Current), "rootInt", null, root);
        // 仅延迟贴图/图标加载；真实继承、新版 XML 解析和交叉引用保留。
        Type queuedEvent = typeof(LongEventHandler).GetNestedType("QueuedLongEvent", BindingFlags.NonPublic)!;
        SetField(typeof(LongEventHandler), "currentEvent", null, Activator.CreateInstance(queuedEvent)!);
        var content = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        SetField(typeof(ModContentPack), "rootDirInt", content, new DirectoryInfo(modRoot));
        SetField(typeof(ModContentPack), "nameInt", content, "Reinforced Concrete Heavy Bridge");
        content.assemblies = new ModAssemblyHandler(content);
        content.assemblies.loadedAssemblies.Add(patch);

        TerrainDef bridge = ParseBridge(gameRoot, modRoot);
        LoadedModManager.RunningModsListForReading.Add(content);
        Check(patch.GetReferencedAssemblies().Any(a => a.Name == "0Harmony" && a.Version == new Version(3, 0, 0, 0)),
            "Expected Harmony 3 reference missing.");
        Check(bridge.isFoundation && bridge.bridge && bridge.layerable && bridge.autoRebuildable && bridge.preventCraters,
            "Foundation/bridge behavior differs from vanilla.");
        Check(bridge.destroyOnBombDamageThreshold == -1 && bridge.affordances.Select(d => d.defName).SequenceEqual(
            new[] { "Light", "Medium", "Heavy", "Walkable" }), "Blast immunity or heavy support differs.");
        Check(bridge.statBases.Single(s => s.stat.defName == "WorkToBuild").value == 2500,
            "Material stuff modifiers changed WorkToBuild.");
        Check(bridge.costList.Count == 1 && bridge.costList[0].thingDef.defName == "FT_ReinforcedConcreteBag" &&
            bridge.costList[0].count == 14 && !bridge.MadeFromStuff, "Cost is not exactly 14 reinforced concrete.");
        Check(bridge.researchPrerequisites.Select(d => d.defName).SequenceEqual(new[] { "HeavyBridges", "FT_Concrete" }),
            "Research requirements differ.");
        Check(bridge.tags.SequenceEqual(new[] { "Floor" }) && !bridge.tags.Contains("Substructure"),
            "Bridge acquired a gravship-substructure tag instead of retaining vanilla behavior.");

        Type generator = typeof(ThingDefGenerator_Buildings);
        var blueprint = (ThingDef)generator.GetMethod("NewBlueprintDef_Terrain", Fields)!.Invoke(null, [bridge, false])!;
        var frame = (ThingDef)generator.GetMethod("NewFrameDef_Terrain", Fields)!.Invoke(null, [bridge, false])!;
        Check(blueprint.defName == "Blueprint_" + BridgeName && blueprint.entityDefToBuild == bridge &&
            blueprint.thingClass == typeof(Blueprint_Build) && frame.defName == "Frame_" + BridgeName &&
            frame.entityDefToBuild == bridge && frame.thingClass == typeof(Frame), "Generated construction defs differ.");
        Add(blueprint);
        Add(frame);

        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Current.Game = game;
        game.Scenario = new Scenario();
        game.playSettings = new PlaySettings();
        game.researchManager = new ResearchManager();
        game.storyteller = (Storyteller)RuntimeHelpers.GetUninitializedObject(typeof(Storyteller));
        game.storyteller.difficulty = new Difficulty();
        var progress = (Dictionary<ResearchProjectDef, float>)GetField(typeof(ResearchManager), "progress", game.researchManager)!;
        foreach ((bool heavy, bool concrete) in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            progress[bridge.researchPrerequisites[0]] = heavy ? 800 : 0;
            progress[bridge.researchPrerequisites[1]] = concrete ? 250 : 0;
            Check(bridge.IsResearchFinished == (heavy && concrete), "Research gate must require BOTH technologies.");
        }
        Console.WriteLine("PASS: real XML inheritance/parser/cross-references; vanilla properties/textures, exact cost, four research states.");

        ValidateSettings(content, bridge, sandbox, patch);
        AutoBridgeValidation.Run(patch, replace, game, bridge);
        ValidateConversion(game, bridge, sandbox, patch);
        ValidatePawnCollection(game, patch);
        Check(GenFilePaths.SaveDataFolderPath == sandbox, "Save-data isolation was lost.");
        Console.WriteLine("NOT TESTED: rendered settings UI, full gameplay construction/rendering, or loading a complete save without the mod.");
    }

    private static TerrainDef ParseBridge(string gameRoot, string modRoot)
    {
        foreach (string name in new[] { "Light", "Medium", "Heavy", "Walkable", "Bridgeable" })
            Add(new TerrainAffordanceDef { defName = name });
        Add(new DesignationCategoryDef { defName = "Structure" });
        Add(new DrawStyleCategoryDef { defName = "Foundations" });
        Add(new KeyBindingDef { defName = "Misc4" });
        foreach (string name in new[] { "Bridge_Collapse", "Bridge_CollapseWater", "ConstructMetal" })
            Add(new EffecterDef { defName = name });
        StatDefOf.WorkToBuild = new StatDef { defName = "WorkToBuild", defaultBaseValue = 0 };
        StatDefOf.WorkToMake = new StatDef { defName = "WorkToMake", defaultBaseValue = 0 };
        StatDefOf.MarketValue = new StatDef { defName = "MarketValue", workerClass = typeof(StatWorker_MarketValue) };
        Add(StatDefOf.WorkToBuild);
        Add(StatDefOf.WorkToMake);
        Add(StatDefOf.MarketValue);
        Add(new ThingDef { defName = "Steel" });
        Add(new ThingDef { defName = "FT_ReinforcedConcreteBag", stackLimit = 75, category = ThingCategory.Item });
        DefDatabase<ThingDef>.GetNamed("Steel").BaseMarketValue = 1.9f;
        DefDatabase<ThingDef>.GetNamed("FT_ReinforcedConcreteBag").BaseMarketValue = 1.25f;
        Add(new ResearchProjectDef { defName = "HeavyBridges", baseCost = 800 });
        Add(new ResearchProjectDef { defName = "FT_Concrete", baseCost = 250 });

        var core = new XmlDocument();
        core.Load(Path.Combine(gameRoot, "Data/Core/Defs/TerrainDefs/Terrain_Foundation.xml"));
        var odyssey = new XmlDocument();
        odyssey.Load(Path.Combine(gameRoot, "Data/Odyssey/Defs/TerrainDefs/Terrain_Foundation.xml"));
        var custom = new XmlDocument();
        custom.Load(Path.Combine(modRoot, "Defs/TerrainDefs/Bridge.xml"));
        XmlNode parent = core.SelectSingleNode("Defs/TerrainDef[@Name='BridgeBase']")!;
        XmlNode vanilla = odyssey.SelectSingleNode("Defs/TerrainDef[defName='HeavyBridge']")!;
        XmlNode child = custom.DocumentElement!.FirstChild!;
        XmlInheritance.Clear();
        foreach (XmlNode node in new[] { parent, vanilla, child }) XmlInheritance.TryRegister(node, null);
        XmlInheritance.Resolve();
        XmlNode expected = XmlInheritance.GetResolvedNodeFor(vanilla);
        XmlNode actual = XmlInheritance.GetResolvedNodeFor(child);
        string[] changed = ["defName", "label", "description", "costList", "researchPrerequisites"];
        foreach (XmlNode field in expected.ChildNodes)
        {
            if (field.NodeType != XmlNodeType.Element || changed.Contains(field.Name)) continue;
            Check(actual[field.Name]?.OuterXml == field.OuterXml, "Inherited property changed: " + field.Name);
        }
        TerrainDef original = (TerrainDef)DirectXmlToObjectNew.DefFromNodeNew(vanilla, null);
        TerrainDef bridge = (TerrainDef)DirectXmlToObjectNew.DefFromNodeNew(child, null);
        Add(original);
        Add(bridge);
        DirectXmlCrossRefLoader.ResolveAllWantedCrossReferences(FailMode.LogErrors);
        DirectXmlCrossRefLoader.Clear();
        XmlInheritance.Clear();
        Check(bridge.defName == BridgeName && bridge.texturePath == original.texturePath &&
            bridge.bridgePropsPath == original.bridgePropsPath && bridge.spaceBridgePropsPath == original.spaceBridgePropsPath &&
            bridge.uiIconPath == original.uiIconPath && bridge.color == original.color, "Vanilla art references differ.");
        return bridge;
    }

    private static void ValidateSettings(ModContentPack content, TerrainDef bridge, string sandbox, Assembly patch)
    {
        Type modType = patch.GetType("WhiteGiver.ReinforcedConcreteHeavyBridge.BridgeMod", true)!;
        string filename = Path.Combine(sandbox, "Config", $"Mod_{content.FolderName}_{modType.Name}.xml");
        File.WriteAllText(filename, "<SettingsBlock><ModSettings /></SettingsBlock>");
        var mod = (Mod)Activator.CreateInstance(modType, content)!;
        BridgeSettings settings = mod.GetSettings<BridgeSettings>();
        Check(settings.MaterialCost == 14, "First-run cost differs from 14.");
        Check(!settings.AutoUseReinforcedConcrete, "First-run automatic bridge must retain the vanilla selection.");
        RuntimeHelpers.PrepareMethod(modType.GetMethod(nameof(Mod.DoSettingsWindowContents))!.MethodHandle);
        Type costType = patch.GetType("WhiteGiver.ReinforcedConcreteHeavyBridge.BridgeCost", true)!;
        MethodInfo apply = costType.GetMethod("Apply", Fields)!;
        List<ThingDefCountClass> cached = bridge.CostListAdjusted(null);
        WealthWatcher.ResetStaticData();
        float before = ((float[])GetField(typeof(WealthWatcher), "cachedTerrainMarketValue", null)!)[bridge.index];
        settings.MaterialCost = 37;
        settings.AutoUseReinforcedConcrete = true;
        apply.Invoke(null, [settings]);
        Check(bridge.costList[0].count == 37 && bridge.CostListAdjusted(null).Single().count == 37 &&
            !ReferenceEquals(cached, bridge.CostListAdjusted(null)), "Cost update/cache reset failed.");
        float after = ((float[])GetField(typeof(WealthWatcher), "cachedTerrainMarketValue", null)!)[bridge.index];
        Check(Math.Abs(after - bridge.GetStatValueAbstract(StatDefOf.MarketValue)) < 0.0001f &&
            Math.Abs(after - before - 23 * 1.25f) < 0.0001f, "Terrain wealth cache did not follow the changed material cost.");
        mod.WriteSettings();
        Check(File.ReadAllText(filename).Contains("<materialCost>37</materialCost>", StringComparison.Ordinal), "Cost not saved.");
        Check(File.ReadAllText(filename).Contains("<autoUseReinforcedConcrete>True</autoUseReinforcedConcrete>", StringComparison.OrdinalIgnoreCase),
            "Automatic bridge selection not saved.");
        BridgeSettings restored = ((Mod)Activator.CreateInstance(modType, content)!).GetSettings<BridgeSettings>();
        Check(restored.MaterialCost == 37 && restored.AutoUseReinforcedConcrete, "Cost/automatic bridge setting did not round-trip.");
        restored.AutoUseReinforcedConcrete = false;
        restored.Mod.WriteSettings();
        Check(!((Mod)Activator.CreateInstance(modType, content)!).GetSettings<BridgeSettings>().AutoUseReinforcedConcrete,
            "Vanilla automatic bridge selection did not round-trip.");
        foreach ((string text, int expected) in new[] { ("<materialCost>0</materialCost>", 1),
            ("<materialCost>10001</materialCost>", 10000), ("", 14) })
        {
            File.WriteAllText(filename, "<SettingsBlock><ModSettings>" + text + "</ModSettings></SettingsBlock>");
            BridgeSettings loaded = ((Mod)Activator.CreateInstance(modType, content)!).GetSettings<BridgeSettings>();
            Check(loaded.MaterialCost == expected && !loaded.AutoUseReinforcedConcrete,
                "Persisted setting default/clamp differs.");
        }
        apply.Invoke(null, [new BridgeSettings()]);
        Check(bridge.costList[0].count == 14, "Default cost was not restored.");
        Console.WriteLine("PASS: actual settings/Scribe cost and both automatic bridge choices round-trip, old settings default to vanilla, live cost/wealth caches.");
    }

    private static void Add<T>(T def) where T : Def, new()
    {
        def.ResolveDefNameHash();
        DefDatabase<T>.Add(def);
    }

    private static Map NewMap(TerrainDef water)
    {
        var map = new Map();
        map.info.Size = new IntVec3(4, 1, 4);
        map.cellIndices = new CellIndices(map);
        map.terrainGrid = new TerrainGrid(map);
        Array.Fill(map.terrainGrid.topGrid, water);
        map.listerThings = new ListerThings(ListerThingsUse.Global);
        map.events = new MapEvents(map);
        map.lordManager = new Verse.AI.Group.LordManager(map);
        map.mapDrawer = new MapDrawer(map);
        map.pathing = new Pathing(map);
        map.listerBuildings = new ListerBuildings();
        map.designationManager = (DesignationManager)RuntimeHelpers.GetUninitializedObject(typeof(DesignationManager));
        SetField(typeof(DesignationManager), "thingDesignations", map.designationManager, new Dictionary<Thing, List<Designation>>());
        map.regionGrid = (RegionGrid)RuntimeHelpers.GetUninitializedObject(typeof(RegionGrid));
        SetField(typeof(RegionGrid), "map", map.regionGrid, map);
        SetField(typeof(RegionGrid), "regionGrid", map.regionGrid, new Region[map.cellIndices.NumGridCells]);
        var section = (Section)RuntimeHelpers.GetUninitializedObject(typeof(Section));
        SetField(typeof(MapDrawer), "sections", map.mapDrawer, new[,] { { section } });
        return map;
    }

    private static void ValidateConversion(Game game, TerrainDef custom, string sandbox, Assembly patch)
    {
        TerrainDef vanilla = DefDatabase<TerrainDef>.GetNamed("HeavyBridge");
        var water = new TerrainDef { defName = "ValidationWater" };
        var floor = new TerrainDef { defName = "ValidationFloor", layerable = true };
        var temporary = new TerrainDef { defName = "ValidationTemporary", temporary = true };
        foreach (TerrainDef terrain in new[] { water, floor, temporary }) Add(terrain);
        Add(new ColorDef { defName = "ValidationPaint" });
        var terrainFlag = new MapMeshFlagDef { defName = "Terrain" };
        terrainFlag.PostSetIndices();
        MapMeshFlagDefOf.Terrain = terrainFlag;
        TerrainDefOf.Soil = water;
        ushort hash = 1;
        foreach (TerrainDef terrain in DefDatabase<TerrainDef>.AllDefs) terrain.shortHash = hash++;
        DefDatabase<ColorDef>.AllDefsListForReading[0].shortHash = 100;

        Map first = NewMap(water);
        Map second = NewMap(water);
        SetField(typeof(Game), "maps", game, new List<Map> { first, second });
        first.terrainGrid.foundationGrid[0] = custom;
        first.terrainGrid.foundationGrid[1] = custom;
        first.terrainGrid.foundationGrid[2] = vanilla;
        second.terrainGrid.foundationGrid[3] = custom;
        first.terrainGrid.topGrid[1] = floor;
        ((TerrainDef[])GetField(typeof(TerrainGrid), "underGrid", first.terrainGrid)!)[1] = water;
        ((TerrainDef[])GetField(typeof(TerrainGrid), "tempGrid", first.terrainGrid)!)[0] = temporary;
        first.terrainGrid.colorGrid[0] = DefDatabase<ColorDef>.AllDefsListForReading[0];
        first.terrainGrid.colorGrid[1] = DefDatabase<ColorDef>.AllDefsListForReading[0];
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var district = (District)RuntimeHelpers.GetUninitializedObject(typeof(District));
        SetField(typeof(District), "roomInt", district, room);
        var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
        SetField(typeof(Region), "districtInt", region, district);
        first.regionGrid.SetRegionAt(new IntVec3(0, 0, 0), region);
        int terrainEvents = 0, pathEvents = 0;
        foreach (Map map in new[] { first, second })
        {
            map.events.TerrainChanged += _ => terrainEvents++;
            map.events.PathCostRecalculate += _ => pathEvents++;
        }

        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.meleeVerbs = new Pawn_MeleeVerbs(pawn) { lastTerrainBasedVerbUseTick = 1234 };
        Pawn_MeleeVerbs_TerrainSource source = Pawn_MeleeVerbs_TerrainSource.Create(pawn.meleeVerbs, custom);
        SetField(typeof(Pawn_MeleeVerbs), "terrainVerbs", pawn.meleeVerbs, source);
        VerbTracker tracker = source.tracker;
        var unrelatedPawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        unrelatedPawn.meleeVerbs = new Pawn_MeleeVerbs(unrelatedPawn);
        var unrelatedSource = Pawn_MeleeVerbs_TerrainSource.Create(unrelatedPawn.meleeVerbs, floor);
        SetField(typeof(Pawn_MeleeVerbs), "terrainVerbs", unrelatedPawn.meleeVerbs, unrelatedSource);
        Type conversionType = patch.GetType("WhiteGiver.ReinforcedConcreteHeavyBridge.BridgeConversion", true)!;
        MethodInfo convert = conversionType.GetMethod("Convert", Fields)!;
        Current.ProgramState = ProgramState.Playing;
        var result = ((int Bridges, int Sites, int Pawns))convert.Invoke(null, [new[] { first, second }, new[] { pawn, pawn, unrelatedPawn }])!;
        Check(result == (3, 0, 1), "Multi-map conversion counts differ.");
        Check(first.terrainGrid.FoundationAt(0) == vanilla && first.terrainGrid.FoundationAt(1) == vanilla &&
            second.terrainGrid.FoundationAt(3) == vanilla && first.terrainGrid.foundationGrid[2] == vanilla, "Foundation conversion missed a bridge.");
        Check(first.terrainGrid.TerrainAt(0) == temporary && first.terrainGrid.TopTerrainAt(0) == water &&
            first.terrainGrid.UnderTerrainAt(0) == null && first.terrainGrid.TerrainAt(1) == floor &&
            first.terrainGrid.UnderTerrainAt(1) == water && first.terrainGrid.ColorAt(1) != null,
            "Conversion altered floor/natural/temporary/paint layers.");
        Check(terrainEvents == 3 && pathEvents == 3 && (bool)GetField(typeof(Room), "statsAndRoleDirty", room)!,
            "Terrain/path/room refresh missed an affected cell.");
        Check(source.def == vanilla && ReferenceEquals(source.tracker, tracker) &&
            pawn.meleeVerbs.lastTerrainBasedVerbUseTick == 1234 && unrelatedSource.def == floor,
            "Stored pawn terrain conversion altered tracker, cooldown or unrelated terrain.");
        Check(((Section[,])GetField(typeof(MapDrawer), "sections", first.mapDrawer)!)[0, 0].dirtyFlags != 0,
            "Terrain section was not dirtied.");
        var repeated = ((int Bridges, int Sites, int Pawns))convert.Invoke(null, [new[] { first, second }, new[] { pawn }])!;
        Check(repeated == (0, 0, 0) && terrainEvents == 3, "Repeated conversion is not idempotent.");
        source.def = custom;
        var pawnOnly = ((int Bridges, int Sites, int Pawns))convert.Invoke(null, [new[] { first, second }, new[] { pawn }])!;
        Check(pawnOnly == (0, 0, 1), "Pawn-only leftover was skipped when no bridges remained.");
        first.terrainGrid.foundationGrid[2] = custom;
        second.terrainGrid.topGrid[5] = custom;
        try
        {
            convert.Invoke(null, [new[] { first, second }, Array.Empty<Pawn>()]);
            throw new InvalidOperationException("Unexpected non-foundation bridge did not block conversion.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { }
        Check(first.terrainGrid.foundationGrid[2] == custom, "Preflight failure partially converted an earlier map.");
        first.terrainGrid.foundationGrid[2] = vanilla;
        second.terrainGrid.topGrid[5] = water;
        ValidateSites(first, second, custom, vanilla, convert);
        RoundTripTerrain(first, custom, sandbox);
        RoundTripPawnSource(pawn, source, custom, sandbox);
        Console.WriteLine("PASS: real two-map conversion, floor/water/temp/paint preservation, mesh/path/room/events, pawn references/cooldowns, idempotence and preflight.");
    }

    private static void ValidateSites(Map first, Map second, TerrainDef custom, TerrainDef vanilla, MethodInfo convert)
    {
        var blueprint = new ProbeBlueprint { def = custom.blueprintDef };
        var frame = new ProbeFrame { def = custom.frameDef, workDone = 1250 };
        var unrelated = new ProbeBlueprint { def = new ThingDef { entityDefToBuild = vanilla } };
        PlaceSite(blueprint, first, 0);
        PlaceSite(frame, second, 1);
        PlaceSite(unrelated, first, 0);
        ThingDef material = custom.costList[0].thingDef;
        var delivered = new Thing { def = material, stackCount = 9 };
        Check(frame.resourceContainer.TryAdd(delivered), "Frame fixture could not receive materials.");
        var result = ((int Bridges, int Sites, int Pawns))convert.Invoke(null, [new[] { first, second }, Array.Empty<Pawn>()])!;
        Check(result == (0, 2, 0) && blueprint.Destroyed && frame.Destroyed && !unrelated.Destroyed,
            "Construction-site cleanup touched wrong entities or missed a target.");
        Check(frame.resourceContainer.Count == 0 && !delivered.Destroyed && delivered.stackCount == 9 &&
            second.listerThings.AllThings.Contains(delivered) && ValidationHooks.Dropped.Single() == delivered,
            "Real frame cancellation did not refund exactly the delivered materials.");
        Console.WriteLine("PASS: actual Blueprint/Frame Destroy(Cancel), snapshot-safe removal, nine delivered materials refunded, unrelated plan preserved; map drop placement isolated.");
    }

    private static void PlaceSite(Thing thing, Map map, sbyte mapIndex)
    {
        SetField(typeof(Thing), "mapIndexOrState", thing, mapIndex);
        SetField(typeof(Thing), "positionInt", thing, new IntVec3(2, 0, 2));
        map.listerThings.AllThings.Add(thing);
    }

    private sealed class ProbeBlueprint : Blueprint_Build
    {
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish) => RemoveSite(this);
    }

    private sealed class ProbeFrame : Frame
    {
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish) => RemoveSite(this);
    }

    private static void RemoveSite(Thing thing)
    {
        thing.Map.listerThings.AllThings.Remove(thing);
        SetField(typeof(Thing), "mapIndexOrState", thing, (sbyte)-1);
    }

    private static void RoundTripPawnSource(Pawn pawn, Pawn_MeleeVerbs_TerrainSource source, TerrainDef custom, string sandbox)
    {
        string filename = Path.Combine(sandbox, "pawn-terrain.xml");
        Scribe.saver.InitSaving(filename, "PawnTerrainFixture");
        try { pawn.meleeVerbs.ExposeData(); }
        finally { Scribe.saver.FinalizeSaving(); }
        Check(!File.ReadAllText(filename).Contains(BridgeName, StringComparison.Ordinal), "Pawn source still saves the custom terrain.");
        Remove(custom);
        try
        {
            var restored = new Pawn_MeleeVerbs(pawn);
            Scribe.loader.InitLoading(filename);
            try { restored.ExposeData(); }
            finally { Scribe.loader.FinalizeLoading(); }
            var loadedSource = (Pawn_MeleeVerbs_TerrainSource)GetField(typeof(Pawn_MeleeVerbs), "terrainVerbs", restored)!;
            Check(loadedSource.def == source.def && loadedSource.parent == restored && loadedSource.tracker != null &&
                restored.lastTerrainBasedVerbUseTick == 1234, "Pawn terrain source/cooldown did not round-trip without custom def.");
        }
        finally { Add(custom); }
        Console.WriteLine("PASS: actual Pawn_MeleeVerbs/TerrainSource Scribe round-trip without custom terrain, with cooldown and tracker preserved.");
    }

    private static void RoundTripTerrain(Map map, TerrainDef custom, string sandbox)
    {
        TerrainGrid original = map.terrainGrid;
        string filename = Path.Combine(sandbox, "terrain-grid.xml");
        Scribe.saver.InitSaving(filename, "TerrainFixture");
        try { original.ExposeData(); }
        finally { Scribe.saver.FinalizeSaving(); }
        Remove(custom);
        Remove(custom.blueprintDef);
        Remove(custom.frameDef);
        try
        {
            var restored = new TerrainGrid(map);
            Scribe.loader.InitLoading(filename);
            try { restored.ExposeData(); }
            finally { Scribe.loader.FinalizeLoading(); }
            Check(restored.topGrid.SequenceEqual(original.topGrid) &&
                restored.foundationGrid.SequenceEqual(original.foundationGrid), "Terrain grid changed while reloading without custom def.");
            for (int i = 0; i < map.cellIndices.NumGridCells; i++)
            {
                Check(restored.UnderTerrainAt(i) == original.UnderTerrainAt(i) &&
                    restored.TempTerrainAt(i) == original.TempTerrainAt(i) &&
                    restored.ColorAt(i) == original.ColorAt(i), "Underlying/temporary/paint terrain changed after serialization.");
            }
        }
        finally
        {
            Add(custom);
            Add(custom.blueprintDef);
            Add(custom.frameDef);
        }
        Console.WriteLine("PASS: real TerrainGrid.ExposeData/Scribe save-and-load with custom terrain absent from short-hash registry.");
    }

    private static void ValidatePawnCollection(Game game, Assembly patch)
    {
        SetField(typeof(UnityData), "mainThreadId", null, Environment.CurrentManagedThreadId);
        var world = new RimWorld.Planet.World
        {
            worldPawns = new RimWorld.Planet.WorldPawns(),
            worldObjects = new RimWorld.Planet.WorldObjectsHolder()
        };
        SetField(typeof(Game), "worldInt", game, world);
        game.transportShipManager = new TransportShipManager();
        foreach (Map map in game.Maps)
        {
            map.mapPawns = new MapPawns(map);
            map.passingShipManager = new PassingShipManager(map);
            map.spawnedThings = new ThingOwner<Thing>(map);
        }
        var materialDef = new ThingDef { defName = "ValidationHolder", category = ThingCategory.Item, thingClass = typeof(ProbeHolder) };
        var container = new ProbeHolder { def = materialDef };
        var corpse = new ProbeHolder { def = materialDef };
        Pawn dead = ProbePawn(true);
        ((ThingOwner<Thing>)corpse.GetDirectlyHeldThings()).InnerListForReading.Add(dead);
        ((ThingOwner<Thing>)container.GetDirectlyHeldThings()).InnerListForReading.Add(corpse);
        game.Maps[0].spawnedThings.TryAdd(container);
        game.Maps[0].listerThings.Add(container);
        Pawn worldPawn = ProbePawn(false);
        world.worldPawns.AllPawnsDead.Add(worldPawn);
        Pawn shuttlePawn = ProbePawn(false);
        var shuttle = new ProbeHolder { def = materialDef };
        ((ThingOwner<Thing>)shuttle.GetDirectlyHeldThings()).InnerListForReading.Add(shuttlePawn);
        game.transportShipManager.AllTransportShips.Add(new TransportShip { shipThing = shuttle });
        Type conversion = patch.GetType("WhiteGiver.ReinforcedConcreteHeavyBridge.BridgeConversion", true)!;
        var pawns = (HashSet<Pawn>)conversion.GetMethod("CollectPawns", Fields)!.Invoke(null, [game.Maps.ToArray()])!;
        Check(pawns.Contains(dead) && pawns.Contains(worldPawn) && pawns.Contains(shuttlePawn),
            $"Collector coverage differs: nested dead={pawns.Contains(dead)}, world={pawns.Contains(worldPawn)}, transport={pawns.Contains(shuttlePawn)}.");
        Console.WriteLine("PASS: actual PawnsFinder plus recursive collector covers dead nested map contents, world pawns and unspawned transport-ship contents.");
    }

    private static Pawn ProbePawn(bool dead)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = new ThingDef { defName = "ValidationPawn", category = ThingCategory.Pawn, race = new RaceProperties() };
        pawn.health = new Pawn_HealthTracker(pawn);
        SetField(typeof(Pawn_HealthTracker), "healthState", pawn.health, dead ? PawnHealthState.Dead : PawnHealthState.Mobile);
        return pawn;
    }

    private sealed class ProbeHolder : Thing, IThingHolder
    {
        private readonly ThingOwner<Thing> contents;
        public ProbeHolder() => contents = new ThingOwner<Thing>(this);
        public ThingOwner GetDirectlyHeldThings() => contents;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, contents);
    }

    private static object? GetField(Type type, string name, object? target) => type.GetField(name, Fields)!.GetValue(target);
    private static void Remove<T>(T def) where T : Def =>
        typeof(DefDatabase<T>).GetMethod("Remove", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [def]);
    private static void SetField(Type type, string name, object? target, object value) => type.GetField(name, Fields)!.SetValue(target, value);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public static class ValidationHooks
{
    public static readonly List<Thing> Dropped = [];

    public static bool Drop(Thing thing, IntVec3 cell, Map map, ThingPlaceMode mode, out Thing resultingThing,
        Action<Thing, int>? placedAction, Predicate<IntVec3>? validator, bool playDropSound)
    {
        Dropped.Add(thing);
        map.listerThings.AllThings.Add(thing);
        resultingThing = thing;
        return true;
    }
}
