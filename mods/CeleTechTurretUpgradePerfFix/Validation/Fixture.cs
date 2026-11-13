using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CeleTech.Base;
using RimWorld;
using Verse;
using Verse.AI;

namespace CeleTechTurretUpgradePerfFix.Validation;

/// <summary>
/// Offline RimWorld 1.7 fixture: real Map/ListerThings/Thing graph, real CeleTech.Base types, and a
/// BuildingArtificial lister containing 5,000 walls plus CeleTech turrets with real Comp_TurretUpgrade.
/// No game loop, no XML loading and no Unity scene.
/// </summary>
internal sealed class Fixture
{
    internal const int WallCount = 5_000;

    private static readonly FieldInfo MapIndexField =
        typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo PositionField =
        typeof(Thing).GetField("positionInt", BindingFlags.NonPublic | BindingFlags.Instance)!;

    internal Map Map = null!;
    internal ThingDef WallDef = null!;
    internal ThingDef TurretDef = null!;
    internal ThingDef PawnDef = null!;
    internal TurretUpgradeDef Upgrade = null!;
    internal TurretUpgradeDef Upgrade2 = null!;
    internal TurretUpgradeDef Upgrade3 = null!;
    internal JobDef TurretUpgradeJob = null!;
    internal WorkGiver_TurretUpgrade Giver = null!;
    internal Pawn Pawn = null!;
    internal readonly List<Building> Walls = new();

    private int nextThingId = 1;

    internal static Fixture Create()
    {
        Fixture fixture = new();
        fixture.CreateWorld();
        fixture.CreateDefs();
        fixture.Pawn = fixture.NewPawn(0, new IntVec3(50, 0, 25));
        fixture.Giver = new WorkGiver_TurretUpgrade
        {
            def = new WorkGiverDef
            {
                defName = "Validation_TurretUpgrade",
                giverClass = typeof(WorkGiver_TurretUpgrade),
                workType = null,
                workTags = WorkTags.None,
                nonColonistsCanDo = true,
                scanThings = true,
                requiredCapacities = new List<PawnCapacityDef>(),
            },
        };
        fixture.AddWalls();
        return fixture;
    }

    internal Building AddTurret(Map map, bool enqueue, bool postSpawnSetup)
    {
        Building turret = NewBuilding(TurretDef, map, new IntVec3(1, 0, 1));
        Comp_TurretUpgrade comp = turret.GetComp<Comp_TurretUpgrade>();
        if (enqueue)
        {
            comp.Enqueue(Upgrade);
        }

        if (postSpawnSetup)
        {
            comp.PostSpawnSetup(false);
        }

        return turret;
    }

    internal Map CreateAdditionalMap()
    {
        Map map = NewMap(Current.Game, uniqueID: 2, width: 60, height: 30);
        return map;
    }

    internal Pawn NewPawn(int mapIndex, IntVec3 position)
    {
        Pawn pawn = new() { def = PawnDef, thingIDNumber = nextThingId++ };
        pawn.kindDef = new PawnKindDef { defName = "ValidationPawnKind" };
        SetPosition(pawn, position);
        SetMap(pawn, mapIndex);
        return pawn;
    }

    internal void SetSpawned(Thing thing, int mapIndex)
    {
        SetMap(thing, mapIndex);
    }

    internal void SetDespawned(Thing thing)
    {
        SetMap(thing, -1);
    }

    internal void SetDestroyed(Thing thing)
    {
        SetMap(thing, -3);
    }

    private void CreateWorld()
    {
        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, new List<Map>());
        game.tickManager = new TickManager();
        game.questManager = new QuestManager();
        game.uniqueIDsManager = new UniqueIDsManager();
        Current.Game = game;

        Map = NewMap(game, uniqueID: 1, width: 100, height: 50);
    }

    private static Map NewMap(Game game, int uniqueID, int width, int height)
    {
        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.uniqueID = uniqueID;
        map.info = new MapInfo { Size = new IntVec3(width, 1, height), isPocketMap = true };
        map.cellIndices = new CellIndices(width, height);
        map.components = new List<MapComponent>();
        map.mapPawns = new MapPawns(map);
        map.listerThings = new ListerThings(ListerThingsUse.Global);
        map.reservationManager = new ReservationManager(map);
        map.physicalInteractionReservationManager = new PhysicalInteractionReservationManager();
        game.Maps.Add(map);
        return map;
    }

    private void CreateDefs()
    {
        WallDef = NewBuildingDef("ValidationWall");
        TurretDef = NewBuildingDef("ValidationTurret");
        TurretDef.comps.Add(new CompProperties_TurretUpgrade());

        RaceProperties race = new()
        {
            intelligence = Intelligence.Humanlike,
        };
        typeof(RaceProperties).GetField("fleshType", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(race, new FleshTypeDef { defName = "ValidationFlesh" });

        PawnDef = new ThingDef
        {
            defName = "ValidationPawn",
            category = ThingCategory.Pawn,
            thingClass = typeof(Pawn),
            race = race,
        };

        Upgrade = NewUpgrade("ValidationTurretUpgrade");
        Upgrade2 = NewUpgrade("ValidationTurretUpgrade2");
        Upgrade3 = NewUpgrade("ValidationTurretUpgrade3");

        TurretUpgradeJob = new JobDef
        {
            defName = "Job_TurretUpgrade",
            driverClass = typeof(JobDriver_TurretUpgrade),
        };
        DefDatabase<JobDef>.Add(TurretUpgradeJob);
    }

    private void AddWalls()
    {
        for (int i = 0; i < WallCount; i++)
        {
            Building wall = NewBuilding(WallDef, Map, new IntVec3(i % 100, 0, i / 100));
            Walls.Add(wall);
        }
    }

    private Building NewBuilding(ThingDef def, Map map, IntVec3 position)
    {
        Building building = new() { def = def, thingIDNumber = nextThingId++ };
        building.InitializeComps();
        SetPosition(building, position);
        SetMap(building, Current.Game.Maps.IndexOf(map));
        map.listerThings.Add(building);
        return building;
    }

    private static TurretUpgradeDef NewUpgrade(string defName)
    {
        return new TurretUpgradeDef
        {
            defName = defName,
            costThing = null,
            costCount = 0,
            workTicks = 1,
        };
    }

    private static ThingDef NewBuildingDef(string defName)
    {
        return new ThingDef
        {
            defName = defName,
            category = ThingCategory.Building,
            thingClass = typeof(Building),
            building = new BuildingProperties(),
            useHitPoints = false,
            stackLimit = 1,
            size = new IntVec2(1, 1),
        };
    }

    private static void SetPosition(Thing thing, IntVec3 position)
    {
        PositionField.SetValue(thing, position);
    }

    private static void SetMap(Thing thing, int mapIndex)
    {
        MapIndexField.SetValue(thing, (sbyte)mapIndex);
    }
}
