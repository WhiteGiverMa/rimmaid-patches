using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Unity.Collections;
using Verse;
using Verse.AI;

namespace Validation;

internal sealed class Fixture
{
    internal readonly Map Map;
    internal readonly Faction Player;
    internal readonly Faction Enemy;
    internal readonly PathGrid Grid;
    internal object? Component;
    internal readonly ThingDef Animal = new() { defName = "Animal", category = ThingCategory.Pawn, race = new RaceProperties { intelligence = Intelligence.Animal } };
    internal readonly ThingDef Human = new() { defName = "Human", category = ThingCategory.Pawn, race = new RaceProperties { intelligence = Intelligence.Humanlike } };
    internal readonly TerrainDef Ground = new() { defName = "Ground", pathCost = 13 };
    internal static void Set(object o, string f, object? value) => AccessTools.Field(o.GetType(), f).SetValue(o, value);
    internal static T Raw<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    internal Fixture(int size = 325)
    {
        Game game = Raw<Game>();
        Set(game, "maps", new List<Map>());
        game.tickManager = new TickManager();
        game.World = Raw<World>();
        game.World.factionManager = new FactionManager();
        Current.Game = game;
        Set(game.tickManager, "ticksGameInt", 2000);
        Set(Animal.race, "fleshType", new FleshTypeDef { defName = "Flesh" });
        Set(Human.race, "fleshType", new FleshTypeDef { defName = "HumanFlesh" });
        Player = new Faction { def = new FactionDef { defName = "Player", isPlayer = true } };
        Enemy = new Faction { def = new FactionDef { defName = "Enemy" } };
        Set(game.World.factionManager, "ofPlayer", Player);
        game.World.factionManager.AllFactionsListForReading.Add(Player);
        game.World.factionManager.AllFactionsListForReading.Add(Enemy);
        Set(Player, "relations", new List<FactionRelation> { new() { other = Enemy, kind = FactionRelationKind.Hostile } });
        Set(Enemy, "relations", new List<FactionRelation> { new() { other = Player, kind = FactionRelationKind.Hostile } });
        Map = Raw<Map>();
        Map.info = new MapInfo { Size = new IntVec3(size, 1, size), isPocketMap = true };
        Map.generatorDef = new MapGeneratorDef { defeatRequiresCantReachUnfogged = false };
        Map.cellIndices = new CellIndices(size, size);
        Map.components = [];
        Map.mapPawns = new MapPawns(Map);
        Map.listerThings = new ListerThings(ListerThingsUse.Global);
        Map.thingGrid = new ThingGrid(Map);
        Map.terrainGrid = new TerrainGrid(Map);
        Map.snowGrid = new SnowGrid(Map);
        Map.fogGrid = new FogGrid(Map);
        Map.events = new MapEvents(Map);
        Map.attackTargetsCache = new AttackTargetsCache(Map);
        Grid = new PathGrid(Map, new PathGridDef());
        Map.pathing = Raw<Pathing>();
        PathingContext context = new(Map, Grid);
        Set(Map.pathing, "normal", context); Set(Map.pathing, "fenceBlocked", context); Set(Map.pathing, "flying", context);
        Set(Map.terrainGrid, "topGrid", Enumerable.Repeat(Ground, size * size).ToArray());
        game.Maps.Add(Map);
        ThingDefOf.Fire = new ThingDef { defName = "Fire", thingClass = typeof(Fire), category = ThingCategory.Ethereal, pathfinderDangerous = true };
        JobDefOf.Wait = new JobDef { defName = "Wait" };
        JobDefOf.Wait_Combat = new JobDef { defName = "Wait_Combat" };
        MentalStateDefOf.PanicFlee = new MentalStateDef { defName = "PanicFlee" };
        StatDefOf.Flammability = new StatDef { defName = "Flammability", defaultBaseValue = 0f };
        if (Program.Dnaf != null)
        {
            Component = Activator.CreateInstance(Program.Dnaf.GetType("DoNotAvoidFire.DNAFMapComponent", true)!, Map)!;
            Map.components.Add((MapComponent)Component);
        }
    }
    internal Pawn Pawn(IntVec3 pos, bool human = false, Faction? faction = null)
    {
        Pawn p = new() { def = human ? Human : Animal, kindDef = new PawnKindDef { canMeleeAttack = true, race = human ? Human : Animal }, thingIDNumber = next++ };
        Set(p, "positionInt", pos); Set(p, "mapIndexOrState", (sbyte)0); Set(p, "factionInt", faction ?? Player);
        p.health = new Pawn_HealthTracker(p);
        p.mindState = new Pawn_MindState(p);
        p.stances = new Pawn_StanceTracker(p);
        p.jobs = new Pawn_JobTracker(p);
        p.natives = new Pawn_NativeVerbs(p);
        p.meleeVerbs = new Pawn_MeleeVerbs(p);
        p.carryTracker = new Pawn_CarryTracker(p);
        p.ageTracker = new Pawn_AgeTracker(p);
        p.apparel = human ? new Pawn_ApparelTracker(p) : null;
        ((List<Pawn>)Map.mapPawns.AllPawnsSpawned).Add(p);
        Map.thingGrid.Register(p);
        return p;
    }
    private int next = 1;
    internal Fire Fire(IntVec3 pos, float size = 1f, Thing? parent = null)
    {
        Fire f = new() { def = ThingDefOf.Fire, fireSize = size, parent = parent, thingIDNumber = next++ };
        Set(f, "positionInt", pos); Set(f, "mapIndexOrState", (sbyte)0);
        Map.listerThings.Add(f); Map.thingGrid.Register(f);
        return f;
    }
    internal JobDriver_Wait Wait(Pawn p, bool combat = false)
    {
        Job job = Raw<Job>(); job.def = combat ? JobDefOf.Wait_Combat : JobDefOf.Wait;
        job.canUseRangedWeapon = true;
        return new() { pawn = p, job = job, collideWithPawns = true };
    }
    internal void Tick() => Set(Current.Game.tickManager, "ticksGameInt", Find.TickManager.TicksGame + 1);
    internal PathRequest Request(Pawn p) => new(Map, p.Position, p.Position, null, new TraverseParms { pawn = p, mode = TraverseMode.ByPawn, avoidPersistentDanger = true }, PathFinderCostTuning.DefaultTuning, PathEndMode.OnCell, p, 0, 0, 0, null);
}
