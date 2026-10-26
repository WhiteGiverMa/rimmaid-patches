using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

public static class Driver
{
    internal const string Owner = "whitegiver.milian.weaponinteractions";
    internal static readonly BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static Assembly Milira = null!;
    internal static Assembly Ancot = null!;
    internal static Map Map = null!;
    internal static Faction Player = null!;
    internal static JobDef EquipDef = null!;
    internal static HediffDef Adaptive = null!;
    internal static HediffDef Sling = null!;
    internal static HashSet<string> Active = null!;
    private static int nextThingId = 10000;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(string mode, string patchPath, string gameRoot, string workshop, string qaRoot)
    {
        Assembly? patch = mode == "contract" ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
        if (patch != null) Check(!patch.GetReferencedAssemblies().Any(reference => reference.Name is "Milira" or "AncotLibrary" or "MilianModification" or "Fortified"),
            "Patch must not hard-reference upstream mod assemblies.");
        if (mode == "absent")
        {
            Activator.CreateInstance(patch!.GetType("WhiteGiver.MilianWeaponInteractions.MilianWeaponInteractionsMod", true)!, new object?[] { null });
            Check(!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true),
                "Missing Milira registered a patch.");
            Console.WriteLine("PASS: actual Mod constructor skips absent Milira without registering patches.");
            return;
        }

        string bound = Path.Combine(qaRoot, "bound");
        Ancot = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bound, "AncotLibrary.dll"));
        Milira = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bound, "Milira.dll"));
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bound, "MilianModification.dll"));
        Setup(workshop);
        ValidateUpstreamContract();
        if (mode == "run") Assembly.GetExecutingAssembly().GetType("PatchValidation", true)!.GetMethod("Run")!.Invoke(null, new object[] { patch! });
        Console.WriteLine("NOT TESTED: actual game window rendering, OS mouse hit-testing, map pathfinding/toil execution or in-game save/load.");
    }

    private static void Setup(string workshop)
    {
        Current.ProgramState = ProgramState.Entry;
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Current.Game = game;
        Set(typeof(Game), "maps", game, new List<Map>());
        Set(typeof(Game), "uniqueIDsManager", game, new UniqueIDsManager());
        Set(typeof(Game), "tickManager", game, new TickManager());
        Player = new Faction { def = new FactionDef { defName = "ValidationPlayer", isPlayer = true } };
        game.InitData = new GameInitData { playerFaction = Player };
        Set(typeof(Prefs), "data", null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        var language = (LoadedLanguage)RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage));
        Set(typeof(LoadedLanguage), "workerInt", language, new LanguageWorker_English());
        LanguageDatabase.activeLanguage = language;
        Active = new HashSet<string>(StringComparer.Ordinal)
        {
            "ancot.milirarace", "ancot.ancotlibrary", "ancot.milianmodification", "ludeon.rimworld.biotech"
        };
        Set(typeof(ModsConfig), "activeModsHashSet", null, Active);
        Set(typeof(ModsConfig), "biotechActive", null, true);

        var flesh = new FleshTypeDef { defName = "Mechanoid" };
        Add(flesh);
        Set(typeof(FleshTypeDefOf), "Mechanoid", null, flesh);
        var body = new BodyDef { defName = "Milian_Body" };
        Add(body);
        Adaptive = new HediffDef { defName = "MilianFitting_AdaptiveArmSystem", hediffClass = typeof(Hediff) };
        Add(Adaptive);
        Sling = new HediffDef { defName = "MilianFitting_TacticalSling", hediffClass = typeof(HediffWithComps) };
        Add(Sling);

        var content = (ModContentPack)RuntimeHelpers.GetUninitializedObject(typeof(ModContentPack));
        Set(typeof(ModContentPack), "rootDirInt", content, new DirectoryInfo(Path.Combine(Path.GetTempPath(), "opencode")));
        content.assemblies = new ModAssemblyHandler(content);
        content.assemblies.loadedAssemblies.Add(Milira);
        LoadedModManager.RunningModsListForReading.Add(content);
        GenTypes.ClearCache();

        var document = new XmlDocument();
        document.Load(Path.Combine(workshop, "3256974620", "1.6", "Mods", "Biotech", "Defs", "BiotechDefs", "JobDefs", "Jobs_Work.xml"));
        EquipDef = (JobDef)DirectXmlToObjectNew.DefFromNodeNew(document.SelectSingleNode("Defs/JobDef[defName='Milira_EquipMilian']")!, null);
        Add(EquipDef);
        Type defOf = Milira.GetType("Milira.MiliraDefOf", true)!;
        RuntimeHelpers.RunClassConstructor(defOf.TypeHandle);
        defOf.GetField("Milira_EquipMilian")!.SetValue(null, EquipDef);
        defOf.GetField("MilianFitting_AdaptiveArmSystem")!.SetValue(null, Adaptive);
        defOf.GetField("MilianFitting_TacticalSling")!.SetValue(null, Sling);

        Map = new Map();
        Map.info.Size = new IntVec3(20, 1, 20);
        Map.cellIndices = new CellIndices(Map);
        Map.listerThings = new ListerThings(ListerThingsUse.Global);
        Map.reservationManager = new ReservationManager(Map);
        Map.events = new MapEvents(Map);
        Map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
        Map.fogGrid = (FogGrid)RuntimeHelpers.GetUninitializedObject(typeof(FogGrid));
        game.Maps.Add(Map);
        game.currentMapIndex = 0;
        Check(EquipDef.driverClass == Milira.GetType("Milira.JobDriver_EquipMilian"), "Real XML equip driver differs.");
    }

    internal static Pawn NewMilian(bool ranged = true, string bodyName = "Milian_Body")
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.thingIDNumber = nextThingId++;
        pawn.def = new ThingDef
        {
            defName = "ValidationMilian", label = "validation milian", category = ThingCategory.Pawn,
            thingClass = typeof(Pawn), race = new RaceProperties
            {
                body = new BodyDef { defName = bodyName },
                intelligence = Intelligence.ToolUser
            }
        };
        Set(typeof(RaceProperties), "fleshType", pawn.def.race, FleshTypeDefOf.Mechanoid);
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.equipment = new Pawn_EquipmentTracker(pawn);
        pawn.jobs = new Pawn_JobTracker(pawn);
        pawn.carryTracker = new Pawn_CarryTracker(pawn);
        pawn.pather = new Pawn_PathFollower(pawn);
        pawn.mindState = new Pawn_MindState(pawn);
        Set(typeof(Thing), "factionInt", pawn, Player);
        Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)0);
        Set(typeof(Thing), "positionInt", pawn, new IntVec3(5, 0, 5));
        var properties = (CompProperties)Activator.CreateInstance(Milira.GetType("Milira.CompProperties_TargetableWeapon", true)!)!;
        properties.GetType().GetField("shouldBeRangeWeapon")!.SetValue(properties, ranged);
        var comp = (ThingComp)Activator.CreateInstance(Milira.GetType("Milira.CompTargetableWeapon", true)!)!;
        comp.parent = pawn;
        comp.props = properties;
        Set(typeof(ThingWithComps), "comps", pawn, new List<ThingComp>
        {
            comp, new CompOverseerSubject { parent = pawn, props = new CompProperties_OverseerSubject() }
        });
        return pawn;
    }

    internal static ThingWithComps NewWeapon(bool ranged = true)
    {
        var weapon = new ProbeWeapon
        {
            def = new ThingDef
            {
                defName = ranged ? "ValidationRanged" : "ValidationMelee", label = ranged ? "ranged weapon" : "melee weapon",
                category = ThingCategory.Item, thingClass = typeof(ThingWithComps), stackLimit = 1, useHitPoints = false,
                equipmentType = EquipmentType.Primary,
                tools = ranged ? new List<Tool>() : new List<Tool> { new() }
            },
            stackCount = 1
        };
        weapon.thingIDNumber = nextThingId++;
        Set(typeof(ThingDef), "verbs", weapon.def, ranged ? new List<VerbProperties> { new() } : new List<VerbProperties>());
        var equippable = new CompEquippable { parent = weapon, props = new CompProperties(typeof(CompEquippable)) };
        Set(typeof(VerbTracker), "verbs", equippable.verbTracker, new List<Verb>());
        Set(typeof(ThingWithComps), "comps", weapon, new List<ThingComp> { equippable });
        Set(typeof(Thing), "mapIndexOrState", weapon, (sbyte)0);
        Set(typeof(Thing), "positionInt", weapon, new IntVec3(7, 0, 5));
        Map.listerThings.Add(weapon);
        return weapon;
    }

    private static void ValidateUpstreamContract()
    {
        Pawn rangedPawn = NewMilian();
        Pawn meleePawn = NewMilian(false);
        ThingWithComps gun = NewWeapon();
        ThingWithComps sword = NewWeapon(false);
        MethodInfo available = Milira.GetType("Milira.CompTargetableWeapon", true)!.GetMethod("Available")!;
        bool Available(Pawn pawn, Thing? thing) => (bool)available.Invoke(pawn.AllComps[0], new object?[] { thing, pawn })!;
        Check(!Available(rangedPawn, null), "Upstream null weapon accepted.");
        Check(Available(rangedPawn, gun) && !Available(rangedPawn, sword), "Ranged-only upstream filter changed.");
        Check(Available(meleePawn, sword) && !Available(meleePawn, gun), "Melee-only upstream filter changed.");
        rangedPawn.health.hediffSet.hediffs.Add(new Hediff { def = Adaptive, pawn = rangedPawn });
        Check(Available(rangedPawn, sword), "Adaptive arm did not allow the opposite weapon type.");
        Active.Remove("ancot.milianmodification");
        Check(!Available(rangedPawn, sword), "Inactive fitting mod still bypassed class restriction.");
        Active.Add("ancot.milianmodification");
        rangedPawn.health.hediffSet.hediffs.Clear();

        Job job = JobMaker.MakeJob(EquipDef, rangedPawn, gun);
        job.count = 1;
        var driver = (JobDriver)Activator.CreateInstance(EquipDef.driverClass)!;
        driver.pawn = rangedPawn;
        driver.job = job;
        Check(ReferenceEquals(EquipDef.driverClass.GetProperty("Milian", All)!.GetValue(driver), rangedPawn), "TargetA is not Milian.");
        Check(ReferenceEquals(EquipDef.driverClass.GetProperty("Weapon", All)!.GetValue(driver), gun), "TargetB is not weapon.");
        Console.WriteLine("PASS: actual Milira Available ranged/melee/adaptive-arm boundaries and XML equip job TargetA/TargetB contract.");
        gun.DeSpawn();
        sword.DeSpawn();
    }

    internal static void Add<T>(T def) where T : Def, new()
    {
        def.ResolveDefNameHash();
        DefDatabase<T>.Add(def);
    }
    internal static void Set(Type type, string field, object? instance, object value) => AccessTools.Field(type, field).SetValue(instance, value);
    internal static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

public sealed class ProbeWeapon : ThingWithComps
{
    // QA presentation boundary: no global stat/quality database is loaded for labels.
    public override string LabelNoCount => def.label;

    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        Map.listerThings.Remove(this);
        Driver.Set(typeof(Thing), "mapIndexOrState", this, (sbyte)-1);
    }
}

public static class ValidationHooks
{
    public static bool Drop(Thing thing, IntVec3 cell, Map map, ThingPlaceMode mode, out Thing resultingThing,
        Action<Thing, int>? placedAction, Predicate<IntVec3>? validator, bool playDropSound)
    {
        Driver.Set(typeof(Thing), "mapIndexOrState", thing, (sbyte)0);
        Driver.Set(typeof(Thing), "positionInt", thing, cell);
        map.listerThings.Add(thing);
        resultingThing = thing;
        return true;
    }
}
