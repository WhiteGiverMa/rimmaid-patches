using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using DMSSR.Gene;
using DMSSR.Hediffs;
using DMSSR.Jobs;
using DMSSR.Misc;
using Fortified;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using WhiteGiver.DMSSRUnifiedFix.Audited;

namespace DMSSRUnifiedFix.Validation.Audited;

/// <summary>
/// Real RimWorld 1.7 classes with a hand-built world: real Map, Pathing, ThingGrid, HediffSets,
/// StatWorkers, Ability/Verb trackers and the real DMSSR/Fortified types. Only defs and Unity scene
/// data are constructed here, exactly like the XML loader would.
/// </summary>
internal sealed class Fixture
{
    internal const int MapSize = 20;

    internal Map Map = null!;
    internal Faction PlayerFaction = null!;
    internal Faction HostileFaction = null!;
    internal ThingDef MechDef = null!;
    internal ThingDef HumanDef = null!;
    internal BodyDef Body = null!;
    internal readonly List<BodyPartRecord> Parts = new();

    internal HediffDef DeathCheat = null!;
    internal HediffDef Oppressive = null!;
    internal HediffDef CascadeDock = null!;
    internal HediffDef Blackwater = null!;
    internal HediffDef UndyingBoost = null!;
    internal HediffDef CasterMark = null!;
    internal HediffDef Linked = null!;
    internal HediffDef Burst = null!;
    internal HediffDef ChargeBuff = null!;
    internal HediffDef Fatigue = null!;
    internal HediffDef RemoteController = null!;
    internal HediffDef SyntheticInterface = null!;
    internal HediffDef FacedancerFacechange = null!;
    internal HediffDef Injury = null!;
    internal HediffDef FibrousMechanites = null!;
    internal HediffDef SensoryMechanites = null!;
    internal HediffDef HeteroLeg = null!;

    internal ThingDef CerebellumItem = null!;
    internal ThingDef CascadeItem = null!;
    internal ThingDef BlackwaterItem = null!;
    internal ThingDef HeteroLegItem = null!;
    internal ThingDef PodDef = null!;

    internal RecipeDef HeteroLegRecipe = null!;

    private static readonly FieldInfo PawnsSpawnedField =
        typeof(MapPawns).GetField("pawnsSpawned", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private int nextId = 1000;

    internal static Fixture Create()
    {
        Fixture fixture = new();
        return fixture;
    }

    private Fixture()
    {
        FieldInfo bindingNow = typeof(DefOfHelper).GetField("bindingNow", BindingFlags.NonPublic | BindingFlags.Static)!;
        bindingNow.SetValue(null, true);
        try
        {
            SetBiotech(true);
            BindBaseDefs();
            Body = MakeBody();
            MechDef = MakePawnDef("Audit_Mech", FleshTypeDefOf.Mechanoid, Intelligence.ToolUser);
            HumanDef = MakePawnDef("Audit_Human", FleshTypeDefOf.Normal, Intelligence.Humanlike);
            MakeWorldAndMap();
            PlayerFaction = Faction.OfPlayer;
            HostileFaction = MakeHostileFaction();
            MakeItemsAndRecipes();
            MakeHediffs();
            BindJobDefs();
            SyntheticBionicUtility.EnsureModificationHediffsKeepOnBodyPartRestoration();
            setSyntheticRecipes();
        }
        finally
        {
            bindingNow.SetValue(null, false);
        }
    }

    private void BindBaseDefs()
    {
        foreach (string name in new[] { "Consciousness", "Moving", "Manipulation" })
        {
            PawnCapacityDef capacity = GetOrAdd<PawnCapacityDef>(name);
            capacity.workerClass = typeof(PawnCapacityWorker);
            typeof(PawnCapacityDefOf).GetField(name)!.SetValue(null, capacity);
        }

        BindStat("PainShockThreshold", 0.8f);
        BindStat("HealthScaleFactor", 1f);
        BindStat("LethalDamageThreshold", 150f);
        BindStat("MaxHitPoints", 100f);
        BindStat("BandwidthCost", 0f);
        BindStat("MechBandwidth", 0f);
        BindStat("MechControlGroups", 0f);
        BindStat("ArmorRating_Sharp", 0.5f);
        BindStat("ArmorRating_Blunt", 0.5f);

        BindDefOf(typeof(PawnCapacityDefOf), "Manipulation", GetOrAdd<PawnCapacityDef>("Manipulation"));
        BindDefOf(typeof(PawnCapacityDefOf), "Consciousness", GetOrAdd<PawnCapacityDef>("Consciousness"));
        BindDefOf(typeof(PawnCapacityDefOf), "Moving", GetOrAdd<PawnCapacityDef>("Moving"));

        BodyPartTagDef manip = GetOrAdd<BodyPartTagDef>("ManipulationLimbCore");
        BodyPartTagDef moving = GetOrAdd<BodyPartTagDef>("MovingLimbCore");
        BindDefOf(typeof(BodyPartTagDefOf), "ManipulationLimbCore", manip);
        BindDefOf(typeof(BodyPartTagDefOf), "MovingLimbCore", moving);

        FleshTypeDefOf.Mechanoid = GetOrAdd<FleshTypeDef>("Mechanoid");
        FleshTypeDefOf.Mechanoid.isOrganic = false;
        FleshTypeDefOf.Normal = GetOrAdd<FleshTypeDef>("Normal");
        FleshTypeDefOf.Normal.isOrganic = true;

        BindDefOf(typeof(MessageTypeDefOf), "PositiveEvent", GetOrAdd<MessageTypeDef>("PositiveEvent"));
        BindDefOf(typeof(MessageTypeDefOf), "RejectInput", GetOrAdd<MessageTypeDef>("RejectInput"));
        BindDefOf(typeof(MessageTypeDefOf), "NegativeHealthEvent", GetOrAdd<MessageTypeDef>("NegativeHealthEvent"));

        HediffDef mechlink = GetOrAdd<HediffDef>("MechlinkImplant");
        mechlink.hediffClass = typeof(HediffWithComps);
        BindDefOf(typeof(HediffDefOf), "MechlinkImplant", mechlink);

        HediffDef missing = GetOrAdd<HediffDef>("MissingBodyPart");
        missing.hediffClass = typeof(Hediff_MissingPart);
        BindDefOf(typeof(HediffDefOf), "MissingBodyPart", missing);

        GetOrAdd<DesignationDef>("AuditDesignation");

        PawnRelationDef overseer = GetOrAdd<PawnRelationDef>("Overseer");
        overseer.workerClass = typeof(PawnRelationWorker_Overseer);
        overseer.reflexive = true;
        BindDefOf(typeof(PawnRelationDefOf), "Overseer", overseer);
    }

    private BodyDef MakeBody()
    {
        BodyDef body = new() { defName = "DMS_Synthroid", label = "synthroid" };
        BodyPartDef torsoDef = PartDef("Torso", 200);
        BodyPartRecord torso = new()
        {
            def = torsoDef,
            body = body,
            coverage = 1f,
            parts = new List<BodyPartRecord>(),
        };
        body.corePart = torso;

        AddPart(body, torso, "Head", 40, 0.12f);
        AddPart(body, torso, "Brain", 30, 0.05f);
        AddPart(body, torso, "Spine", 50, 0.12f);
        AddPart(body, torso, "Reactor", 60, 0.12f);
        AddPart(body, torso, "DMS_SynthicLeg", 60, 0.12f);
        body.ResolveReferences();
        Parts.AddRange(body.AllParts);
        DefDatabase<BodyDef>.Add(body);
        return body;
    }

    private void AddPart(BodyDef body, BodyPartRecord parent, string name, int hp, float coverage)
    {
        parent.parts.Add(new BodyPartRecord
        {
            def = PartDef(name, hp),
            body = body,
            parent = parent,
            coverage = coverage,
            parts = new List<BodyPartRecord>(),
        });
    }

    private static BodyPartDef PartDef(string name, int hp)
    {
        BodyPartDef def = GetOrAdd<BodyPartDef>(name);
        def.label = name;
        def.hitPoints = hp;
        def.tags ??= new List<BodyPartTagDef>();
        return def;
    }

    private ThingDef MakePawnDef(string name, FleshTypeDef flesh, Intelligence intelligence)
    {
        RaceProperties race = new()
        {
            body = Body,
            intelligence = intelligence,
            baseHealthScale = 1f,
            baseBodySize = 1f,
            lifeStageAges = new List<LifeStageAge>
            {
                new() { def = new LifeStageDef { defName = name + "_Adult", healthScaleFactor = 1f, bodySizeFactor = 1f }, minAge = 0f },
            },
            mechEnabledWorkTypes = new List<WorkTypeDef>(),
        };
        Set(typeof(RaceProperties), "fleshType", race, flesh);
        Set(typeof(RaceProperties), "bloodDef", race, new ThingDef
        {
            defName = name + "_Blood",
            label = "blood",
            category = ThingCategory.Item,
            thingClass = typeof(Thing),
        });
        ThingDef def = new()
        {
            defName = name,
            label = name,
            category = ThingCategory.Pawn,
            thingClass = typeof(Pawn),
            race = race,
            comps = new List<CompProperties>(),
            statBases = new List<StatModifier>(),
        };
        if (flesh == FleshTypeDefOf.Mechanoid)
        {
            def.comps.Add(new CompProperties_OverseerSubject());
            def.statBases.Add(new StatModifier { stat = StatDefOf.BandwidthCost, value = 1f });
        }
        DefDatabase<ThingDef>.Add(def);
        return def;
    }

    private void MakeItemsAndRecipes()
    {
        CerebellumItem = ModificationItem(
            "DMSSR_EmbCerebellum", "PR protocol", null, new List<BodyPartDef>());
        CascadeItem = ModificationItem(
            "DMSSR_CascadeDock", "cascade dock", null, new List<BodyPartDef>());
        BlackwaterItem = ModificationItem(
            "DMSSR_Blackwater", "Blackwater", null, new List<BodyPartDef>());

        ThingCategoryDef bodyPartsBionic = GetOrAdd<ThingCategoryDef>("BodyPartsBionic");
        HeteroLegItem = new ThingDef
        {
            defName = "DMSSR_HeteroBionicLeg",
            label = "heterogeneous bionic leg",
            category = ThingCategory.Item,
            thingClass = typeof(ThingWithComps),
            stackLimit = 1,
            useHitPoints = false,
            tradeTags = new List<string> { "DMS_Synth_Bionic" },
            thingCategories = new List<ThingCategoryDef> { bodyPartsBionic },
            comps = new List<CompProperties>(),
        };
        DefDatabase<ThingDef>.Add(HeteroLegItem);

        HeteroLeg = GetOrAdd<HediffDef>("DMSSR_HeteroBionicLeg");
        HeteroLeg.label = "heterogeneous bionic leg";
        HeteroLeg.hediffClass = typeof(Hediff_AddedPart);
        HeteroLeg.addedPartProps = new AddedBodyPartProps { partEfficiency = 1.4f, solid = true, betterThanNatural = true };
        HeteroLeg.spawnThingOnRemoved = HeteroLegItem;
        HeteroLeg.countsAsAddedPartOrImplant = true;

        IngredientCount ingredient = new();
        ingredient.filter.SetAllow(HeteroLegItem, true);
        ingredient.SetBaseCount(1);
        HeteroLegRecipe = new RecipeDef
        {
            defName = "DMS_Install_HeteroBionicLeg",
            addsHediff = HeteroLeg,
            appliedOnFixedBodyParts = new List<BodyPartDef> { PartDef("DMS_SynthicLeg", 60) },
            ingredients = new List<IngredientCount> { ingredient },
            fixedIngredientFilter = new ThingFilter(),
        };
        HeteroLegRecipe.fixedIngredientFilter.SetAllow(HeteroLegItem, true);
        DefDatabase<RecipeDef>.Add(HeteroLegRecipe);

        PodDef = new ThingDef
        {
            defName = "DMS_GeneFittingPod",
            label = "gene fitting pod",
            category = ThingCategory.Building,
            thingClass = typeof(Building_DMSGeneFittingPod),
            comps = new List<CompProperties> { new CompProperties_DMSGeneFittingPod() },
            statBases = new List<StatModifier>(),
        };
        DefDatabase<ThingDef>.Add(PodDef);
    }

    private ThingDef ModificationItem(string defName, string label, HediffDef? hediff, List<BodyPartDef> targetParts)
    {
        ThingDef def = new()
        {
            defName = defName,
            label = label,
            category = ThingCategory.Item,
            thingClass = typeof(ThingWithComps),
            stackLimit = 1,
            useHitPoints = false,
            comps = new List<CompProperties>(),
        };
        def.comps.Add(new CompProperties_AddHediffOnTarget
        {
            compClass = typeof(CompTargetable_AddHediffOnTarget),
            hediffDef = hediff!,
            targetBodyPartDefs = targetParts,
            supportRaceDefs = new List<ThingDef>(),
        });
        DefDatabase<ThingDef>.Add(def);
        return def;
    }

    private void MakeHediffs()
    {
        DeathCheat = RegisterHediff(new HediffDef
        {
            defName = "DMS_DeathCheat",
            label = "PR protocol",
            hediffClass = typeof(HediffWithComps),
            isBad = false,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_DeathCheatCharges { addCharges = 1 },
            },
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_DeathCheat", DeathCheat);

        Oppressive = RegisterHediff(new HediffDef
        {
            defName = "DMS_OppressiveRestructuring",
            label = "oppressive restructuring",
            hediffClass = typeof(HediffWithComps),
            isBad = true,
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_OppressiveRestructuring", Oppressive);

        CascadeDock = PartModification("DMS_CascadeDock", "cascade dock", CascadeItem, new[] { "Brain", "ArtificialBrain" });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_CascadeDock", CascadeDock);

        Blackwater = PartModification("DMS_Blackwater", "Blackwater", BlackwaterItem, new[] { "Spine" });

        UndyingBoost = RegisterHediff(new HediffDef
        {
            defName = "DMS_UndyingArmorBoost",
            label = "Undying",
            hediffClass = typeof(HediffWithComps),
            isBad = false,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_Disappears
                {
                    disappearsAfterTicks = new IntRange(10000, 10000),
                    showRemainingTime = true,
                },
                new HediffCompProperties_ArmorBoost(),
            },
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_UndyingArmorBoost", UndyingBoost);

        CasterMark = RegisterHediff(new HediffDef
        {
            defName = "DMS_Schizophrenia_CasterMark",
            label = "Cascade root node",
            hediffClass = typeof(HediffWithComps),
            isBad = false,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_Disappears
                {
                    disappearsAfterTicks = new IntRange(1800, 1800),
                    showRemainingTime = true,
                },
                new HediffCompProperties_SchizophreniaEmitter
                {
                    linkedHediff = null,
                    range = 8f,
                    checkIntervalTicks = 60,
                    onlySameFaction = true,
                },
            },
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_Schizophrenia_CasterMark", CasterMark);

        Linked = RegisterHediff(new HediffDef
        {
            defName = "DMS_Schizophrenia_Linked",
            label = "Cascade leaf node",
            hediffClass = typeof(HediffWithComps),
            isBad = false,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_SchizophreniaLink(),
            },
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_Schizophrenia_Linked", Linked);

        Burst = RegisterHediff(new HediffDef
        {
            defName = "DMS_Skylark_PowerCharge_Burst",
            label = "Charging",
            hediffClass = typeof(HediffWithComps),
            isBad = false,
            initialSeverity = 1f,
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_Skylark_PowerCharge_Burst", Burst);

        ChargeBuff = RegisterHediff(new HediffDef
        {
            defName = "DMS_Skylark_PowerCharge_Buff",
            label = "Enhanced performance",
            hediffClass = typeof(HediffWithComps),
            isBad = false,
            initialSeverity = 1f,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_Disappears
                {
                    disappearsAfterTicks = new IntRange(600, 600),
                    showRemainingTime = true,
                },
            },
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_Skylark_PowerCharge_Buff", ChargeBuff);

        Fatigue = RegisterHediff(new HediffDef
        {
            defName = "DMS_Skylark_PowerCharge_Fatigue",
            label = "Enhancement aftermath",
            hediffClass = typeof(HediffWithComps),
            isBad = true,
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_Skylark_PowerCharge_Fatigue", Fatigue);

        RemoteController = RegisterHediff(new HediffDef
        {
            defName = "DMS_RemoteController",
            label = "Remote control plugin",
            hediffClass = typeof(Hediff_Implant),
            isBad = false,
        });

        SyntheticInterface = RegisterHediff(new HediffDef
        {
            defName = "DMSSR_WetwareSyntheticInterface",
            label = "wetware synthetic interface",
            hediffClass = typeof(HediffWithComps),
            isBad = false,
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMSSR_WetwareSyntheticInterface", SyntheticInterface);

        FacedancerFacechange = RegisterHediff(new HediffDef
        {
            defName = "DMS_Facedancer_Facechange",
            label = "facechange",
            hediffClass = typeof(HediffWithComps),
            isBad = false,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_FacedancerFacechange
                {
                    explosionRadius = 5f,
                    explosionDamage = 80,
                },
            },
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_Facedancer_Facechange", FacedancerFacechange);

        Injury = RegisterHediff(new HediffDef
        {
            defName = "AuditInjury",
            label = "injury",
            hediffClass = typeof(Hediff_Injury),
            isBad = true,
            tendable = true,
            injuryProps = new InjuryProps { bleedRate = 0.1f },
            comps = new List<HediffCompProperties>
            {
                TendDuration(48f),
            },
        });

        FibrousMechanites = Mechanite("FibrousMechanites");
        SensoryMechanites = Mechanite("SensoryMechanites");

        // Bind the Fortified item profiles against the final hediff defs.
        ((CompProperties_AddHediffOnTarget)BlackwaterItem.comps![0]).hediffDef = Blackwater;
        ((CompProperties_AddHediffOnTarget)CascadeItem.comps![0]).hediffDef = CascadeDock;
        ((CompProperties_AddHediffOnTarget)CerebellumItem.comps![0]).hediffDef = DeathCheat;
    }

    private static HediffCompProperties_TendDuration TendDuration(float hours)
    {
        HediffCompProperties_TendDuration props = new() { severityPerDayTended = -1f };
        typeof(HediffCompProperties_TendDuration)
            .GetField("baseTendDurationHours", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(props, hours);
        return props;
    }

    private static HediffDef RegisterHediff(HediffDef def)
    {
        DefDatabase<HediffDef>.Add(def);
        return def;
    }

    private HediffDef Mechanite(string name)
    {        return RegisterHediff(new HediffDef
        {
            defName = name,
            label = name,
            hediffClass = typeof(HediffWithComps),
            isInfection = true,
            tendable = true,
            isBad = true,
            initialSeverity = 0.001f,
            minSeverity = 0.001f,
            maxSeverity = 1f,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_Disappears
                {
                    disappearsAfterTicks = new IntRange(900000, 1800000),
                },
                new HediffCompProperties_Immunizable { severityPerDayNotImmune = 0.25f },
            },
            stages = new List<HediffStage>
            {
                new() { minSeverity = 0f, label = "mild pain" },
                new() { minSeverity = 0.5f, label = "intense pain" },
            },
        });
    }

    private HediffDef PartModification(string defName, string label, ThingDef item, string[] targetPartNames)
    {
        List<BodyPartDef> targets = new();
        foreach (string name in targetPartNames)
        {
            BodyPartDef? def = DefDatabase<BodyPartDef>.GetNamedSilentFail(name);
            if (def != null)
            {
                targets.Add(def);
            }
        }

        HediffDef hediff = RegisterHediff(new HediffDef
        {
            defName = defName,
            label = label,
            hediffClass = typeof(HediffWithComps),
            isBad = false,
            defaultInstallPart = PartDef("Reactor", 60),
            spawnThingOnRemoved = item,
            countsAsAddedPartOrImplant = true,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_Modification(),
            },
        });
        ((CompProperties_AddHediffOnTarget)item.comps![0]).hediffDef = hediff;
        ((CompProperties_AddHediffOnTarget)item.comps![0]).targetBodyPartDefs = targets;
        return hediff;
    }

    private void BindJobDefs()
    {
        JobDef modify = GetOrAdd<JobDef>("FFF_Modification");
        modify.driverClass = typeof(JobDriver_ApplyModification);
        JobDef remove = GetOrAdd<JobDef>("FFF_ModificationRemove");
        remove.driverClass = typeof(JobDriver_RemoveModification);
        JobDef remote = GetOrAdd<JobDef>("DMS_OperateMechRemote");
        remote.driverClass = typeof(JobDriver_OperateMechRemote);
    }

    private void setSyntheticRecipes()
    {
        FieldInfo field = typeof(SyntheticBionicUtility).GetField(
            "syntheticInstallRecipes", BindingFlags.NonPublic | BindingFlags.Static)!;
        field.SetValue(null, new List<RecipeDef> { HeteroLegRecipe });
    }

    private void MakeWorldAndMap()
    {
        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, new List<Map>());
        game.tickManager = new TickManager();
        game.uniqueIDsManager = new UniqueIDsManager();
        game.relationshipRecords = new RelationshipRecords();
        game.playSettings = new PlaySettings();
        game.questManager = new QuestManager();
        game.storyWatcher = new StoryWatcher();
        Current.Game = game;
        Current.ProgramState = ProgramState.Playing;

        FactionDef playerDef = new() { defName = "PlayerColony", isPlayer = true };
        Faction player = new() { def = playerDef, loadID = 1 };
        FactionManager manager = (FactionManager)RuntimeHelpers.GetUninitializedObject(typeof(FactionManager));
        typeof(FactionManager).GetField("ofPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(manager, player);
        typeof(FactionManager).GetField("allFactions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(manager, new List<Faction> { player });

        World world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World));
        world.factionManager = manager;
        typeof(Game).GetField("worldInt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, world);

        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.uniqueID = 1;
        map.info = new MapInfo { Size = new IntVec3(MapSize, 1, MapSize), isPocketMap = true };
        game.Maps.Add(map);
        map.cellIndices = new CellIndices(MapSize, MapSize);
        map.events = new MapEvents(map);
        map.spawnedThings = new ThingOwner<Thing>(map);
        map.thingGrid = new ThingGrid(map);
        map.listerThings = new ListerThings(ListerThingsUse.Global);
        map.edificeGrid = new EdificeGrid(map);
        map.regionGrid = new RegionGrid(map);
        map.regionDirtyer = new RegionDirtyer(map);
        map.regionAndRoomUpdater = new RegionAndRoomUpdater(map) { Enabled = false };
        typeof(RegionAndRoomUpdater).GetField("initialized", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(map.regionAndRoomUpdater, true);
        PathGridDef pathDef = new() { defName = "AuditNormal" };
        PathingContext context = new(map, new PathGrid(map, pathDef));
        Pathing pathing = (Pathing)RuntimeHelpers.GetUninitializedObject(typeof(Pathing));
        typeof(Pathing).GetField("normal", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(pathing, context);
        typeof(Pathing).GetField("fenceBlocked", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(pathing, context);
        map.pathing = pathing;
        map.reachability = new Reachability(map);
        map.mapPawns = new MapPawns(map);
        map.designationManager = new DesignationManager(map);
        map.lordManager = new LordManager(map);
        map.reservationManager = new ReservationManager(map);
        map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
        map.attackTargetReservationManager = new AttackTargetReservationManager(map);
        map.areaManager = new AreaManager(map);
        map.components = new List<MapComponent>();
        Map = map;
    }

    private Faction MakeHostileFaction()
    {
        Faction hostile = new() { def = new FactionDef { defName = "Pirate", isPlayer = false }, loadID = 2 };
        hostile.SetRelation(new FactionRelation { other = PlayerFaction, kind = FactionRelationKind.Hostile });
        PlayerFaction.SetRelation(new FactionRelation { other = hostile, kind = FactionRelationKind.Hostile });
        return hostile;
    }

    internal Pawn NewPawn(ThingDef def, IntVec3 pos, Faction faction, bool humanlike = false)
    {
        PawnKindDef kind = GetOrAdd<PawnKindDef>(def.defName + "_Kind");
        kind.label = def.defName;
        kind.race = def;
        kind.lifeStages ??= new List<PawnKindLifeStage> { new() };
        Pawn pawn = new()
        {
            def = def,
            kindDef = kind,
            thingIDNumber = nextId++,
            Name = new NameSingle("Audit" + nextId),
        };
        Set(typeof(Thing), "factionInt", pawn, faction);
        Set(typeof(Thing), "positionInt", pawn, pos);
        Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)0);

        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        typeof(Pawn_AgeTracker).GetField("cachedLifeStageIndex", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(pawn.ageTracker, 0);
        pawn.mindState = new Pawn_MindState(pawn);
        pawn.needs = new Pawn_NeedsTracker(pawn);
        pawn.jobs = new Pawn_JobTracker(pawn);
        pawn.carryTracker = new Pawn_CarryTracker(pawn);
        pawn.inventory = new Pawn_InventoryTracker(pawn);
        pawn.stances = new Pawn_StanceTracker(pawn);
        pawn.relations = new Pawn_RelationsTracker(pawn);
        pawn.drafter = new Pawn_DraftController(pawn);
        pawn.playerSettings = new Pawn_PlayerSettings(pawn);

        if (humanlike)
        {
            // Prevent AddAndRemoveDynamicComponents from constructing database-backed trackers.
            pawn.foodRestriction = Uninitialized<Pawn_FoodRestrictionTracker>();
            pawn.outfits = Uninitialized<Pawn_OutfitTracker>();
            pawn.drugs = Uninitialized<Pawn_DrugPolicyTracker>();
            pawn.timetable = Uninitialized<Pawn_TimetableTracker>();
            pawn.reading = Uninitialized<Pawn_ReadingTracker>();
            pawn.inventoryStock = Uninitialized<Pawn_InventoryStockTracker>();
        }

        pawn.InitializeComps();
        Map.listerThings.Add(pawn);
        Map.thingGrid.Register(pawn);
        ((List<Pawn>)PawnsSpawnedField.GetValue(Map.mapPawns)!).Add(pawn);
        return pawn;
    }

    internal Pawn NewMech(IntVec3 pos, Faction? faction = null)
    {
        return NewPawn(MechDef, pos, faction ?? PlayerFaction);
    }

    internal Pawn NewColonist(IntVec3 pos)
    {
        return NewPawn(HumanDef, pos, PlayerFaction, humanlike: true);
    }

    internal Hediff GiveHediff(Pawn pawn, HediffDef def, BodyPartRecord? part = null)
    {
        Hediff hediff = HediffMaker.MakeHediff(def, pawn, part);
        pawn.health.AddHediff(hediff, part);
        return hediff;
    }

    internal Hediff GiveHediffDirect(Pawn pawn, HediffDef def, BodyPartRecord? part = null)
    {
        Hediff hediff = HediffMaker.MakeHediff(def, pawn, part);
        pawn.health.hediffSet.hediffs.Add(hediff);
        pawn.health.hediffSet.DirtyCache();
        return hediff;
    }

    internal BodyPartRecord Part(string name)
    {
        return Parts.Find(part => part.def.defName == name)
            ?? throw new MissingMemberException("Body part " + name);
    }

    private static void SetBiotech(bool active)
    {
        typeof(ModsConfig).GetField("biotechActive", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, active);
    }

    private static T Uninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }

    private static void BindStat(string name, float baseValue)
    {
        StatDef stat = GetOrAdd<StatDef>(name);
        stat.defaultBaseValue = baseValue;
        stat.workerClass = typeof(StatWorker);
        stat.cacheable = false;
        stat.supressDisabledError = true;
        typeof(StatDefOf).GetField(name)?.SetValue(null, stat);
        if (name.StartsWith("ArmorRating", StringComparison.Ordinal))
        {
            stat.minValue = 0f;
        }
    }

    private static T GetOrAdd<T>(string defName) where T : Def, new()
    {
        T? existing = DefDatabase<T>.GetNamedSilentFail(defName);
        if (existing != null)
        {
            return existing;
        }
        T def = new() { defName = defName };
        DefDatabase<T>.Add(def);
        return def;
    }

    private static void BindDefOf(Type type, string field, Def def)
    {
        type.GetField(field)?.SetValue(null, def);
    }

    private static void Set(Type type, string name, object? target, object? value)
    {
        (type.GetField(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, name)).SetValue(target, value);
    }
}
