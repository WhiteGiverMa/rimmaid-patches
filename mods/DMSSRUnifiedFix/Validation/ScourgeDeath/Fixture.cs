using WhiteGiver.DMSSRUnifiedFix;
using DMSSRUnifiedFix.Validation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using DMSSR.Extensions;
using DMSSR.Hediffs;
using DMSSR.Misc;
using Fortified;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace DMSSRUnifiedFix.Validation.ScourgeDeath;

internal sealed class Fixture
{
    internal readonly Map Map;
    internal readonly ThingDef ScourgeDef;
    internal readonly ThingDef OtherMechDef;
    internal readonly HediffDef Born;
    internal readonly HediffDef Injury;
    internal readonly HediffDef Implant;
    internal readonly DamageDef Bullet;
    internal readonly MechUndyingTracker Tracker;
    internal readonly List<Corpse> Corpses = new();
    internal readonly Faction PlayerFaction;
    private int nextId = 100;

    internal Fixture()
    {
        Set(typeof(DefOfHelper), "bindingNow", null, true);
        try
        {
            foreach (Type type in new[] { typeof(ThingDefOf), typeof(HediffDefOf), typeof(DMS_HediffDefOf), typeof(PawnCapacityDefOf), typeof(BodyPartTagDefOf), typeof(FleshTypeDefOf), typeof(StatDefOf), typeof(DesignationDefOf) })
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);

            foreach (string name in new[] { "Consciousness", "Moving", "Manipulation" })
            {
                PawnCapacityDef capacity = new() { defName = name, workerClass = typeof(PawnCapacityWorker) };
                DefDatabase<PawnCapacityDef>.Add(capacity);
                typeof(PawnCapacityDefOf).GetField(name)!.SetValue(null, capacity);
            }
            DesignationDefOf.Hunt = new DesignationDef { defName = "Hunt" };
            DefDatabase<DesignationDef>.Add(DesignationDefOf.Hunt);
            foreach ((string name, float value) in new[] { ("PainShockThreshold", 0.8f), ("HealthScaleFactor", 1f), ("LethalDamageThreshold", 150f) })
            {
                StatDef stat = new() { defName = name, defaultBaseValue = value, workerClass = typeof(StatWorker) };
                DefDatabase<StatDef>.Add(stat);
                typeof(StatDefOf).GetField(name)?.SetValue(null, stat);
            }

            FleshTypeDef flesh = new() { defName = "Mechanoid", isOrganic = false };
            FleshTypeDefOf.Mechanoid = flesh;
            BodyDef body = new() { defName = "DMS_Synthroid" };
            body.corePart = new BodyPartRecord
            {
                body = body, coverage = 1f,
                def = new BodyPartDef { defName = "Torso", label = "torso", hitPoints = 40, tags = new() },
                parts = new(),
            };
            body.ResolveReferences();

            ScourgeDef = PawnDef("DMS_Mech_Scourge", body, flesh);
            XElement scourgeXml = XDocument.Load(Path.Combine(Program.WorkshopRoot, @"3835512735\1.7\Defs\Things_race\Races_Scourge.xml"))
                .Root!.Elements("ThingDef").Single(def => (string?)def.Element("defName") == "DMS_Mech_Scourge");
            Tests.Check((string?)scourgeXml.Element("thingClass") == typeof(HumanlikeMech).FullName, "installed Scourge uses HumanlikeMech");
            XElement initial = scourgeXml.Element("modExtensions")!.Elements("li")
                .Single(extension => (string?)extension.Attribute("Class") == typeof(DefModExt_InitialDeathCheat).FullName);
            Tests.Check((bool)initial.Element("enabled")! && (int)initial.Element("charges")! == 1, "installed Scourge starts with one death-cheat charge");
            ScourgeDef.modExtensions = new() { new DefModExt_InitialDeathCheat { enabled = (bool)initial.Element("enabled")!, charges = (int)initial.Element("charges")! } };
            ScourgeDef.comps = new() { new CompProperties_DeathCheatInit() };
            OtherMechDef = PawnDef("Validation_OtherHumanlikeMech", body, flesh);
            XElement bornXml = XDocument.Load(Path.Combine(Program.WorkshopRoot, @"3835512735\1.7\Defs\Hediffs\Hediffs_Undying.xml"))
                .Root!.Elements("HediffDef").Single(def => (string?)def.Element("defName") == "DMSSR_BornFromDeath");
            Tests.Check((string?)bornXml.Element("hediffClass") == nameof(HediffWithComps), "installed BornFromDeath uses standard HediffWithComps");
            Born = Add(new HediffDef
            {
                defName = "DMSSR_BornFromDeath", hediffClass = typeof(HediffWithComps), isBad = false,
                stages = bornXml.Element("stages")!.Elements("li").Select(stage => new HediffStage
                {
                    statOffsets = StatModifiers(stage.Element("statOffsets")),
                    statFactors = StatModifiers(stage.Element("statFactors")),
                    capMods = stage.Element("capMods")!.Elements("li").Select(modifier => new PawnCapacityModifier
                    {
                        capacity = DefDatabase<PawnCapacityDef>.GetNamed((string)modifier.Element("capacity")!),
                        offset = (float)modifier.Element("offset")!,
                    }).ToList(),
                }).ToList(),
            });
            Injury = Add(new HediffDef { defName = "Gunshot", hediffClass = typeof(Hediff_Injury), injuryProps = new InjuryProps() });
            ThingDef implantItem = new() { defName = "Validation_ImplantItem", label = "implant", thingClass = typeof(ThingWithComps), category = ThingCategory.Item, stackLimit = 1, useHitPoints = false };
            Implant = Add(new HediffDef { defName = "Validation_SpawnOnRemoved", hediffClass = typeof(RemovalProbe), spawnThingOnRemoved = implantItem });
            Bullet = new DamageDef { defName = "Bullet", workerClass = typeof(DamageWorker_AddInjury), hediff = Injury };
            Set(typeof(DamageDef), "externalViolence", Bullet, true);
            DMS_HediffDefOf.DMS_DeathCheat = Add(new HediffDef
            {
                defName = "DMS_DeathCheat", hediffClass = typeof(HediffWithComps), isBad = false,
                comps = new() { new HediffCompProperties_DeathCheatCharges() },
            });
            DMS_HediffDefOf.DMS_DeathCheat_Cheap = Add(new HediffDef { defName = "DMS_DeathCheat_Cheap", hediffClass = typeof(HediffWithComps) });
            DMS_HediffDefOf.DMS_OppressiveRestructuring = Add(new HediffDef
            {
                defName = "DMS_OppressiveRestructuring", hediffClass = typeof(HediffWithComps),
                comps = new() { new HediffCompProperties_OppressiveTimer() },
            });
            DMS_HediffDefOf.DMS_Schizophrenia_CasterMark = Add(new HediffDef { defName = "DMS_Schizophrenia_CasterMark", hediffClass = typeof(HediffWithComps) });
            HediffDefOf.ToxicBuildup = Add(new HediffDef { defName = "ToxicBuildup" });
            HediffDefOf.Scaria = Add(new HediffDef { defName = "Scaria" });

            Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            Set(typeof(Game), "maps", game, new List<Map>());
            game.tickManager = new TickManager();
            game.uniqueIDsManager = new UniqueIDsManager();
            game.questManager = new QuestManager();
            game.storyWatcher = new StoryWatcher();
            game.components = new() { new GameComponent_Bossgroup(game) };
            World world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World));
            world.factionManager = new FactionManager();
            world.worldPawns = new WorldPawns();
            world.ideoManager = new IdeoManager();
            PlayerFaction = new Faction { def = new FactionDef { defName = "PlayerColony", isPlayer = true } };
            Set(typeof(FactionManager), "ofPlayer", world.factionManager, PlayerFaction);
            Set(typeof(FactionManager), "allFactions", world.factionManager, new List<Faction> { PlayerFaction });
            game.World = world;
            Current.Game = game;
            Current.ProgramState = ProgramState.Playing;

            Map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
            Map.uniqueID = 1;
            Map.info = new MapInfo { Size = new IntVec3(16, 1, 16), isPocketMap = true };
            game.Maps.Add(Map);
            Map.cellIndices = new CellIndices(16, 16);
            Map.events = new MapEvents(Map);
            Map.spawnedThings = new ThingOwner<Thing>(Map);
            Map.thingGrid = new ThingGrid(Map);
            Map.listerThings = new ListerThings(ListerThingsUse.Global);
            Map.edificeGrid = new EdificeGrid(Map);
            Map.regionGrid = new RegionGrid(Map);
            Map.regionDirtyer = new RegionDirtyer(Map);
            Map.regionAndRoomUpdater = new RegionAndRoomUpdater(Map) { Enabled = false };
            Set(typeof(RegionAndRoomUpdater), "initialized", Map.regionAndRoomUpdater, true);
            PathingContext context = new(Map, new PathGrid(Map, new PathGridDef { defName = "ValidationNormal" }));
            Map.pathing = (Pathing)RuntimeHelpers.GetUninitializedObject(typeof(Pathing));
            Set(typeof(Pathing), "normal", Map.pathing, context);
            Set(typeof(Pathing), "fenceBlocked", Map.pathing, context);
            Map.mapPawns = new MapPawns(Map);
            Map.designationManager = new DesignationManager(Map);
            Map.lordManager = new Verse.AI.Group.LordManager(Map);
            Map.components = new();
            Tracker = new MechUndyingTracker(Map);
            Map.components.Add(Tracker);
            Probes.CurrentFixture = this;
            Set(typeof(UnityData), "mainThreadId", null, Environment.CurrentManagedThreadId);
        }
        finally { Set(typeof(DefOfHelper), "bindingNow", null, false); }
    }

    private static HediffDef Add(HediffDef def)
    {
        DefDatabase<HediffDef>.Add(def);
        return def;
    }

    private static List<StatModifier> StatModifiers(XElement? xml) => xml?.Elements().Select(element =>
    {
        string name = element.Name.LocalName;
        StatDef? stat = DefDatabase<StatDef>.GetNamedSilentFail(name);
        if (stat == null)
        {
            stat = new StatDef { defName = name, workerClass = typeof(StatWorker), defaultBaseValue = 1f };
            DefDatabase<StatDef>.Add(stat);
        }
        return new StatModifier { stat = stat, value = (float)element };
    }).ToList() ?? new();

    private static ThingDef PawnDef(string name, BodyDef body, FleshTypeDef flesh)
    {
        RaceProperties race = new()
        {
            body = body, intelligence = Intelligence.ToolUser, baseHealthScale = 1.5f, baseBodySize = 1.1f,
            lifeStageAges = new() { new LifeStageAge { def = new LifeStageDef { defName = "MechanoidFullyFormed", healthScaleFactor = 1f, bodySizeFactor = 1f } } },
        };
        Set(typeof(RaceProperties), "fleshType", race, flesh);
        race.corpseDef = new ThingDef
        {
            defName = "Corpse_" + name, label = "corpse", thingClass = typeof(Corpse),
            category = ThingCategory.Item, stackLimit = 1, passability = Traversability.Standable,
            comps = new(), useHitPoints = false,
        };
        return new ThingDef { defName = name, label = name, thingClass = typeof(HumanlikeMech), category = ThingCategory.Pawn, race = race, comps = new(), statBases = new() };
    }

    internal HumanlikeMech NewMech(bool scourge = true)
    {
        HumanlikeMech pawn = new() { def = scourge ? ScourgeDef : OtherMechDef, thingIDNumber = nextId++ };
        pawn.kindDef = new PawnKindDef { defName = pawn.def.defName, race = pawn.def };
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        Set(typeof(Pawn_AgeTracker), "cachedLifeStageIndex", pawn.ageTracker, 0);
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.mindState = new Verse.AI.Pawn_MindState(pawn);
        pawn.needs = new Pawn_NeedsTracker(pawn);
        pawn.InitializeComps();
        Register(pawn, new IntVec3(8, 0, 8));
        return pawn;
    }

    internal void SetPlayer(Pawn pawn) => Set(typeof(Thing), "factionInt", pawn, PlayerFaction);

    internal void SetTick(int tick) => Set(typeof(TickManager), "ticksGameInt", Current.Game.tickManager, tick);

    internal void ClearMap()
    {
        foreach (Thing thing in Map.listerThings.AllThings.ToArray()) Unregister(thing);
        Corpses.Clear();
    }

    internal void AddFatalInjuryWithoutCheck(Pawn pawn)
    {
        Hediff injury = HediffMaker.MakeHediff(Injury, pawn, pawn.RaceProps.body.corePart);
        injury.Severity = 100f;
        pawn.health.hediffSet.AddDirect(injury);
        Tests.Check(pawn.health.ShouldBeDead() && !pawn.Dead, "fixture is fatally injured but not yet killed");
    }

    internal void Register(Thing thing, IntVec3 cell)
    {
        Set(typeof(Thing), "positionInt", thing, cell);
        Set(typeof(Thing), "mapIndexOrState", thing, (sbyte)0);
        Map.spawnedThings.TryAdd(thing, canMergeWithExistingStacks: false);
        Map.thingGrid.Register(thing);
        Map.listerThings.Add(thing);
    }

    internal void Unregister(Thing thing)
    {
        if (thing.Spawned)
        {
            Map.spawnedThings.Remove(thing);
            Map.thingGrid.Deregister(thing);
            Map.listerThings.Remove(thing);
        }
        Set(typeof(Thing), "mapIndexOrState", thing, (sbyte)-1);
    }

    internal void LethalBullet(Pawn pawn)
    {
        Hediff_Injury injury = (Hediff_Injury)HediffMaker.MakeHediff(Injury, pawn, pawn.RaceProps.body.corePart);
        injury.Severity = 100f;
        DamageInfo damage = new(Bullet, 100f);
        damage.SetIgnoreInstantKillProtection(true);
        new BulletImpact { def = Bullet }.ApplyInjury(pawn, injury, damage);
    }

    internal static void Set(Type type, string name, object? target, object? value) =>
        (type.GetField(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, name)).SetValue(target, value);
}

internal sealed class BulletImpact : DamageWorker_AddInjury
{
    // Begin at the post-armour bullet impact seam; the inherited injury finalization is real game IL.
    internal void ApplyInjury(Pawn pawn, Hediff_Injury injury, DamageInfo damage) =>
        FinalizeAndAddInjury(pawn, injury, damage, new DamageResult());
}

public sealed class RemovalProbe : Hediff
{
    internal static int PreCount;
    internal static int PostCount;
    internal static bool Throw;
    public override void PreRemoved() { PreCount++; base.PreRemoved(); }
    public override void PostRemoved()
    {
        PostCount++;
        if (Throw) throw new InvalidOperationException("Expected removal failure");
        base.PostRemoved();
    }
}
