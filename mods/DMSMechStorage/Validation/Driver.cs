using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using Fortified;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using WhiteGiver.DMSMechStorage;

public static class Driver
{
    internal static Map Map = null!;
    internal static Faction Player = null!;
    internal static JobDef StoreJob = null!;
    internal static ThingDef MiniDef = null!;
    internal static readonly HashSet<IntVec3> Blocked = new();
    internal static readonly Dictionary<Thing, float> Costs = new();
    internal static readonly Dictionary<Thing, float> Bandwidth = new();
    internal static readonly Dictionary<PawnRenderer, Graphic> Graphics = new();
    internal static readonly List<string> Errors = new();
    internal static Job? Ordered;
    internal static bool Reachable = true;
    internal static bool Manipulation = true;
    internal static int RejectAccept;
    internal static int FailCapsuleSpawn;
    internal static int FailPawnSpawn;
    internal static bool ThrowPawnSetup;
    internal static int ForcedWait;
    internal static int NativeSealCalls;
    internal static bool LastPawnSpawnWasRestore;
    private static int nextId = 10000;
    private static readonly Dictionary<string, string> Translations = new();

    public static void Run(string workshop)
    {
        var qa = new Harmony("whitegiver.dms.mechstorage.validation");
        Hook(qa, typeof(Log), nameof(Log.Error), nameof(Error), new[] { typeof(string) });
        Hook(qa, typeof(Log), nameof(Log.Message), nameof(Skip), new[] { typeof(string) });
        Hook(qa, typeof(Log), nameof(Log.Warning), nameof(Skip), new[] { typeof(string) });
        Hook(qa, typeof(DefOfHelper), nameof(DefOfHelper.EnsureInitializedInCtor), nameof(Skip));
        Hook(qa, typeof(ModLister), nameof(ModLister.CheckBiotech), nameof(True));
        Hook(qa, typeof(ModLister), nameof(ModLister.CheckBiotechOrAnomalyOrOdyssey), nameof(True));
        Hook(qa, typeof(Translator), nameof(Translator.TryTranslate), nameof(Translate), new[] { typeof(string), typeof(TaggedString).MakeByRefType() });
        Hook(qa, typeof(ColoredText), nameof(ColoredText.Resolve), nameof(Plain), new[] { typeof(TaggedString) });
        Hook(qa, typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode), nameof(Skip));
        Hook(qa, typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.CapableOf), nameof(Capable));
        Hook(qa, typeof(SoundStarter), nameof(SoundStarter.PlayOneShotOnCamera), nameof(Skip));
        Hook(qa, typeof(ContentFinder<Texture2D>), "Get", nameof(NoTexture), new[] { typeof(string), typeof(bool) });
        Hook(qa, typeof(GenGrid), nameof(GenGrid.Walkable), nameof(Walkable), new[] { typeof(IntVec3), typeof(Map) });
        Hook(qa, typeof(RegionAndRoomQuery), nameof(RegionAndRoomQuery.RegionAt), nameof(NoRegion),
            new[] { typeof(IntVec3), typeof(Map), typeof(RegionType) });
        Hook(qa, typeof(FogGrid), nameof(FogGrid.IsFogged), nameof(False), new[] { typeof(IntVec3) });
        Hook(qa, typeof(ReachabilityUtility), nameof(ReachabilityUtility.CanReach), nameof(Reach),
            new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(Danger), typeof(bool), typeof(bool), typeof(TraverseMode) });
        Hook(qa, typeof(StatExtension), nameof(StatExtension.GetStatValue), nameof(Stat), new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) });
        Hook(qa, typeof(Pawn), nameof(Pawn.SpawnSetup), nameof(SpawnSetup));
        Hook(qa, typeof(Building), nameof(Building.SpawnSetup), nameof(SpawnSetup));
        Hook(qa, typeof(ThingWithComps), nameof(ThingWithComps.SpawnSetup), nameof(SpawnSetup));
        Hook(qa, typeof(Pawn), nameof(Pawn.DeSpawn), nameof(DeSpawn));
        Hook(qa, typeof(Building), nameof(Building.DeSpawn), nameof(DeSpawn));
        Hook(qa, typeof(Thing), nameof(Thing.DeSpawn), nameof(DeSpawn));
        Hook(qa, typeof(Building), nameof(Building.Destroy), nameof(Destroy));
        Hook(qa, typeof(Thing), nameof(Thing.Destroy), nameof(Destroy));
        Hook(qa, typeof(CompOverseer), nameof(CompOverseer.UpdateDummy), nameof(Skip));
        Hook(qa, typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents), nameof(Skip));
        qa.Patch(AccessTools.PropertyGetter(typeof(PawnRenderer), nameof(PawnRenderer.BodyGraphic)),
            prefix: new HarmonyMethod(typeof(Driver), nameof(BodyGraphic)));
        Hook(qa, typeof(ThingOwnerUtility), nameof(ThingOwnerUtility.ShouldAutoExtinguishInnerThings), nameof(False));
        Hook(qa, typeof(ThingOwnerUtility), nameof(ThingOwnerUtility.ShouldRemoveDesignationsOnAddedThings), nameof(False));
        Hook(qa, typeof(Pawn), nameof(Pawn.Notify_DisabledWorkTypesChanged), nameof(Skip));
        Hook(qa, typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentAdded), nameof(Skip));
        Hook(qa, typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved), nameof(Skip));
        Hook(qa, typeof(CompForbiddable), nameof(CompForbiddable.PostSpawnSetup), nameof(Skip));
        Hook(qa, typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob), nameof(Order), new[] { typeof(Job), typeof(JobTag?), typeof(bool) });
        Hook(qa, typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.CheckForJobOverride), nameof(Skip), new[] { typeof(float), typeof(bool) });
        Hook(qa, typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob), nameof(Skip));
        Hook(qa, typeof(PawnUtility), nameof(PawnUtility.ForceWait), nameof(ForceWait));
        Hook(qa, typeof(Messages), nameof(Messages.Message), nameof(Skip), new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) });
        Hook(qa, typeof(GenSpawn), nameof(GenSpawn.Spawn), nameof(SpawnFailure),
            new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) });
        Hook(qa, typeof(Building_MechCapsule), nameof(Building_MechCapsule.TryAcceptMech), nameof(AcceptFailure));
        qa.Patch(AccessTools.DeclaredMethod(typeof(MechCapsuleUtility), nameof(MechCapsuleUtility.DeactivateMech)),
            postfix: new HarmonyMethod(typeof(Driver), nameof(RecordNativeSeal)));
        Setup(workshop);
        new DMSMechStorageMod(null!);
        WaitingRegression.Run(qa);
        DraftAndPoseRegression.Run();
        FollowupRegression.Run(qa);
        WeaponInteractionRegression.Run();
        Scenarios.Run();
        Console.WriteLine("PASS: real sidecar menu/job, FFF capsule/container and native relation/bandwidth operations under explicit engine-boundary hooks.");
        Console.WriteLine("NOT TESTED: game GUI/art, real pathfinding/map registration, complete game save/load or third-party callback failures.");
    }

    private static void Setup(string workshop)
    {
        Current.ProgramState = ProgramState.Entry;
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Current.Game = game;
        Set(typeof(Game), "maps", game, new List<Map>());
        Set(typeof(Game), "uniqueIDsManager", game, new UniqueIDsManager());
        Set(typeof(Game), "tickManager", game, new TickManager());
        Set(typeof(Game), "components", game, new List<GameComponent>());
        Set(typeof(Game), "questManager", game, new QuestManager());
        Player = new Faction { def = new FactionDef { defName = "ValidationPlayer", isPlayer = true } };
        game.InitData = new GameInitData { playerFaction = Player };
        Set(typeof(Prefs), "data", null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        var language = (LoadedLanguage)RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage));
        Set(typeof(LoadedLanguage), "workerInt", language, new LanguageWorker_English());
        LanguageDatabase.activeLanguage = language;
        var root = (Root_Play)RuntimeHelpers.GetUninitializedObject(typeof(Root_Play));
        var ui = (UIRoot_Play)RuntimeHelpers.GetUninitializedObject(typeof(UIRoot_Play));
        ui.mapUI = (MapInterface)RuntimeHelpers.GetUninitializedObject(typeof(MapInterface));
        ui.mapUI.selector = new Selector();
        root.uiRoot = ui;
        Set(typeof(Current), "rootInt", null, root);
        Set(typeof(ModsConfig), "activeModsHashSet", null, new HashSet<string> { "ludeon.rimworld.biotech", "aoba.framework", "aoba.deadmanswitch.core" });
        Set(typeof(ModsConfig), "biotechActive", null, true);
        Seed(typeof(FleshTypeDefOf), nameof(FleshTypeDefOf.Mechanoid), new FleshTypeDef { defName = "Mechanoid" });
        Seed(typeof(HediffDefOf), nameof(HediffDefOf.MechlinkImplant), new HediffDef { defName = "MechlinkImplant", hediffClass = typeof(Hediff) });
        Seed(typeof(PawnRelationDefOf), nameof(PawnRelationDefOf.Overseer), new PawnRelationDef
        {
            defName = "Overseer", reflexive = true, workerClass = typeof(PawnRelationWorker_Overseer)
        });
        Seed(typeof(StatDefOf), nameof(StatDefOf.BandwidthCost), new StatDef { defName = "BandwidthCost" });
        Seed(typeof(StatDefOf), nameof(StatDefOf.MechBandwidth), new StatDef { defName = "MechBandwidth" });
        Seed(typeof(StatDefOf), nameof(StatDefOf.MechControlGroups), new StatDef { defName = "MechControlGroups" });
        Seed(typeof(PawnCapacityDefOf), nameof(PawnCapacityDefOf.Manipulation), new PawnCapacityDef { defName = "Manipulation" });
        Seed(typeof(MechWorkModeDefOf), nameof(MechWorkModeDefOf.Work), new MechWorkModeDef { defName = "Work" });
        Add(new DesignationDef { defName = "ValidationDesignation" });
        Add(new WorkTypeDef { defName = "ValidationWork" });
        foreach (string name in new[] { "Wait_Combat", "AttackMelee", "AttackStatic", "PredatorHunt", "ManTurret" })
            Seed(typeof(JobDefOf), name, new JobDef { defName = name, driverClass = typeof(JobDriver_Wait) });
        MiniDef = new ThingDef { defName = "MinifiedThing", label = "minified", category = ThingCategory.Item, thingClass = typeof(MinifiedThing), stackLimit = 1 };
        Add(MiniDef);
        var content = Content("whitegiver.dms.mechstorage");
        content.assemblies = new ModAssemblyHandler(content);
        content.assemblies.loadedAssemblies.Add(typeof(DMSMechStorageMod).Assembly);
        LoadedModManager.RunningModsListForReading.Add(content);
        GenTypes.ClearCache();
        var xml = new XmlDocument();
        xml.Load(Path.Combine(workshop, "3498575851", "1.6", "Defs", "Base_MechCapsule.xml"));
        foreach (XmlElement node in xml.SelectNodes("Defs/ThingDef[@ParentName='FFF_MechCapsuleBase']")!)
        {
            string[] size = node["size"]!.InnerText.Trim('(', ')').Split(',');
            Add(new ThingDef
            {
                defName = node["defName"]!.InnerText, label = node["label"]!.InnerText,
                category = ThingCategory.Building, thingClass = typeof(Building_MechCapsule),
                size = new IntVec2(int.Parse(size[0]), int.Parse(size[1])),
                building = new BuildingProperties { isInert = true },
                minifiedDef = MiniDef, passability = Traversability.PassThroughOnly,
                rotatable = true, fillPercent = 0.4f, useHitPoints = false
            });
        }
        string modRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        xml.Load(Path.Combine(modRoot, "Defs", "Jobs.xml"));
        StoreJob = (JobDef)DirectXmlToObjectNew.DefFromNodeNew(xml.SelectSingleNode("Defs/JobDef")!, null);
        Add(StoreJob);
        xml.Load(Path.Combine(modRoot, "Languages", "ChineseSimplified", "Keyed", "MechStorage.xml"));
        foreach (XmlElement node in xml.DocumentElement!.ChildNodes) Translations.Add(node.Name, node.InnerText);
        Map = new Map();
        Map.info.Size = new IntVec3(24, 1, 24);
        Map.cellIndices = new CellIndices(Map);
        Map.thingGrid = new ThingGrid(Map);
        Map.edificeGrid = new EdificeGrid(Map);
        Map.terrainGrid = new TerrainGrid(Map);
        Array.Fill(Map.terrainGrid.topGrid, new TerrainDef { defName = "ValidationGround", passability = Traversability.Standable, affordances = new List<TerrainAffordanceDef>() });
        Map.listerThings = new ListerThings(ListerThingsUse.Global);
        Map.spawnedThings = new ThingOwner<Thing>(Map, false, LookMode.Reference);
        Map.listerBuildings = new ListerBuildings();
        Map.reservationManager = new ReservationManager(Map);
        Map.events = new MapEvents(Map);
        Map.lordManager = new LordManager(Map);
        Map.designationManager = new DesignationManager(Map);
        Map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
        Map.fogGrid = (FogGrid)RuntimeHelpers.GetUninitializedObject(typeof(FogGrid));
        game.Maps.Add(Map);
        game.currentMapIndex = 0;
    }

    internal static Pawn NewPawn(string package = "aoba.deadmanswitch.core", bool mechanitor = false, Type? type = null, bool subject = true,
        IntVec3? at = null)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(type ?? typeof(Pawn));
        pawn.thingIDNumber = nextId++;
        pawn.stackCount = 1;
        pawn.def = new ThingDef
        {
            defName = "ValidationMech" + pawn.thingIDNumber, label = "test mech", category = ThingCategory.Pawn,
            thingClass = type ?? typeof(Pawn), modContentPack = Content(package),
            race = new RaceProperties { intelligence = mechanitor ? Intelligence.Humanlike : Intelligence.ToolUser, baseBodySize = 1f }
        };
        if (!mechanitor) Set(typeof(RaceProperties), "fleshType", pawn.def.race, FleshTypeDefOf.Mechanoid);
        pawn.kindDef = new PawnKindDef { defName = "ValidationKind" + pawn.thingIDNumber, label = "test mech", race = pawn.def };
        pawn.Name = new NameSingle("测试机兵" + pawn.thingIDNumber);
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.relations = new Pawn_RelationsTracker(pawn);
        pawn.jobs = new Pawn_JobTracker(pawn);
        pawn.pather = new Pawn_PathFollower(pawn);
        pawn.mindState = new Pawn_MindState(pawn);
        pawn.inventory = new Pawn_InventoryTracker(pawn);
        pawn.equipment = new Pawn_EquipmentTracker(pawn);
        pawn.carryTracker = new Pawn_CarryTracker(pawn);
        pawn.drafter = new Pawn_DraftController(pawn);
        pawn.stances = new Pawn_StanceTracker(pawn);
        var drawer = (Pawn_DrawTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_DrawTracker));
        drawer.renderer = (PawnRenderer)RuntimeHelpers.GetUninitializedObject(typeof(PawnRenderer));
        Set(typeof(PawnRenderer), "pawn", drawer.renderer, pawn);
        var wigglerField = AccessTools.Field(typeof(PawnRenderer), "wiggler");
        object wiggler = RuntimeHelpers.GetUninitializedObject(wigglerField.FieldType);
        Set(wigglerField.FieldType, "downedAngle", wiggler, 25f);
        wigglerField.SetValue(drawer.renderer, wiggler);
        Graphics[drawer.renderer] = new Graphic_Single { drawSize = new Vector2(2, 2) };
        Set(typeof(Pawn), "drawer", pawn, drawer);
        Set(typeof(Thing), "factionInt", pawn, Player);
        Set(typeof(Thing), "positionInt", pawn, at ?? new IntVec3(10, 0, 10));
        Set(typeof(Thing), "rotationInt", pawn, Rot4.North);
        var comps = new List<ThingComp>();
        Set(typeof(ThingWithComps), "comps", pawn, comps);
        if (!mechanitor && subject) comps.Add(new CompOverseerSubject { parent = pawn, props = new CompProperties_OverseerSubject() });
        if (mechanitor)
        {
            pawn.health.hediffSet.hediffs.Add(new Hediff { def = HediffDefOf.MechlinkImplant, pawn = pawn });
            pawn.mechanitor = new Pawn_MechanitorTracker(pawn);
            pawn.mechanitor.controlGroups.Add(new MechanitorControlGroup(pawn.mechanitor));
            Bandwidth[pawn] = 10;
        }
        Costs[pawn] = 1;
        Register(pawn, Map);
        return pawn;
    }

    internal static void Register(Thing thing, Map map)
    {
        Set(typeof(Thing), "mapIndexOrState", thing, (sbyte)0);
        map.spawnedThings.TryAdd(thing, false);
        map.thingGrid.Register(thing);
        map.listerThings.Add(thing);
        if (thing is Building building && thing.def.IsEdifice()) map.edificeGrid.Register(building);
    }

    internal static void ResetMap()
    {
        foreach (Thing thing in Map.listerThings.AllThings.ToArray())
            if (thing.Spawned) thing.DeSpawn();
        var left = Map.listerThings.AllThings.Where(t => t.Spawned).Select(t => t.LabelShort).ToArray();
        if (left.Length > 0) throw new InvalidOperationException("QA reset left spawned things: " + string.Join(",", left));
        // Rebuild the spatial indexes so a fixture hook cannot leak stale cells into the next case.
        Map.thingGrid = new ThingGrid(Map);
        Map.edificeGrid = new EdificeGrid(Map);
        Map.spawnedThings = new ThingOwner<Thing>(Map, false, LookMode.Reference);
        Map.reservationManager = new ReservationManager(Map);
        Blocked.Clear();
        Errors.Clear();
        Reachable = true;
        Manipulation = true;
        RejectAccept = FailCapsuleSpawn = FailPawnSpawn = ForcedWait = NativeSealCalls = 0;
        ThrowPawnSetup = false;
        Ordered = null;
    }

    internal static ModContentPack Content(string package)
    {
        var content = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        Set(typeof(ModContentPack), "packageIdPlayerFacingInt", content, package);
        Set(typeof(ModContentPack), "packageIdInt", content, package.ToLowerInvariant());
        Set(typeof(ModContentPack), "rootDirInt", content, new DirectoryInfo(AppContext.BaseDirectory));
        return content;
    }

    internal static void Add<T>(T def) where T : Def, new() { def.ResolveDefNameHash(); DefDatabase<T>.Add(def); }
    private static void Seed<T>(Type type, string field, T def) where T : Def, new() { Add(def); Set(type, field, null, def); }
    internal static void Set(Type type, string field, object? instance, object value) => AccessTools.Field(type, field).SetValue(instance, value);
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Hook(Harmony qa, Type type, string name, string replacement, Type[]? arguments = null) =>
        qa.Patch(AccessTools.DeclaredMethod(type, name, arguments), prefix: new HarmonyMethod(typeof(Driver), replacement));

    public static bool Skip() => false;
    public static bool NoTexture(ref Texture2D __result) { __result = null!; return false; }
    public static bool True(ref bool __result) { __result = true; return false; }
    public static bool False(ref bool __result) { __result = false; return false; }
    public static bool Error(string text) { Errors.Add(text); return false; }
    public static bool Translate(string key, out TaggedString result, ref bool __result)
    {
        result = Translations.GetValueOrDefault(key, key); __result = true; return false;
    }
    public static bool Plain(TaggedString taggedStr, ref string __result) { __result = taggedStr.RawText; return false; }
    public static bool Walkable(IntVec3 c, ref bool __result) { __result = !Blocked.Contains(c); return false; }
    public static bool NoRegion(ref Region? __result) { __result = null; return false; }
    public static bool Reach(ref bool __result) { __result = Reachable; return false; }
    public static bool Capable(ref bool __result) { __result = Manipulation; return false; }
    public static bool BodyGraphic(PawnRenderer __instance, ref Graphic __result) { __result = Graphics[__instance]; return false; }
    public static bool Stat(Thing thing, StatDef stat, ref float __result)
    {
        __result = stat == StatDefOf.BandwidthCost ? Costs.GetValueOrDefault(thing, 1f)
            : stat == StatDefOf.MechBandwidth ? Bandwidth.GetValueOrDefault(thing, 10f) : 2f;
        return false;
    }
    public static bool SpawnSetup(Thing __instance, Map map, bool respawningAfterLoad)
    {
        if (__instance is Pawn)
        {
            LastPawnSpawnWasRestore = respawningAfterLoad;
            if (ThrowPawnSetup) { ThrowPawnSetup = false; throw new InvalidOperationException("QA pawn SpawnSetup failure after ownership removal."); }
        }
        Register(__instance, map);
        if (__instance is ThingWithComps withComps)
            foreach (ThingComp comp in withComps.AllComps) comp.PostSpawnSetup(respawningAfterLoad);
        return false;
    }
    public static bool DeSpawn(Thing __instance, DestroyMode mode)
    {
        Map map = __instance.Map;
        if (__instance.Spawned)
        {
            map.spawnedThings.Remove(__instance);
            map.thingGrid.Deregister(__instance);
            map.listerThings.Remove(__instance);
            if (__instance is Building building && __instance.def.IsEdifice()) map.edificeGrid.DeRegister(building);
            Set(typeof(Thing), "mapIndexOrState", __instance, (sbyte)-1);
            if (__instance is ThingWithComps withComps)
                foreach (ThingComp comp in withComps.AllComps) comp.PostDeSpawn(map, mode);
        }
        return false;
    }
    public static bool Destroy(Thing __instance, DestroyMode mode)
    {
        if (__instance.Spawned) __instance.DeSpawn(mode);
        Set(typeof(Thing), "mapIndexOrState", __instance, (sbyte)-2);
        return false;
    }
    public static bool SpawnFailure(Thing newThing, ref Thing? __result)
    {
        if (newThing is Building_MechCapsule && FailCapsuleSpawn > 0) FailCapsuleSpawn--;
        else if (newThing is Pawn && FailPawnSpawn > 0) FailPawnSpawn--;
        else return true;
        __result = null;
        return false;
    }
    public static bool AcceptFailure(ref bool __result)
    {
        if (RejectAccept == 0) return true;
        RejectAccept--; __result = false; return false;
    }
    public static bool Order(Job job, ref bool __result) { Ordered = job; __result = true; return false; }
    public static bool ForceWait(Pawn pawn, int ticks) { ForcedWait = ticks; return false; }
    public static void RecordNativeSeal() => NativeSealCalls++;
}
