using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMS;
using DMSSR.Hediffs;
using DMSSR.Jobs;
using Fortified;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace DMSSRNeuroglueFix.Validation;

internal sealed class Fixture
{
    internal const int MapSize = 16;

    private static readonly FieldInfo MapIndexField =
        typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo PositionField =
        typeof(Thing).GetField("positionInt", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo FleshTypeField =
        typeof(RaceProperties).GetField("fleshType", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo AgeCachedLifeStageField =
        typeof(Pawn_AgeTracker).GetField("cachedLifeStageIndex", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo BiotechActiveField =
        typeof(ModsConfig).GetField("biotechActive", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly FieldInfo RegionGridDirectField =
        typeof(RegionGrid).GetField("regionGrid", BindingFlags.NonPublic | BindingFlags.Instance)!;

    internal Map Map = null!;
    internal Faction PlayerFaction = null!;
    internal Faction HostileFaction = null!;
    internal Pawn Overseer = null!;
    internal MechanitorControlGroup ControlGroup = null!;
    internal MechWorkModeDef WorkModeWork = null!;
    internal MechWorkModeDef WorkModeRecharge = null!;
    internal MechWorkModeDef WorkModeEscort = null!;
    internal MechWorkModeDef WorkModeSelfShutdown = null!;
    internal ThingDef SkylarkDef = null!;
    internal ThingDef HumanDef = null!;
    internal ThingDef NeuroglueDef = null!;
    internal JobDef UseNeuroglueJobDef = null!;
    internal HediffDef ReservoirDef = null!;
    internal HediffDef NeuroglueHighDef = null!;
    internal HediffDef DegenerativeDef = null!;
    internal HediffDef SyntheticRejectionDef = null!;
    internal HediffDef BionicShockRemoveDef = null!;
    internal HediffDef BionicShockDef = null!;
    internal BodyDef SynthBody = null!;
    internal List<BodyPartRecord> Parts = null!;
    internal Region ConnectedRegion = null!;

    private int nextId = 100;

    internal static Fixture Create()
    {
        Fixture fx = new();
        FieldInfo bindingNow = typeof(DefOfHelper).GetField("bindingNow", BindingFlags.NonPublic | BindingFlags.Static)!;
        bindingNow.SetValue(null, true);
        try
        {
            BiotechActiveField.SetValue(null, true);
            fx.InitDefs();
            fx.InitWorldAndMap();
            fx.InitOverseer();
        }
        finally
        {
            bindingNow.SetValue(null, false);
        }

        return fx;
    }

    private void InitDefs()
    {
        PawnCapacityDef manipulation = GetOrAddDef<PawnCapacityDef>("Manipulation");
        manipulation.workerClass = typeof(PawnCapacityWorker);
        PawnCapacityDef consciousness = GetOrAddDef<PawnCapacityDef>("Consciousness");
        consciousness.workerClass = typeof(PawnCapacityWorker);
        PawnCapacityDef moving = GetOrAddDef<PawnCapacityDef>("Moving");
        moving.workerClass = typeof(PawnCapacityWorker);
        BindDefOf(typeof(PawnCapacityDefOf), "Manipulation", manipulation);
        BindDefOf(typeof(PawnCapacityDefOf), "Consciousness", consciousness);
        BindDefOf(typeof(PawnCapacityDefOf), "Moving", moving);

        BindStat("PainShockThreshold", 0.8f);
        BindStat("MaxHitPoints", 100f);
        BindStat("CarryingCapacity", 75f);
        BindStat("Mass", 0.05f);
        BindStat("Nutrition", 0f);

        BodyPartTagDef manipTag = GetOrAddDef<BodyPartTagDef>("ManipulationLimbCore");
        BodyPartTagDef conTag = GetOrAddDef<BodyPartTagDef>("ConsciousnessSource");
        BodyPartTagDef moveTag = GetOrAddDef<BodyPartTagDef>("MovingLimbCore");
        BindDefOf(typeof(BodyPartTagDefOf), "ManipulationLimbCore", manipTag);
        BindDefOf(typeof(BodyPartTagDefOf), "ConsciousnessSource", conTag);
        BindDefOf(typeof(BodyPartTagDefOf), "MovingLimbCore", moveTag);

        SynthBody = new BodyDef { defName = "DMS_Synthroid", label = "synthroid" };
        BodyPartDef torsoDef = new() { defName = "Torso", label = "torso", hitPoints = 40 };
        string[] childNames =
        {
            "Neck", "Head", "Skull", "Brain", "Nose", "Tongue", "RightClavicle", "RightSightSensor", "LeftArm", "RightArm"
        };

        BodyPartRecord core = new()
        {
            def = torsoDef,
            body = SynthBody,
            coverage = 1f,
            parts = new List<BodyPartRecord>(),
        };

        for (int i = 0; i < childNames.Length; i++)
        {
            string name = childNames[i];
            var tags = new List<BodyPartTagDef>();
            if (name == "Brain") tags.Add(conTag);
            if (name is "LeftArm" or "RightArm") tags.Add(manipTag);
            BodyPartDef pDef = new() { defName = name, label = name.ToLowerInvariant(), hitPoints = 20, tags = tags };
            core.parts.Add(new BodyPartRecord
            {
                def = pDef,
                body = SynthBody,
                parent = core,
                coverage = 0.08f,
                parts = new List<BodyPartRecord>(),
            });
        }

        SynthBody.corePart = core;
        SynthBody.ResolveReferences();
        Parts = SynthBody.AllParts;

        FleshTypeDef mechFlesh = new() { defName = "Mechanoid", isOrganic = false };
        FleshTypeDef humanFlesh = new() { defName = "Normal", isOrganic = true };
        BindDefOf(typeof(FleshTypeDefOf), "Mechanoid", mechFlesh);
        BindDefOf(typeof(FleshTypeDefOf), "Normal", humanFlesh);

        WorkModeWork = GetOrAddDef<MechWorkModeDef>("Work");
        WorkModeRecharge = GetOrAddDef<MechWorkModeDef>("Recharge");
        WorkModeEscort = GetOrAddDef<MechWorkModeDef>("Escort");
        WorkModeSelfShutdown = GetOrAddDef<MechWorkModeDef>("SelfShutdown");
        BindDefOf(typeof(MechWorkModeDefOf), "Work", WorkModeWork);
        BindDefOf(typeof(MechWorkModeDefOf), "Recharge", WorkModeRecharge);
        BindDefOf(typeof(MechWorkModeDefOf), "Escort", WorkModeEscort);
        BindDefOf(typeof(MechWorkModeDefOf), "SelfShutdown", WorkModeSelfShutdown);

        PawnRelationDef overseerRel = GetOrAddDef<PawnRelationDef>("Overseer");
        BindDefOf(typeof(PawnRelationDefOf), "Overseer", overseerRel);

        HediffDef mechlinkDef = GetOrAddDef<HediffDef>("MechlinkImplant");
        mechlinkDef.hediffClass = typeof(HediffWithComps);
        BindDefOf(typeof(HediffDefOf), "MechlinkImplant", mechlinkDef);

        MessageTypeDef posMsg = GetOrAddDef<MessageTypeDef>("PositiveEvent");
        MessageTypeDef negMsg = GetOrAddDef<MessageTypeDef>("NegativeHealthEvent");
        BindDefOf(typeof(MessageTypeDefOf), "PositiveEvent", posMsg);
        BindDefOf(typeof(MessageTypeDefOf), "NegativeHealthEvent", negMsg);

        GetOrAddDef<DesignationDef>("DummyDesignation");

        SkylarkDef = MakePawnDef("DMS_Mech_Skylark", mechFlesh, Intelligence.ToolUser, humanlike: false);
        HumanDef = MakePawnDef("Human", humanFlesh, Intelligence.Humanlike, humanlike: true);

        // Exact upstream HediffDefs:
        NeuroglueHighDef = RegisterHediff(new HediffDef
        {
            defName = "NeuroglueHigh",
            label = "receptor gel",
            hediffClass = typeof(Hediff_High),
            initialSeverity = 1f,
            maxSeverity = 1f,
            isBad = false,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_SeverityPerDay { severityPerDay = -0.18f },
            },
            stages = new List<HediffStage>
            {
                new() { painOffset = 0.1f },
            },
        });

        DegenerativeDef = RegisterHediff(new HediffDef
        {
            defName = "DMS_DegenerativeSyndrome",
            label = "degenerative syndrome",
            hediffClass = typeof(HediffWithComps),
            initialSeverity = 0.05f,
            minSeverity = 0f,
            maxSeverity = 1f,
            isBad = true,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_SeverityPerDay { severityPerDay = 0.15f },
                new HediffCompProperties_RemoveIfOtherHediff
                {
                    hediffs = new List<HediffDef> { NeuroglueHighDef },
                },
                new HediffCompProperties_DegenerativeSyndromeSpread { spreadStep = 0.25f },
            },
        });
        BindDefOf(typeof(DMS_HediffDefOf), "DMS_DegenerativeSyndrome", DegenerativeDef);

        SyntheticRejectionDef = RegisterHediff(new HediffDef
        {
            defName = "DMS_SyntheticRejection",
            label = "synthetic rejection",
            hediffClass = typeof(HediffWithComps),
            initialSeverity = 1f,
            isBad = true,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_RemoveIfOtherHediff
                {
                    hediffs = new List<HediffDef> { NeuroglueHighDef },
                },
            },
        });

        BionicShockRemoveDef = RegisterHediff(new HediffDef
        {
            defName = "DMS_BionicShockRemove",
            label = "phantom limb syndrome",
            hediffClass = typeof(HediffWithComps),
            initialSeverity = 1f,
            isBad = true,
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_RemoveIfOtherHediff
                {
                    hediffs = new List<HediffDef> { NeuroglueHighDef },
                },
            },
        });

        BionicShockDef = RegisterHediff(new HediffDef
        {
            defName = "DMS_BionicShock",
            label = "xeno limb syndrome",
            hediffClass = typeof(Hediff_LevelLabel),
            initialSeverity = 1f,
            minSeverity = 0f,
            maxSeverity = 6f,
            isBad = true,
        });

        ReservoirDef = RegisterHediff(new HediffDef
        {
            defName = "DMSSR_BiogelReservoir",
            label = "heterogeneous signal",
            hediffClass = typeof(Hediff_BiogelReservoir),
            initialSeverity = 1f,
            maxSeverity = 1f,
            isBad = false,
        });

        // Exact Neuroglue ThingDef after DMSSR_BiogelGenes.xml patch:
        NeuroglueDef = new ThingDef
        {
            defName = "Neuroglue",
            label = "neuroglue",
            category = ThingCategory.Item,
            thingClass = typeof(ThingWithComps),
            stackLimit = 25,
            useHitPoints = true,
            selectable = true,
            alwaysHaulable = true,
            comps = new List<CompProperties>
            {
                new CompProperties_Forbiddable(),
            },
            ingestible = new IngestibleProperties
            {
                drugCategory = DrugCategory.Medical,
                outcomeDoers = new List<IngestionOutcomeDoer>
                {
                    new IngestionOutcomeDoer_GiveHediff
                    {
                        hediffDef = NeuroglueHighDef,
                        severity = 1.0f,
                    },
                    new IngestionOutcomeDoer_ChangeHediffLevel
                    {
                        hediffDef = BionicShockDef,
                        levelOffset = -1,
                    },
                    new IngestionOutcomeDoer_RefillBiogel
                    {
                        amount = 25,
                    },
                },
            },
        };
        NeuroglueDef.ResolveReferences();
        DefDatabase<ThingDef>.Add(NeuroglueDef);

        UseNeuroglueJobDef = new JobDef
        {
            defName = "DMS_UseNeuroglue",
            driverClass = typeof(JobDriver_UseNeuroglue),
            reportString = "Applying neuroglue to TargetA.",
        };
        DefDatabase<JobDef>.Add(UseNeuroglueJobDef);
        BindDefOf(typeof(DMS_JobDefOf), "DMS_UseNeuroglue", UseNeuroglueJobDef);

        DefDatabase<ThingDef>.Add(SkylarkDef);
        DefDatabase<ThingDef>.Add(HumanDef);
        ShortHashGiver.GiveAllShortHashes();
    }

    private ThingDef MakePawnDef(string defName, FleshTypeDef flesh, Intelligence intelligence, bool humanlike)
    {
        RaceProperties race = new()
        {
            intelligence = intelligence,
            body = SynthBody,
            mechEnabledWorkTypes = new List<WorkTypeDef>(),
            lifeStageAges = new List<LifeStageAge>
            {
                new() { def = new LifeStageDef { defName = defName + "_Adult" }, minAge = 0f },
            },
        };
        FleshTypeField.SetValue(race, flesh);

        var comps = new List<CompProperties>();
        if (!humanlike)
        {
            comps.Add(new CompProperties_OverseerSubject());
        }

        ThingDef def = new()
        {
            defName = defName,
            label = defName,
            category = ThingCategory.Pawn,
            thingClass = typeof(Pawn),
            race = race,
            comps = comps,
            statBases = new List<StatModifier>(),
        };
        return def;
    }

    private void InitWorldAndMap()
    {
        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, new List<Map>());
        game.tickManager = new TickManager();
        game.uniqueIDsManager = new UniqueIDsManager();
        game.relationshipRecords = new RelationshipRecords();
        game.playSettings = new PlaySettings();
        Current.Game = game;

        FactionDef playerFactionDef = new() { defName = "PlayerColony", isPlayer = true };
        FactionDef hostileFactionDef = new() { defName = "Pirate", isPlayer = false };
        PlayerFaction = new Faction { def = playerFactionDef, loadID = 1 };
        HostileFaction = new Faction { def = hostileFactionDef, loadID = 2 };

        FactionManager factionManager = (FactionManager)RuntimeHelpers.GetUninitializedObject(typeof(FactionManager));
        typeof(FactionManager).GetField("ofPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(factionManager, PlayerFaction);
        typeof(FactionManager).GetField("allFactions", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(factionManager, new List<Faction> { PlayerFaction, HostileFaction });

        World world = (World)RuntimeHelpers.GetUninitializedObject(typeof(World));
        world.factionManager = factionManager;
        typeof(Game).GetField("worldInt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, world);

        Map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        Map.uniqueID = 1;
        Map.info = new MapInfo { Size = new IntVec3(MapSize, 1, MapSize), isPocketMap = true };
        game.Maps.Add(Map);
        Map.cellIndices = new CellIndices(MapSize, MapSize);
        Map.events = new MapEvents(Map);
        Map.components = new List<MapComponent>();
        Map.mapPawns = new MapPawns(Map);
        Map.listerThings = new ListerThings(ListerThingsUse.Global);
        Map.thingGrid = new ThingGrid(Map);
        Map.edificeGrid = new EdificeGrid(Map);
        Map.lordManager = new LordManager(Map);
        Map.reservationManager = new ReservationManager(Map);
        Map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
        Map.attackTargetReservationManager = new AttackTargetReservationManager(Map);
        Map.designationManager = new DesignationManager(Map);
        Map.areaManager = new AreaManager(Map);
        Map.regionDirtyer = new RegionDirtyer(Map);
        Map.regionGrid = new RegionGrid(Map);
        Map.regionAndRoomUpdater = new RegionAndRoomUpdater(Map) { Enabled = false };
        typeof(RegionAndRoomUpdater).GetField("initialized", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(Map.regionAndRoomUpdater, true);
        PathGridDef pathDef = new() { defName = "ValidationNormal" };
        PathingContext pathCtx = new(Map, new PathGrid(Map, pathDef));
        Pathing pathing = (Pathing)RuntimeHelpers.GetUninitializedObject(typeof(Pathing));
        typeof(Pathing).GetField("normal", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(pathing, pathCtx);
        typeof(Pathing).GetField("fenceBlocked", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(pathing, pathCtx);
        Map.pathing = pathing;
        Map.reachability = new Reachability(Map);

        // Populate connected District + Region for all cells except (15, 0, 15), which serves as an unreachable cell.
        District district = District.MakeNew(Map);
        Region region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
        typeof(Region).GetField("districtInt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(region, district);
        region.mapIndex = 0;
        region.valid = true;
        region.links = new List<RegionLink>();
        region.type = RegionType.Normal;
        ConnectedRegion = region;

        Region[] rawGrid = new Region[Map.cellIndices.NumGridCells];
        int blockedIdx = Map.cellIndices.CellToIndex(new IntVec3(15, 0, 15));
        for (int i = 0; i < rawGrid.Length; i++)
        {
            if (i != blockedIdx)
            {
                rawGrid[i] = region;
            }
        }
        RegionGridDirectField.SetValue(Map.regionGrid, rawGrid);
    }

    private void InitOverseer()
    {
        Overseer = CreateBasePawn(HumanDef, new IntVec3(1, 0, 1), PlayerFaction);
        Overseer.relations = new Pawn_RelationsTracker(Overseer);
        Overseer.health.AddHediff(HediffDefOf.MechlinkImplant);
        Overseer.mechanitor = new Pawn_MechanitorTracker(Overseer);
        ControlGroup = new MechanitorControlGroup(Overseer.mechanitor);
        typeof(MechanitorControlGroup).GetField("workMode", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ControlGroup, WorkModeWork);
        Overseer.mechanitor.controlGroups.Add(ControlGroup);
    }

    internal void SetControlGroupMode(MechWorkModeDef mode)
    {
        typeof(MechanitorControlGroup).GetField("workMode", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(ControlGroup, mode);
    }

    internal Pawn SpawnColonyMech(IntVec3 pos, bool playerControlled = true)
    {
        Pawn mech = CreateBasePawn(SkylarkDef, pos, PlayerFaction);
        mech.InitializeComps();
        mech.relations = new Pawn_RelationsTracker(mech);
        mech.playerSettings = new Pawn_PlayerSettings(mech);
        mech.drafter = new Pawn_DraftController(mech);

        if (playerControlled)
        {
            mech.relations.AddDirectRelation(PawnRelationDefOf.Overseer, Overseer);
            Overseer.mechanitor.ControlledPawns.Add(mech);
            ControlGroup.AssignedMechs.Add(new AssignedMech(mech));
        }

        return mech;
    }

    internal Pawn SpawnColonist(IntVec3 pos)
    {
        Pawn human = CreateBasePawn(HumanDef, pos, PlayerFaction);
        human.playerSettings = new Pawn_PlayerSettings(human);
        human.drafter = new Pawn_DraftController(human);
        return human;
    }

    private Pawn CreateBasePawn(ThingDef def, IntVec3 pos, Faction faction)
    {
        Pawn pawn = new()
        {
            def = def,
            kindDef = new PawnKindDef
            {
                defName = def.defName + "_Kind",
                label = def.defName,
                race = def,
                lifeStages = new List<PawnKindLifeStage> { new() },
            },
            thingIDNumber = nextId++,
        };
        typeof(Thing).GetField("factionInt", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(pawn, faction);
        PositionField.SetValue(pawn, pos);
        MapIndexField.SetValue(pawn, (sbyte)0);

        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        AgeCachedLifeStageField.SetValue(pawn.ageTracker, 0);
        pawn.mindState = new Pawn_MindState(pawn);
        pawn.jobs = new Pawn_JobTracker(pawn);
        pawn.carryTracker = new Pawn_CarryTracker(pawn);
        pawn.inventory = new Pawn_InventoryTracker(pawn);
        pawn.stances = new Pawn_StanceTracker(pawn);

        Map.listerThings.Add(pawn);
        Map.thingGrid.Register(pawn);
        return pawn;
    }

    internal ThingWithComps SpawnNeuroglue(IntVec3 pos, int count = 10, bool forbidden = false)
    {
        ThingWithComps gel = (ThingWithComps)ThingMaker.MakeThing(NeuroglueDef);
        gel.thingIDNumber = nextId++;
        gel.stackCount = count;
        PositionField.SetValue(gel, pos);
        MapIndexField.SetValue(gel, (sbyte)0);
        Map.listerThings.Add(gel);
        Map.thingGrid.Register(gel);
        if (forbidden)
        {
            typeof(CompForbiddable).GetField("forbiddenInt", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(gel.GetComp<CompForbiddable>(), true);
        }

        return gel;
    }

    internal Thing TakeOneToCarry(Pawn pawn, ThingWithComps shelfStack)
    {
        if (shelfStack.stackCount > 1)
        {
            shelfStack.stackCount--;
        }
        else
        {
            DespawnThing(shelfStack);
        }

        Thing dose = ThingMaker.MakeThing(NeuroglueDef);
        dose.stackCount = 1;
        pawn.carryTracker.innerContainer.TryAdd(dose);
        return dose;
    }

    internal void DespawnThing(Thing thing)
    {
        if (thing.Spawned)
        {
            Map.thingGrid.Deregister(thing);
            Map.listerThings.Remove(thing);
            MapIndexField.SetValue(thing, (sbyte)(-1));
        }
    }

    internal Hediff_BiogelReservoir EnsureReservoirWithFuel(Pawn pawn, float fuel)
    {
        Hediff_BiogelReservoir res = Hediff_BiogelReservoir.Ensure(pawn);
        FieldInfo reserveField = typeof(Hediff_BiogelReservoir).GetField("reserve", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object reserve = reserveField.GetValue(res)!;
        FieldInfo ticksField = reserve.GetType().GetField("remainingFuelTicks", BindingFlags.NonPublic | BindingFlags.Instance)!;
        long targetTicks = (long)Math.Round(Math.Clamp(fuel, 0f, 100f) * 60000.0);
        ticksField.SetValue(reserve, targetTicks);
        return res;
    }

    internal void AddDegenerativeToParts(Pawn pawn, int partCount)
    {
        int added = 0;
        for (int i = 0; i < Parts.Count && added < partCount; i++)
        {
            Hediff h = HediffMaker.MakeHediff(DegenerativeDef, pawn, Parts[i]);
            h.Severity = 0.35f;
            pawn.health.AddHediff(h, Parts[i]);
            added++;
        }
    }

    internal static void TickHediffsOnce(Pawn pawn)
    {
        List<Hediff> snapshot = pawn.health.hediffSet.hediffs.ToList();
        for (int i = 0; i < snapshot.Count; i++)
        {
            Hediff h = snapshot[i];
            if (pawn.health.hediffSet.hediffs.Contains(h))
            {
                h.TickInterval(1);
                if (pawn.health.hediffSet.hediffs.Contains(h))
                {
                    h.PostTickInterval(1);
                }
            }
        }
    }

    internal static Toil BuildFinalUseNeuroglueToil(Pawn actor, Pawn recipient, Thing gelOnFloor, out JobDriver_UseNeuroglue driver)
    {
        Job job = JobMaker.MakeJob(DMS_JobDefOf.DMS_UseNeuroglue, recipient, gelOnFloor);
        job.count = 1;

        driver = new JobDriver_UseNeuroglue
        {
            pawn = actor,
            job = job,
        };

        MethodInfo makeNewToils = typeof(JobDriver_UseNeuroglue)
            .GetMethod("MakeNewToils", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IEnumerable<Toil> toils = (IEnumerable<Toil>)makeNewToils.Invoke(driver, null)!;
        return toils.Last();
    }

    private static T GetOrAddDef<T>(string defName) where T : Def, new()
    {
        T? existing = DefDatabase<T>.GetNamedSilentFail(defName);
        if (existing != null)
        {
            return existing;
        }

        T created = new() { defName = defName, label = defName };
        DefDatabase<T>.Add(created);
        return created;
    }

    private static HediffDef RegisterHediff(HediffDef def)
    {
        DefDatabase<HediffDef>.Add(def);
        return def;
    }

    private static void BindStat(string defName, float baseValue)
    {
        StatDef stat = new()
        {
            defName = defName,
            label = defName,
            defaultBaseValue = baseValue,
            minValue = 0f,
            maxValue = 100000f,
            supressDisabledError = true,
            workerClass = typeof(StatWorker),
        };
        DefDatabase<StatDef>.Add(stat);
        BindDefOf(typeof(StatDefOf), defName, stat);
    }

    private static void BindDefOf(Type defOfType, string fieldName, Def value)
    {
        FieldInfo? field = defOfType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        field?.SetValue(null, value);
    }
}
