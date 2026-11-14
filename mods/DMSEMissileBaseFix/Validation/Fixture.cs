using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DMSE;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace DMSEMissileBaseFixValidation;

/// <summary>
/// Real RimWorld/DMSE objects with small headless UI, rendering and pathfinding boundaries.
/// No method under test is replaced by a fake result.
/// </summary>
internal sealed class SiteFixture
{
    private static readonly BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static bool headlessInstalled;
    private static int nextId = 10000;

    internal static readonly List<string> MessagesSeen = new();
    internal static Window? LastWindow;

    internal Game Game = null!;
    internal World World = null!;
    internal Faction Player = null!;
    internal Faction Enemy = null!;
    internal Site Site = null!;
    internal Map Map = null!;
    internal ThingDef MissileDef = null!;

    internal FormCaravanComp Form => Site.GetComponent<FormCaravanComp>();
    internal WorldObjectComp_MissileBase MissileComp => Site.GetComponent<WorldObjectComp_MissileBase>();

    internal static void InstallHeadlessBoundary()
    {
        if (headlessInstalled)
        {
            return;
        }

        headlessInstalled = true;
        Set(typeof(UnityData), "mainThreadId", null, Thread.CurrentThread.ManagedThreadId);
        Harmony harmony = new("validation.dmse.missilebasefix.headless");

        Patch(harmony, typeof(MatLoader), "LoadMat", typeof(Headless), nameof(Headless.NoMaterial),
            typeof(string), typeof(int));
        Patch(harmony, typeof(MaterialPool), nameof(MaterialPool.MatFrom), typeof(Headless), nameof(Headless.NoMaterial),
            typeof(MaterialRequest));
        Patch(harmony, typeof(SolidColorMaterials), nameof(SolidColorMaterials.SimpleSolidColorMaterial),
            typeof(Headless), nameof(Headless.NoMaterial), typeof(Color), typeof(bool));
        Patch(harmony, typeof(Graphic_Single), nameof(Graphic_Single.Init), typeof(Headless), nameof(Headless.Skip),
            typeof(GraphicRequest));
        Patch(harmony, typeof(FloatMenuOption), nameof(FloatMenuOption.SetSizeMode), typeof(Headless), nameof(Headless.Skip),
            typeof(FloatMenuSizeMode));
        Patch(harmony, typeof(Translator), nameof(Translator.TryTranslate), typeof(Headless), nameof(Headless.Translate),
            typeof(string), typeof(TaggedString).MakeByRefType());
        Patch(harmony, typeof(Messages), nameof(Messages.Message), typeof(Headless), nameof(Headless.RecordMessage),
            typeof(Message), typeof(bool));
        Patch(harmony, typeof(WindowStack), nameof(WindowStack.Add), typeof(Headless), nameof(Headless.RecordWindow),
            typeof(Window));
        Patch(harmony, typeof(ContentFinder<Texture2D>), "Get", typeof(Headless), nameof(Headless.NoTexture),
            typeof(string), typeof(bool));
        Patch(harmony, typeof(ThingWithComps), nameof(ThingWithComps.SpawnSetup), typeof(Headless), nameof(Headless.SpawnSetup),
            typeof(Map), typeof(bool));
        Patch(harmony, typeof(ThingWithComps), nameof(ThingWithComps.DeSpawn), typeof(Headless), nameof(Headless.DeSpawn),
            typeof(DestroyMode));
        Patch(harmony, typeof(GenGrid), nameof(GenGrid.Walkable), typeof(Headless), nameof(Headless.Reachable),
            typeof(IntVec3), typeof(Map));
        Patch(harmony, typeof(StatExtension), nameof(StatExtension.GetStatValue), typeof(Headless), nameof(Headless.CarryingCapacity),
            typeof(Thing), typeof(StatDef), typeof(bool), typeof(int));
        Patch(harmony, typeof(Reachability), nameof(Reachability.CanReach), typeof(Headless), nameof(Headless.Reachable),
            typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms));
        Patch(harmony, typeof(GenClosest), nameof(GenClosest.RegionwiseBFSWorker), typeof(Headless), nameof(Headless.SkipRegionSearch));
    }

    private static void Patch(Harmony harmony, Type type, string name, Type hookType, string hookName, params Type[]? args)
    {
        MethodInfo? target = args == null || args.Length == 0
            ? AccessTools.Method(type, name)
            : AccessTools.Method(type, name, args);
        if (target == null)
        {
            throw new MissingMethodException(type.FullName, name);
        }

        harmony.Patch(target, prefix: new HarmonyMethod(hookType, hookName));
    }

    internal static SiteFixture Create(string defName = "DMSE_MissileBase", bool missileBase = true, bool withPlayer = true)
    {
        InstallHeadlessBoundary();
        SiteFixture fixture = new();
        fixture.Build(defName, missileBase, withPlayer);
        return fixture;
    }

    private void Build(string defName, bool missileBase, bool withPlayer)
    {
        Current.ProgramState = ProgramState.Entry;
        Set(typeof(Prefs), "data", null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        EnsureEngineDefs();
        MessagesSeen.Clear();
        LastWindow = null;

        WorldGrid grid = NewWorldGrid();
        World = new World
        {
            info = new WorldInfo { seedString = "dmse-missile-base-validation" },
            grid = grid,
            worldObjects = new WorldObjectsHolder(),
            components = new List<WorldComponent>(),
            UI = new WorldInterface(),
        };

        FactionManager factionManager = new();
        World.factionManager = factionManager;

        Game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Set(typeof(Game), "maps", Game, new List<Map>());
        Game.tickManager = new TickManager { gameStartAbsTick = 0 };
        Game.uniqueIDsManager = new UniqueIDsManager();
        Game.World = World;
        Game.letterStack = new LetterStack();
        Current.Game = Game;
        World.pathGrid = new WorldPathGrid();

        Root_Play root = (Root_Play)RuntimeHelpers.GetUninitializedObject(typeof(Root_Play));
        UIRoot_Play ui = (UIRoot_Play)RuntimeHelpers.GetUninitializedObject(typeof(UIRoot_Play));
        ui.windows = new WindowStack();
        ui.mapUI = (MapInterface)RuntimeHelpers.GetUninitializedObject(typeof(MapInterface));
        ui.mapUI.selector = new Selector();
        root.uiRoot = ui;
        Set(typeof(Current), "rootInt", null, root);
        LoadedLanguage language = (LoadedLanguage)RuntimeHelpers.GetUninitializedObject(typeof(LoadedLanguage));
        Set(typeof(LoadedLanguage), "workerInt", language, new LanguageWorker_English());
        LanguageDatabase.activeLanguage = language;

        Player = NewFaction("ValidationPlayer", isPlayer: true);
        Enemy = NewFaction("ValidationEnemy", isPlayer: false);
        SetHostile(Player, Enemy);
        SetHostile(Enemy, Player);
        factionManager.AllFactionsListForReading.Add(Player);
        factionManager.AllFactionsListForReading.Add(Enemy);
        Set(typeof(FactionManager), "ofPlayer", factionManager, Player);

        WorldObjectDef def = new()
        {
            defName = defName,
            label = defName,
            description = "validation site",
            worldObjectClass = typeof(Site),
            canHaveFaction = true,
            validLaunchTarget = false,
            mapGenerator = new MapGeneratorDef { defName = "ValidationSurfaceMap" },
            comps = new List<WorldObjectCompProperties> { new WorldObjectCompProperties_FormCaravan() },
        };
        if (missileBase)
        {
            def.comps.Add(new CompProperties_MissileBase());
        }

        Site = (Site)WorldObjectMaker.MakeWorldObject(def);
        Site.Tile = grid.Surface.Tiles[0].tile;
        Site.SetFaction(Enemy);
        SitePartDef partDef = new()
        {
            defName = defName + "Part",
            label = defName,
            description = "validation site part",
        };
        Site.parts.Add(new SitePart(Site, partDef, new SitePartParams()));
        RegisterWorldObject(Site);

        EnsureEngineDefs();
        Map = new Map();
        Map.info.Size = new IntVec3(50, 1, 50);
        Map.info.parent = Site;
        Map.generatorDef = def.mapGenerator;
        Map.cellIndices = new CellIndices(Map);
        Map.edificeGrid = new EdificeGrid(Map);
        Map.terrainGrid = new TerrainGrid(Map);
        Array.Fill(Map.terrainGrid.topGrid, new TerrainDef
        {
            defName = "ValidationGround",
            passability = Traversability.Standable,
            affordances = new List<TerrainAffordanceDef>(),
        });
        Map.components = new List<MapComponent>();
        Map.mapPawns = new MapPawns(Map);
        Map.events = new MapEvents(Map);
        Map.listerThings = new ListerThings(ListerThingsUse.Global);
        Map.listerBuildings = new ListerBuildings();
        Map.haulDestinationManager = new HaulDestinationManager(Map);
        Map.listerHaulables = new ListerHaulables(Map);
        Map.listerMergeables = new ListerMergeables(Map);
        Map.attackTargetsCache = new AttackTargetsCache(Map);
        Map.thingGrid = new ThingGrid(Map);
        Map.spawnedThings = new ThingOwner<Thing>(Map, false, LookMode.Reference);
        Map.reservationManager = new ReservationManager(Map);
        Map.resourceCounter = new ResourceCounter(Map);
        Map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
        Map.reachability = new Reachability(Map);
        Map.fogGrid = new FogGrid(Map);
        Map.exitMapGrid = new ExitMapGrid(Map);
        Map.lordManager = new LordManager(Map);
        EnsureEngineDefs();
        Map.designationManager = new DesignationManager(Map);
        Map.areaManager = new AreaManager(Map);
        Map.regionGrid = new RegionGrid(Map);
        Map.regionDirtyer = new RegionDirtyer(Map);
        Map.regionAndRoomUpdater = new RegionAndRoomUpdater(Map) { Enabled = false };
        Game.Maps.Add(Map);
        Game.currentMapIndex = 0;

        MissileDef = new ThingDef
        {
            defName = "DMSE_Validation_InterceptorMissile",
            label = "validation interceptor",
            thingClass = typeof(ThingWithComps),
            category = ThingCategory.Item,
            alwaysHaulable = true,
            canLoadIntoCaravan = true,
            selectable = true,
            stackLimit = 1,
            drawerType = DrawerType.MapMeshOnly,
            useHitPoints = false,
            modExtensions = new List<DefModExtension>
            {
                new MissileRoleExtension { role = MissileRole.Interceptor },
            },
            comps = new List<CompProperties> { new CompProperties_Forbiddable() },
        };

        if (withPlayer)
        {
            AddPlayerPawn(new IntVec3(5, 0, 5));
        }
    }

    private static void EnsureEngineDefs()
    {
        if (DefDatabase<DesignationDef>.DefCount == 0)
        {
            DefDatabase<DesignationDef>.Add(new DesignationDef { defName = "ValidationDesignation" });
        }
        if (DefDatabase<ReservationLayerDef>.DefCount == 0)
        {
            DefDatabase<ReservationLayerDef>.Add(new ReservationLayerDef { defName = "ValidationReservationLayer" });
        }

        if (StatDefOf.CarryingCapacity == null)
        {
            StatDef capacity = new() { defName = "CarryingCapacity", defaultBaseValue = 100f };
            DefDatabase<StatDef>.Add(capacity);
            Set(typeof(StatDefOf), nameof(StatDefOf.CarryingCapacity), null, capacity);
        }
    }

    private static WorldGrid NewWorldGrid()
    {
        WorldGrid grid = new();
        PlanetLayerDef surfaceDef = new()
        {
            defName = "ValidationSurface",
            label = "validation surface",
            layerType = typeof(SurfaceLayer),
            tileType = typeof(Tile),
            worldDrawLayers = new List<Type>(),
        };

        PlanetLayer surface = grid.RegisterPlanetLayer(
            surfaceDef,
            origin: Vector3.zero,
            radius: 100f,
            viewAngle: 180f,
            extraCameraAltitude: 0f,
            subdivisions: 1);
        Set(typeof(WorldGrid), "surface", grid, surface);

        BiomeDef biome = new() { defName = "ValidationBiome", label = "validation biome", inVacuum = false };
        for (int i = 0; i < surface.TilesCount; i++)
        {
            Tile tile = new(surface.PlanetTileForID(i)) { PrimaryBiome = biome };
            surface.Tiles.Add(tile);
        }

        return grid;
    }

    private static Faction NewFaction(string name, bool isPlayer)
    {
        Faction faction = new();
        faction.def = new FactionDef
        {
            defName = name,
            label = name,
            isPlayer = isPlayer,
            humanlikeFaction = true,
            techLevel = TechLevel.Spacer,
        };
        faction.loadID = isPlayer ? 1 : 2;
        Set(typeof(Faction), "name", faction, name);
        Set(typeof(Faction), "relations", faction, new List<FactionRelation>());
        return faction;
    }

    private static void SetHostile(Faction from, Faction other)
    {
        List<FactionRelation> relations = (List<FactionRelation>)typeof(Faction)
            .GetField("relations", InstanceFields)!.GetValue(from)!;
        relations.Add(new FactionRelation
        {
            other = other,
            kind = FactionRelationKind.Hostile,
            baseGoodwill = -100,
        });
    }

    private void RegisterWorldObject(WorldObject value)
    {
        World.worldObjects.AllWorldObjects.Add(value);
        SetHashSet("worldObjectsHashSet").Add(value);
        SetList<MapParent>("mapParents").Add(Site);
        SetList<Site>("sites").Add(Site);
    }

    private HashSet<WorldObject> SetHashSet(string field)
    {
        return (HashSet<WorldObject>)typeof(WorldObjectsHolder).GetField(field, InstanceFields)!.GetValue(World.worldObjects)!;
    }

    private List<T> SetList<T>(string field)
    {
        return (List<T>)typeof(WorldObjectsHolder).GetField(field, InstanceFields)!.GetValue(World.worldObjects)!;
    }

    private void RegisterThing(Thing thing, Faction faction, IntVec3 position)
    {
        Set(typeof(Thing), "factionInt", thing, faction);
        Set(typeof(Thing), "positionInt", thing, position);
        Set(typeof(Thing), "mapIndexOrState", thing, (sbyte)Game.Maps.IndexOf(Map));
        Map.spawnedThings.TryAdd(thing, canMergeWithExistingStacks: false);
        Map.listerThings.Add(thing);
        Map.thingGrid.Register(thing);
        if (thing is Building building)
        {
            Map.listerBuildings.Add(building);
        }
        Map.attackTargetsCache.Notify_ThingSpawned(thing);
    }

    internal Pawn AddPlayerPawn(IntVec3 position)
    {
        Pawn pawn = NewPawn(Player, position, dormant: false);
        Map.mapPawns.RegisterPawn(pawn);
        return pawn;
    }

    internal Pawn AddDormantEnemy(IntVec3 position, bool fogged)
    {
        Pawn pawn = NewPawn(Enemy, position, dormant: true);
        Map.mapPawns.RegisterPawn(pawn);
        Map.attackTargetsCache.Notify_ThingSpawned(pawn);
        if (fogged)
        {
            Map.fogGrid.Refog(CellRect.SingleCell(position));
        }
        return pawn;
    }

    private Pawn NewPawn(Faction faction, IntVec3 position, bool dormant)
    {
        Pawn pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = new ThingDef
        {
            defName = "ValidationPawn" + nextId,
            label = "validation pawn",
            category = ThingCategory.Pawn,
            thingClass = typeof(Pawn),
            useHitPoints = false,
            race = new RaceProperties { intelligence = Intelligence.Humanlike, baseBodySize = 1f },
        };
        pawn.kindDef = new PawnKindDef { defName = "ValidationKind" + nextId, race = pawn.def };
        pawn.thingIDNumber = nextId++;
        pawn.stackCount = 1;
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.mindState = new Pawn_MindState(pawn);
        pawn.jobs = new Pawn_JobTracker(pawn);
        pawn.inventory = new Pawn_InventoryTracker(pawn);
        pawn.equipment = new Pawn_EquipmentTracker(pawn);
        pawn.carryTracker = new Pawn_CarryTracker(pawn);
        pawn.drafter = new Pawn_DraftController(pawn);
        pawn.stances = new Pawn_StanceTracker(pawn);
        Set(typeof(Thing), "factionInt", pawn, faction);
        Set(typeof(Thing), "positionInt", pawn, position);
        Set(typeof(Thing), "mapIndexOrState", pawn, (sbyte)Game.Maps.IndexOf(Map));

        if (dormant)
        {
            Set(typeof(ThingWithComps), "comps", pawn, new List<ThingComp>());
            CompCanBeDormant comp = new() { parent = pawn };
            comp.Initialize(new CompProperties_CanBeDormant { startsDormant = true });
            pawn.AllComps.Add(comp);
        }

        return pawn;
    }

    internal Building_TurretGun AddTurret(
        string defName,
        float capacity,
        float initialFuel,
        float minimumThreshold,
        bool powerOn,
        bool fieldAirDefense = false,
        int storedMissiles = 0,
        bool mannable = false)
    {
        ThingDef gunDef = new()
        {
            defName = defName + "Gun",
            label = "validation gun",
            thingClass = typeof(ThingWithComps),
            category = ThingCategory.Item,
            useHitPoints = false,
            stackLimit = 1,
            comps = new List<CompProperties>
            {
                new() { compClass = typeof(CompEquippable) },
            },
        };
        Set(typeof(ThingDef), "verbs", gunDef, new List<VerbProperties>
        {
            new()
            {
                verbClass = typeof(Verb_Shoot),
                consumeFuelPerShot = 1f,
                range = 30f,
                defaultProjectile = new ThingDef
                {
                    defName = defName + "Projectile",
                    thingClass = typeof(Projectile),
                    category = ThingCategory.Projectile,
                    useHitPoints = false,
                },
            },
        });

        ThingDef def = new()
        {
            defName = defName,
            label = defName,
            thingClass = typeof(Building_TurretGun),
            category = ThingCategory.Building,
            useHitPoints = false,
            building = new BuildingProperties
            {
                turretGunDef = gunDef,
                fixedStorageSettings = new StorageSettings(),
                defaultStorageSettings = new StorageSettings(),
            },
            comps = new List<CompProperties>
            {
                new CompProperties_Refuelable
                {
                    fuelCapacity = capacity,
                    initialFuelPercent = initialFuel,
                    minimumFueledThreshold = minimumThreshold,
                    consumeFuelOnlyWhenUsed = true,
                },
                new CompProperties_Power { compClass = typeof(CompPowerTrader) },
            },
        };

        if (fieldAirDefense)
        {
            def.comps.Add(new CompProperties_MissileRackStorage());
            def.comps.Add(new CompProperties_MissileLauncher());
            def.comps.Add(new CompProperties_TerminalCIWS { fuelPerShot = 1f });
        }
        if (mannable)
        {
            def.comps.Add(new CompProperties_Mannable());
        }

        foreach (CompProperties comp in def.comps)
        {
            if (comp is CompProperties_Power powerProps)
            {
                Set(typeof(CompProperties_Power), "basePowerConsumption", powerProps, 100f);
            }
        }

        Building_TurretGun turret = (Building_TurretGun)ThingMaker.MakeThing(def);
        turret.refuelableComp = turret.GetComp<CompRefuelable>();
        Set(typeof(Building_TurretGun), "powerComp", turret, turret.GetComp<CompPowerTrader>());
        Set(typeof(Building_TurretGun), "mannableComp", turret, turret.GetComp<CompMannable>());
        SetPower(turret, powerOn);
        RegisterThing(turret, Enemy, new IntVec3(15, 0, 15));

        CompMissileRackStorage? storage = turret.GetComp<CompMissileRackStorage>();
        if (storage != null)
        {
            for (int i = 0; i < storedMissiles; i++)
            {
                if (!storage.HeldContainer.TryAdd(NewMissile(), canMergeWithExistingStacks: false))
                {
                    throw new InvalidOperationException("Could not fill validation turret storage.");
                }
            }
        }

        return turret;
    }

    internal Building AddCompStorageBuilding(string defName, int storedMissiles, bool hostile = true)
    {
        ThingDef def = new()
        {
            defName = defName,
            label = defName,
            thingClass = typeof(Building),
            category = ThingCategory.Building,
            useHitPoints = false,
            building = new BuildingProperties
            {
                fixedStorageSettings = new StorageSettings(),
                defaultStorageSettings = new StorageSettings(),
            },
            comps = new List<CompProperties> { new CompProperties_MissileRackStorage() },
        };
        Building building = (Building)ThingMaker.MakeThing(def);
        RegisterThing(building, hostile ? Enemy : Player, new IntVec3(20, 0, 20));
        CompMissileRackStorage storage = building.GetComp<CompMissileRackStorage>();
        for (int i = 0; i < storedMissiles; i++)
        {
            storage.HeldContainer.TryAdd(NewMissile(), canMergeWithExistingStacks: false);
        }
        return building;
    }

    internal Building_MissileRack AddRack(string defName, int storedMissiles)
    {
        ThingDef def = new()
        {
            defName = defName,
            label = defName,
            thingClass = typeof(Building_MissileRack),
            category = ThingCategory.Building,
            useHitPoints = false,
            building = new BuildingProperties
            {
                fixedStorageSettings = new StorageSettings(),
                defaultStorageSettings = new StorageSettings(),
            },
            comps = new List<CompProperties>(),
        };
        Building_MissileRack rack = (Building_MissileRack)ThingMaker.MakeThing(def);
        RegisterThing(rack, Enemy, new IntVec3(25, 0, 25));
        for (int i = 0; i < storedMissiles; i++)
        {
            rack.GetDirectlyHeldThings().TryAdd(NewMissile(), canMergeWithExistingStacks: false);
        }
        return rack;
    }

    internal Thing NewMissile()
    {
        Thing missile = ThingMaker.MakeThing(MissileDef);
        missile.stackCount = 1;
        return missile;
    }

    internal Thing AddGroundMissile(IntVec3 position)
    {
        Thing missile = NewMissile();
        GenSpawn.Spawn(missile, position, Map);
        return missile;
    }

    internal CompTransporter AddTransporter()
    {
        ThingDef def = new()
        {
            defName = "ValidationTransporter",
            label = "validation transporter",
            thingClass = typeof(Building),
            category = ThingCategory.Building,
            useHitPoints = false,
            building = new BuildingProperties(),
            comps = new List<CompProperties>(),
        };
        Building building = (Building)ThingMaker.MakeThing(def);
        Set(typeof(ThingWithComps), "comps", building, new List<ThingComp>());
        CompTransporter transporter = new() { parent = building };
        transporter.Initialize(new CompProperties_Transporter { canChangeAssignedThingsAfterStarting = true });
        transporter.innerContainer = new ThingOwner<Thing>(transporter);
        transporter.groupID = 1;
        building.AllComps.Add(transporter);
        RegisterThing(building, Player, new IntVec3(6, 0, 6));
        return transporter;
    }

    internal Lord MakeCaravanLord(Pawn pawn, Thing item)
    {
        TransferableOneWay transferable = new();
        transferable.things.Add(item);
        transferable.AdjustTo(1);

        LordJob_FormAndSendCaravan job = new(new List<TransferableOneWay> { transferable }, new List<Pawn>(),
            new IntVec3(5, 0, 5), new IntVec3(1, 0, 1), Site.Tile, Site.Tile);
        Lord lord = new();
        lord.lordManager = Map.lordManager;
        lord.faction = Player;
        lord.ownedPawns.Add(pawn);
        Set(typeof(Lord), "curJob", lord, job);
        job.lord = lord;
        return lord;
    }

    internal void SetPower(Building_TurretGun turret, bool powerOn)
    {
        CompPowerTrader? power = turret.GetComp<CompPowerTrader>();
        if (power != null)
        {
            Set(typeof(CompPowerTrader), "powerOnInt", power, powerOn);
        }
    }

    internal static void SetFuel(CompRefuelable comp, float fuel)
    {
        Set(typeof(CompRefuelable), "fuel", comp, fuel);
    }

    internal void SetBaseDestroyed(bool destroyed)
    {
        MapComponent_MissileBase? component = Map.GetComponent<MapComponent_MissileBase>();
        if (component == null)
        {
            component = new MapComponent_MissileBase(Map);
            Map.components.Add(component);
        }
        Set(typeof(MapComponent_MissileBase), "baseSiteDestroyed", component, destroyed);
    }

    internal void SetReentryBlockedUntil(int tick)
    {
        Set(typeof(WorldObjectComp_MissileBase), "reentryBlockedUntil", MissileComp, tick);
    }

    internal void SetLocalMapAssociation(bool associated)
    {
        Map.info.parent = associated ? Site : null;
    }

    internal CompLaunchable AddLaunchable()
    {
        ThingDef def = new()
        {
            defName = "ValidationLaunchable",
            label = "validation launchable",
            thingClass = typeof(Building),
            category = ThingCategory.Building,
            useHitPoints = false,
            building = new BuildingProperties(),
            comps = new List<CompProperties>(),
        };
        Building building = (Building)ThingMaker.MakeThing(def);
        Set(typeof(ThingWithComps), "comps", building, new List<ThingComp>());
        CompLaunchable launchable = new() { parent = building };
        launchable.Initialize(new CompProperties_Launchable());
        building.AllComps.Add(launchable);
        RegisterThing(building, Player, new IntVec3(7, 0, 7));
        return launchable;
    }

    internal void AddWorldAirDefense()
    {
        WorldObjectComp_AirDefense comp = new() { parent = Site };
        comp.Initialize(new CompProperties_WorldAirDefense());
        Site.AllComps.Add(comp);
    }

    internal static void Set(Type type, string field, object? instance, object? value)
    {
        FieldInfo? info = AccessTools.Field(type, field);
        if (info == null)
        {
            throw new MissingFieldException(type.FullName, field);
        }
        info.SetValue(instance, value);
    }

    private static class Headless
    {
        internal static bool Skip()
        {
            return false;
        }

        internal static bool NoMaterial(ref Material? __result)
        {
            __result = null;
            return false;
        }

        internal static bool NoTexture(ref Texture2D? __result)
        {
            __result = null;
            return false;
        }

        internal static bool Translate(string key, out TaggedString result, ref bool __result)
        {
            result = key;
            __result = true;
            return false;
        }

        internal static bool RecordMessage(Message msg)
        {
            MessagesSeen.Add(msg.text);
            return false;
        }

        internal static bool RecordWindow(Window window)
        {
            LastWindow = window;
            return false;
        }

        internal static bool SpawnSetup(ThingWithComps __instance, Map map)
        {
            int mapIndex = Find.Maps.IndexOf(map);
            Set(typeof(Thing), "mapIndexOrState", __instance, (sbyte)mapIndex);
            map.spawnedThings.TryAdd(__instance, canMergeWithExistingStacks: false);
            map.listerThings.Add(__instance);
            map.thingGrid.Register(__instance);
            if (__instance is Building building)
            {
                map.listerBuildings.Add(building);
            }
            map.attackTargetsCache.Notify_ThingSpawned(__instance);
            return false;
        }

        internal static bool DeSpawn(ThingWithComps __instance)
        {
            Map map = __instance.Map;
            map.spawnedThings.Remove(__instance);
            map.listerThings.Remove(__instance);
            map.thingGrid.Deregister(__instance);
            map.listerHaulables.Notify_DeSpawned(__instance);
            map.listerMergeables.Notify_DeSpawned(__instance);
            if (__instance is Building building)
            {
                map.listerBuildings.Remove(building);
            }
            map.attackTargetsCache.Notify_ThingDespawned(__instance);
            Set(typeof(Thing), "mapIndexOrState", __instance, (sbyte)-1);
            return false;
        }

        internal static bool CarryingCapacity(StatDef stat, ref float __result)
        {
            if (stat != StatDefOf.CarryingCapacity)
            {
                return true;
            }
            __result = 100f;
            return false;
        }

        internal static bool Reachable(ref bool __result)
        {
            __result = true;
            return false;
        }

        internal static bool SkipRegionSearch(ref Thing? __result, out int regionsSeen)
        {
            __result = null;
            regionsSeen = 30;
            return false;
        }
    }
}
