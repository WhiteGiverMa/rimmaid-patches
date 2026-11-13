using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using MiliraTheOneEntwinedWithYou.Data;
using MiliraTheOneEntwinedWithYou.GameComponents;
using MiliraTheOneEntwinedWithYou.Runtime;
using WhiteGiver.MTOEWYStatPerfFix;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace MTOEWYStatPerfFix.Validation;

/// <summary>
/// Offline RimWorld 1.7 fixture: a real <see cref="Game"/>/<see cref="Map"/> graph, real
/// <see cref="StatDef"/>s registered in the DefDatabase, a real MTOEWY
/// <see cref="GameComponent_IdealTracker"/> (uninitialized shell with only the fields the
/// stat path reads), and real Pawn/health/age/story/trait/equipment/apparel objects.
/// No game loop and no XML loading.
/// </summary>
internal sealed class Fixture
{
    internal const int Now = 100_000;
    internal const int Iterations = 100_000;

    internal static readonly string[] AffectedStatNames =
    {
        StatDispatch.GlobalLearningFactor,
        StatDispatch.SocialImpact,
        StatDispatch.ImmunityGainSpeed,
        StatDispatch.RestFallRateFactor,
        StatDispatch.GeneralLaborSpeed,
        StatDispatch.ResearchSpeed,
        StatDispatch.MedicalTendQuality,
        StatDispatch.CarryingCapacity,
        StatDispatch.MentalBreakThreshold,
        StatDispatch.MoveSpeed,
        StatDispatch.ConstructionSpeed,
        StatDispatch.JoyFallRateFactor,
        StatDispatch.AimingDelayFactor,
        StatDispatch.MeleeDodgeChance,
    };

    internal static readonly string[] UnrelatedStatNames =
    {
        "ShootingAccuracyPawn",
        "WorkSpeedGlobal",
    };

    internal Game Game = null!;
    internal Map Map = null!;
    internal Faction PlayerFaction = null!;
    internal Faction EnemyFaction = null!;
    internal LifeStageDef LifeStage = null!;
    internal GameComponent_IdealTracker Tracker = null!;
    internal IdealTrackerState State = null!;
    internal readonly Dictionary<string, StatDef> Stats = new(StringComparer.Ordinal);
    internal readonly List<Pawn> AllPawns = new();

    internal Pawn Ideal = null!;
    internal Pawn Plain = null!;
    internal Pawn Animal = null!;
    internal Pawn Mech = null!;
    internal Pawn Enemy = null!;

    private int nextId = 5_000;

    /// <summary>Real games carry dozens of GameComponents; keep the linear scan realistic.</summary>
    internal const int DummyComponentCount = 24;

    private sealed class DummyGameComponent : GameComponent
    {
    }

    internal IEnumerable<StatDef> AllStats
    {
        get
        {
            foreach (string name in AffectedStatNames)
            {
                yield return Stats[name];
            }

            foreach (string name in UnrelatedStatNames)
            {
                yield return Stats[name];
            }
        }
    }

    internal static Fixture Create(string saveDataPath)
    {
        TestHooks.Install(saveDataPath);
        Fixture fixture = new();
        fixture.CreateWorld();
        fixture.CreateStats();
        fixture.CreateTracker();
        fixture.CreatePawns();
        fixture.Reset();
        return fixture;
    }

    private void CreateWorld()
    {
        Current.ProgramState = ProgramState.Entry;
        Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Set(typeof(Game), "maps", Game, new List<Map>());
        Set(typeof(Game), "components", Game, new List<GameComponent>());
        Set(typeof(Game), "uniqueIDsManager", Game, new UniqueIDsManager());

        TickManager tickManager = new();
        Set(typeof(TickManager), "ticksGameInt", tickManager, Now);
        Set(typeof(Game), "tickManager", Game, tickManager);
        Current.Game = Game;

        PlayerFaction = new Faction { def = new FactionDef { defName = "FixturePlayer", isPlayer = true } };
        EnemyFaction = new Faction { def = new FactionDef { defName = "FixtureEnemy", permanentEnemy = true } };

        World world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World));
        FactionManager factions = new();
        Set(typeof(FactionManager), "allFactions", factions, new List<Faction> { PlayerFaction, EnemyFaction });
        Set(typeof(FactionManager), "ofPlayer", factions, PlayerFaction);
        Set(typeof(World), "factionManager", world, factions);
        Game.World = world;

        Map = new Map();
        Map.info = new MapInfo { Size = new IntVec3(40, 1, 40) };
        Map.cellIndices = new CellIndices(Map);
        Map.roofGrid = new RoofGrid(Map);
        Map.mapPawns = new MapPawns(Map);
        Game.Maps.Add(Map);

        LifeStage = new LifeStageDef { defName = "FixtureAdulthood" };
        LifeStage.ResolveDefNameHash();
    }

    private void CreateStats()
    {
        foreach (string name in AffectedStatNames)
        {
            AddStat(name, 1f);
        }

        AddStat("ShootingAccuracyPawn", 0.55f);
        AddStat("WorkSpeedGlobal", 1f);
    }

    private StatDef AddStat(string defName, float baseValue)
    {
        StatDef def = new()
        {
            defName = defName,
            defaultBaseValue = baseValue,
            supressDisabledError = true,
            workerClass = typeof(StatWorker),
        };
        def.ResolveDefNameHash();
        DefDatabase<StatDef>.Add(def);
        Stats[defName] = def;

        FieldInfo? defOfField = typeof(StatDefOf).GetField(defName, BindingFlags.Static | BindingFlags.Public);
        defOfField?.SetValue(null, def);
        return def;
    }

    private void CreateTracker()
    {
        Tracker = (GameComponent_IdealTracker)RuntimeHelpers.GetUninitializedObject(typeof(GameComponent_IdealTracker));
        State = new IdealTrackerState();

        SetBacking(State, "LifecycleState", IdealLifecycleState.Active);
        SetBacking(State, "P4Phase", IdealP4Phase.Active);

        IdealRuntimeCache runtimeCache = new(Game);
        Set(typeof(GameComponent_IdealTracker), "ownerGame", Tracker, Game);
        Set(typeof(GameComponent_IdealTracker), "state", Tracker, State);
        Set(typeof(GameComponent_IdealTracker), "runtimeCache", Tracker, runtimeCache);
        Set(typeof(GameComponent_IdealTracker), "recoveryMedical", Tracker, null);

        for (int i = 0; i < DummyComponentCount; i++)
        {
            Game.components.Add(new DummyGameComponent());
        }

        Game.components.Add(Tracker);
    }

    private void CreatePawns()
    {
        Ideal = NewPawn("FixtureIdeal", Intelligence.Humanlike, PlayerFaction, humanlike: true, hostileToAll: false);
        Plain = NewPawn("FixturePlain", Intelligence.Humanlike, PlayerFaction, humanlike: true, hostileToAll: false);
        Animal = NewPawn("FixtureAnimal", Intelligence.Animal, PlayerFaction, humanlike: false, hostileToAll: false);
        Mech = NewPawn("FixtureMech", Intelligence.ToolUser, PlayerFaction, humanlike: false, hostileToAll: false);
        Enemy = NewPawn("FixtureEnemyPawn", Intelligence.Humanlike, EnemyFaction, humanlike: true, hostileToAll: true);

        Set(typeof(IdealRuntimeCache), "resolvedPawn", Tracker.RuntimeCache, Ideal);
        Set(typeof(IdealRuntimeCache), "stablePawnId", Tracker.RuntimeCache, Ideal.GetUniqueLoadID());
        SetBacking(State, "CurrentIdealPawnId", Ideal.GetUniqueLoadID());
    }

    private Pawn NewPawn(string defName, Intelligence intelligence, Faction faction, bool humanlike, bool hostileToAll)
    {
        RaceProperties race = new()
        {
            intelligence = intelligence,
            baseBodySize = 1f,
            lifeStageAges = new List<LifeStageAge> { new() { def = LifeStage, minAge = 0f } },
        };

        ThingDef def = new()
        {
            defName = defName,
            category = ThingCategory.Pawn,
            thingClass = typeof(Pawn),
            race = race,
            statBases = new List<StatModifier>(),
            useHitPoints = false,
            stackLimit = 1,
        };

        Pawn pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = def;
        pawn.thingIDNumber = nextId++;
        pawn.stackCount = 1;
        pawn.kindDef = new PawnKindDef { defName = defName + "Kind", race = def, hostileToAll = hostileToAll };

        Set(typeof(ThingWithComps), "comps", pawn, new List<ThingComp> { DummyComp(pawn) });
        Set(typeof(Thing), "factionInt", pawn, faction);
        Set(typeof(Thing), "positionInt", pawn, new IntVec3(10, 0, 10));
        Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)0);

        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        pawn.ageTracker.lockedLifeStageIndex = 0;
        Set(typeof(Pawn_AgeTracker), "cachedLifeStageIndex", pawn.ageTracker, 0);

        if (humanlike)
        {
            pawn.story = new Pawn_StoryTracker(pawn);
            Set(typeof(Pawn_StoryTracker), "childhood", pawn.story, new BackstoryDef { defName = "FixtureChildhood" });
            Set(typeof(Pawn_StoryTracker), "adulthood", pawn.story, new BackstoryDef { defName = "FixtureAdulthood" });
            pawn.equipment = new Pawn_EquipmentTracker(pawn);
            pawn.apparel = new Pawn_ApparelTracker(pawn);
        }

        AllPawns.Add(pawn);
        return pawn;
    }

    /// <summary>Restores every field the scenarios mutate to a neutral state.</summary>
    internal void Reset()
    {
        Scribe.mode = LoadSaveMode.Inactive;

        SetBacking(State, "LifecycleState", IdealLifecycleState.Active);
        SetBacking(State, "CurrentIdealPawnId", Ideal.GetUniqueLoadID());
        SetBacking(State, "P4Phase", IdealP4Phase.Active);
        Set(typeof(GameComponent_IdealTracker), "recoveryMedical", Tracker, null);
        Set(typeof(IdealRuntimeCache), "resolvedPawn", Tracker.RuntimeCache, Ideal);
        Set(typeof(IdealRuntimeCache), "stablePawnId", Tracker.RuntimeCache, Ideal.GetUniqueLoadID());

        State.WorldEffects.Effects.Clear();
        Set(typeof(IdealExclusiveTraitState), "idleStudyTicks", State.ExclusiveTraits, 0);
        Set(typeof(IdealExclusiveTraitState), "burstUntil", State.ExclusiveTraits, 0);
        Set(typeof(IdealExclusiveTraitState), "lastTick", State.ExclusiveTraits, Now);

        foreach (Pawn pawn in AllPawns)
        {
            Set(typeof(Pawn_HealthTracker), "healthState", pawn.health, PawnHealthState.Mobile);
            Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)0);
            Set(typeof(Thing), "positionInt", pawn, new IntVec3(10, 0, 10));
            Set(typeof(Pawn), "jobs", pawn, null);
            pawn.def.statBases!.Clear();
            ClearGear(pawn);

            if (pawn.story != null)
            {
                Set(typeof(Pawn_StoryTracker), "childhood", pawn.story, new BackstoryDef { defName = "FixtureChildhood" });
                Set(typeof(Pawn_StoryTracker), "adulthood", pawn.story, new BackstoryDef { defName = "FixtureAdulthood" });
                pawn.story.traits.allTraits.Clear();
            }
        }
    }

    internal void SetIdealBackstory(bool active)
    {
        Set(typeof(Pawn_StoryTracker), "childhood", Ideal.story,
            new BackstoryDef { defName = active ? UpstreamLogic.ChildhoodPlaceholder : "FixtureChildhood" });
        Set(typeof(Pawn_StoryTracker), "adulthood", Ideal.story,
            new BackstoryDef { defName = active ? UpstreamLogic.AdulthoodPlaceholder : "FixtureAdulthood" });
    }

    internal void AddTrait(Pawn pawn, string traitDefName)
    {
        TraitDef def = new()
        {
            defName = traitDefName,
            degreeDatas = new List<TraitDegreeData> { new() { degree = 0 } },
        };
        pawn.story!.traits.allTraits.Add(new Trait(def));
    }

    internal void SetSpawned(Pawn pawn, bool spawned)
    {
        Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)(spawned ? 0 : -1));
    }

    internal void SetDead(Pawn pawn)
    {
        Set(typeof(Pawn_HealthTracker), "healthState", pawn.health, PawnHealthState.Dead);
    }

    internal void SetDiscarded(Pawn pawn)
    {
        Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)(-3));
    }

    internal void SetPosition(Pawn pawn, IntVec3 position)
    {
        Set(typeof(Thing), "positionInt", pawn, position);
    }

    internal void SetRepairJob(Pawn pawn)
    {
        Pawn_JobTracker tracker = new(pawn);
        Set(typeof(Pawn_JobTracker), "curJob", tracker, new Job(new JobDef { defName = UpstreamLogic.RepairJob }));
        Set(typeof(Pawn), "jobs", pawn, tracker);
    }

    internal void SetStudy(bool needsStudy)
    {
        Set(typeof(IdealExclusiveTraitState), "idleStudyTicks", State.ExclusiveTraits, needsStudy ? 200_000 : 0);
    }

    internal void SetBurstActive(bool active)
    {
        Set(typeof(IdealExclusiveTraitState), "burstUntil", State.ExclusiveTraits, active ? Now + 360 : 0);
    }

    internal void AddStatBase(Pawn pawn, StatDef stat, float value)
    {
        pawn.def.statBases!.Add(new StatModifier { stat = stat, value = value });
    }

    internal void ClearGear(Pawn pawn)
    {
        if (pawn.equipment != null)
        {
            EquipmentOwner(pawn.equipment).InnerListForReading.Clear();
        }

        if (pawn.apparel != null)
        {
            ApparelOwner(pawn.apparel).InnerListForReading.Clear();
        }
    }

    internal void GiveWeapon(Pawn pawn, string defName)
    {
        ThingDef def = new()
        {
            defName = defName,
            category = ThingCategory.Item,
            thingClass = typeof(ThingWithComps),
            equipmentType = EquipmentType.Primary,
            stackLimit = 1,
            useHitPoints = false,
        };
        ThingWithComps weapon = new() { def = def, stackCount = 1 };
        Set(typeof(ThingWithComps), "comps", weapon, new List<ThingComp> { DummyComp(weapon) });
        Set(typeof(Thing), "mapIndexOrState", weapon, (sbyte)(-1));
        EquipmentOwner(pawn.equipment!).InnerListForReading.Add(weapon);
    }

    internal void WearApparel(Pawn pawn, string defName)
    {
        ThingDef def = new()
        {
            defName = defName,
            category = ThingCategory.Item,
            thingClass = typeof(Apparel),
            apparel = new ApparelProperties(),
            stackLimit = 1,
            useHitPoints = false,
        };
        Apparel apparel = new() { def = def, stackCount = 1 };
        Set(typeof(Thing), "mapIndexOrState", apparel, (sbyte)(-1));
        ApparelOwner(pawn.apparel!).InnerListForReading.Add(apparel);
    }

    internal void AddWorldEffect(Pawn source, Map map, IntVec3 center)
    {
        State.WorldEffects.Effects.Add(new IdealWorldEffect
        {
            Kind = "Stasis",
            Source = source,
            Map = map,
            Center = center,
            Until = Now + 5_000,
            Completed = false,
        });
    }

    private static ThingComp DummyComp(ThingWithComps parent)
    {
        return new CompColorable { parent = parent };
    }

    private static ThingOwner<ThingWithComps> EquipmentOwner(Pawn_EquipmentTracker tracker)
    {
        return (ThingOwner<ThingWithComps>)typeof(Pawn_EquipmentTracker)
            .GetField("equipment", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(tracker)!;
    }

    private static ThingOwner<Apparel> ApparelOwner(Pawn_ApparelTracker tracker)
    {
        return (ThingOwner<Apparel>)typeof(Pawn_ApparelTracker)
            .GetField("wornApparel", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(tracker)!;
    }

    private static void SetBacking(object instance, string propertyName, object? value)
    {
        Set(instance.GetType(), "<" + propertyName + ">k__BackingField", instance, value);
    }

    internal static void Set(Type type, string name, object? instance, object? value)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingFieldException(type.FullName, name);
        field.SetValue(instance, value);
    }

    /// <summary>Simulates RimWorld's dev-mode def reload: same defNames, brand-new Def objects.</summary>
    internal void ReloadStats()
    {
        List<KeyValuePair<string, StatDef>> old = Stats.ToList();
        DefDatabase<StatDef>.Clear();
        Stats.Clear();
        foreach (KeyValuePair<string, StatDef> pair in old)
        {
            AddStat(pair.Key, pair.Value.defaultBaseValue);
        }
    }

    internal static object? Get(Type type, string name, object? instance)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingFieldException(type.FullName, name);
        return field.GetValue(instance);
    }
}
