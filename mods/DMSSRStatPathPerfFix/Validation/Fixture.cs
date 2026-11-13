using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMSSR.Extensions;
using DMSSR.Hediffs;
using RimWorld;
using Verse;
using Verse.AI;

namespace DMSSRStatPathPerfFix.Validation;

/// <summary>
/// Offline RimWorld 1.7 fixture: a real Map with real Pathing/PathGrid/TerrainGrid/ThingGrid, real
/// HediffSets and real StatWorkers, but no XML loading, no DefDatabase and no Unity scene.
///
/// Everything the vanilla CostToMoveIntoCell / StatWorker.GetValueUnfinalized paths touch is real
/// game code. Only the leaf data (terrain, stat definitions, hediff definitions) is constructed by
/// hand, exactly like the game's XML loader would.
/// </summary>
internal sealed class Fixture
{
    internal const int MapWidth = 10;
    internal const int MapHeight = 10;
    internal const int TerrainPathCost = 40;
    internal const float MoveSpeedBase = 1f;
    internal const float TerrainTagFactor = 0.5f;

    /// <summary>TicksPerMoveCardinal for a pawn with MoveSpeed 1 and no penalties: 60f / 1f.</summary>
    internal const float MoveTicks = 60f;

    /// <summary>(60 + 40) / 0.5, i.e. what vanilla CostToMoveIntoCell produces before any postfix.</summary>
    internal const float RawCellCost = (MoveTicks + TerrainPathCost) / TerrainTagFactor;

    private static readonly FieldInfo MapIndexField =
        typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo PositionField =
        typeof(Thing).GetField("positionInt", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo FleshTypeField =
        typeof(RaceProperties).GetField("fleshType", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo RoofGridField =
        typeof(RoofGrid).GetField("roofGrid", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo PathingNormalField =
        typeof(Pathing).GetField("normal", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo PathingFenceBlockedField =
        typeof(Pathing).GetField("fenceBlocked", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo AgeCachedLifeStageField =
        typeof(Pawn_AgeTracker).GetField("cachedLifeStageIndex", BindingFlags.NonPublic | BindingFlags.Instance)!;

    internal Map Map = null!;
    internal ThingDef MechDef = null!;
    internal ThingDef HumanDef = null!;
    internal PawnKindDef Kind = null!;
    internal TerrainDef Ground = null!;
    internal FleshTypeDef MechFlesh = null!;
    internal FleshTypeDef OrganicFlesh = null!;
    internal RoofDef Roof = null!;
    internal HediffDef NoiseDef = null!;
    internal HediffDef IgnoreExtensionDef = null!;
    internal HediffDef IgnoreCompDef = null!;
    internal HediffDef IgnoreCompOnPlainHediffDef = null!;
    internal HediffDef MechCustomizationDef = null!;

    internal readonly Dictionary<string, StatDef> Stats = new(StringComparer.Ordinal);

    private int nextThingId = 1;

    /// <summary>Stat -> comp field, in the exact if/else order DMSSR uses.</summary>
    internal static readonly (string Stat, string CompField)[] StatMap =
    {
        ("MoveSpeed", "moveSpeedMult"),
        ("ShootingAccuracyPawn", "shootAccMult"),
        ("MeleeDodgeChance", "meleeDodgeMult"),
        ("AimingDelayFactor", "aimTimeMult"),
        ("MeleeCooldownFactor", "meleeCooldownMult"),
        ("MeleeDamageFactor", "meleeDamageMult"),
        ("MechEnergyUsageFactor", "energyUseMult"),
        ("MechEnergyLossPerHP", "energyUseMult"),
        ("WastepacksPerRecharge", "wastepacksMult"),
        ("BandwidthCost", "bandwidthMult"),
    };

    internal static readonly string[] UnrelatedStats = { "ArmorRating_Sharp", "MarketValue" };

    internal static Fixture Create()
    {
        Fixture fixture = new();
        // DefOf classes are normally bound after all XML defs load. Nothing here reads the user's
        // mod list, and bindingNow stops DefOfHelper from logging "uninitialized DefOf" warnings.
        FieldInfo bindingNow = typeof(DefOfHelper).GetField("bindingNow", BindingFlags.NonPublic | BindingFlags.Static)!;
        bindingNow.SetValue(null, true);
        try
        {
            fixture.CreateWorld();
            fixture.CreateDefs();
            fixture.CreateStats();
            fixture.BindDefOfs();
        }
        finally
        {
            bindingNow.SetValue(null, false);
        }

        return fixture;
    }

    internal Pawn NewPawn(ThingDef def, IntVec3 position)
    {
        Pawn pawn = new() { def = def, thingIDNumber = nextThingId++ };
        pawn.kindDef = Kind;
        PositionField.SetValue(pawn, position);
        MapIndexField.SetValue(pawn, (sbyte)0);
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        AgeCachedLifeStageField.SetValue(pawn.ageTracker, 0);
        pawn.pather = new Pawn_PathFollower(pawn);
        AddRoof(position);
        return pawn;
    }

    private void AddRoof(IntVec3 cell)
    {
        RoofDef[] grid = (RoofDef[])RoofGridField.GetValue(Map.roofGrid)!;
        grid[Map.cellIndices.CellToIndex(cell)] = Roof;
    }

    internal HediffWithComps AddNoiseHediff(Pawn pawn)
    {
        HediffWithComps hediff = new() { def = NoiseDef, pawn = pawn, comps = new List<HediffComp>() };
        pawn.health.hediffSet.hediffs.Add(hediff);
        return hediff;
    }

    internal void AddNoiseHediffs(Pawn pawn, int count)
    {
        for (int i = 0; i < count; i++)
        {
            AddNoiseHediff(pawn);
        }

        pawn.health.hediffSet.DirtyCache();
    }

    internal HediffWithComps AddIgnoreExtensionHediff(Pawn pawn)
    {
        HediffWithComps hediff = new() { def = IgnoreExtensionDef, pawn = pawn, comps = new List<HediffComp>() };
        pawn.health.hediffSet.hediffs.Add(hediff);
        pawn.health.hediffSet.DirtyCache();
        return hediff;
    }

    internal HediffWithComps AddIgnoreCompHediff(Pawn pawn)
    {
        HediffWithComps hediff = new() { def = IgnoreCompDef, pawn = pawn, comps = new List<HediffComp> { new HediffComp_IgnoreTerrainMovePenalty() } };
        pawn.health.hediffSet.hediffs.Add(hediff);
        pawn.health.hediffSet.DirtyCache();
        return hediff;
    }

    /// <summary>A plain Hediff (not HediffWithComps) whose def lists the comp: upstream can never see it.</summary>
    internal Hediff AddPlainIgnoreCompHediff(Pawn pawn)
    {
        Hediff hediff = new() { def = IgnoreCompOnPlainHediffDef, pawn = pawn };
        pawn.health.hediffSet.hediffs.Add(hediff);
        pawn.health.hediffSet.DirtyCache();
        return hediff;
    }

    internal HediffComp_MechCustomization AddMechCustomization(Pawn pawn)
    {
        HediffWithComps hediff = new() { def = MechCustomizationDef, pawn = pawn, comps = new List<HediffComp>() };
        HediffComp_MechCustomization comp = new() { parent = hediff };
        hediff.comps!.Add(comp);
        pawn.health.hediffSet.hediffs.Add(hediff);
        pawn.health.hediffSet.DirtyCache();
        return comp;
    }

    internal void RemoveHediff(Pawn pawn, Hediff hediff)
    {
        pawn.health.hediffSet.hediffs.Remove(hediff);
        pawn.health.hediffSet.DirtyCache();
    }

    internal StatWorker Worker(string statDefName)
    {
        return Stats[statDefName].Worker;
    }

    private void CreateWorld()
    {
        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, new List<Map>());
        game.tickManager = new TickManager();
        Current.Game = game;

        Map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        Map.uniqueID = 1;
        Map.info = new MapInfo { Size = new IntVec3(MapWidth, 1, MapHeight), isPocketMap = true };
        Map.cellIndices = new CellIndices(MapWidth, MapHeight);
        Map.components = new List<MapComponent>();
        Map.mapPawns = new MapPawns(Map);
        Map.listerThings = new ListerThings(ListerThingsUse.Global);
        Map.thingGrid = new ThingGrid(Map);
        Map.edificeGrid = new EdificeGrid(Map);
        Map.terrainGrid = new TerrainGrid(Map);
        Map.roofGrid = new RoofGrid(Map);
        Map.snowGrid = new SnowGrid(Map);
        Map.pathing = NewPathing(Map);
        game.Maps.Add(Map);
    }

    private static Pathing NewPathing(Map map)
    {
        PathGridDef def = new() { defName = "ValidationNormal" };
        PathingContext context = new(map, new PathGrid(map, def));
        Pathing pathing = (Pathing)RuntimeHelpers.GetUninitializedObject(typeof(Pathing));
        PathingNormalField.SetValue(pathing, context);
        PathingFenceBlockedField.SetValue(pathing, context);
        return pathing;
    }

    private void CreateDefs()
    {
        Ground = new TerrainDef
        {
            defName = "ValidationSoil",
            label = "validation soil",
            pathCost = TerrainPathCost,
            passability = Traversability.Standable,
            tags = new List<string> { "ValidationSoil" },
        };

        Roof = new RoofDef { defName = "ValidationRoof" };
        MechFlesh = new FleshTypeDef { defName = "ValidationMechanoid", isOrganic = false };
        OrganicFlesh = new FleshTypeDef { defName = "ValidationOrganic", isOrganic = true };

        Kind = new PawnKindDef { defName = "ValidationKind" };
        Kind.moveSpeedFactorByTerrainTag["ValidationSoil"] = TerrainTagFactor;

        HumanDef = NewPawnDef("ValidationHuman", OrganicFlesh, Intelligence.Humanlike);
        MechDef = NewPawnDef("ValidationMech", MechFlesh, Intelligence.ToolUser);

        NoiseDef = NewHediffDef("ValidationNoise", typeof(HediffWithComps), null, null);

        IgnoreExtensionDef = NewHediffDef(
            "ValidationIgnoreTerrainExtension",
            typeof(HediffWithComps),
            null,
            new List<DefModExtension> { new DefModExt_IgnoreTerrainMovePenalty { enabled = true } });

        IgnoreCompDef = NewHediffDef(
            "ValidationIgnoreTerrainComp",
            typeof(HediffWithComps),
            new List<HediffCompProperties> { new HediffCompProperties_IgnoreTerrainMovePenalty() },
            null);

        // Deliberately a plain Hediff: def.comps lists the comp, but a plain Hediff never builds comps,
        // so upstream's TryGetComp returns null. The def-level cache must agree.
        IgnoreCompOnPlainHediffDef = NewHediffDef(
            "ValidationIgnoreTerrainCompOnPlainHediff",
            typeof(Hediff),
            new List<HediffCompProperties> { new HediffCompProperties_IgnoreTerrainMovePenalty() },
            null);

        MechCustomizationDef = NewHediffDef("ValidationMechCustomization", typeof(HediffWithComps), null, null);
    }

    private ThingDef NewPawnDef(string defName, FleshTypeDef flesh, Intelligence intelligence)
    {
        RaceProperties race = new()
        {
            intelligence = intelligence,
            lifeStageAges = new List<LifeStageAge>
            {
                new() { def = new LifeStageDef { defName = defName + "Stage" }, minAge = 0f },
            },
        };
        FleshTypeField.SetValue(race, flesh);

        return new ThingDef
        {
            defName = defName,
            category = ThingCategory.Pawn,
            thingClass = typeof(Pawn),
            race = race,
            statBases = new List<StatModifier>(),
        };
    }

    private static HediffDef NewHediffDef(
        string defName,
        Type hediffClass,
        List<HediffCompProperties>? comps,
        List<DefModExtension>? extensions)
    {
        HediffDef def = new()
        {
            defName = defName,
            label = defName,
            hediffClass = hediffClass,
            comps = comps,
            modExtensions = extensions,
        };
        return def;
    }

    private void CreateStats()
    {
        foreach ((string stat, string _) in StatMap)
        {
            Stats[stat] = NewStat(stat, MoveSpeedBase);
        }

        Stats["ArmorRating_Sharp"] = NewStat("ArmorRating_Sharp", 0.5f);
        Stats["MarketValue"] = NewStat("MarketValue", 1000f);
    }

    private static StatDef NewStat(string defName, float defaultBaseValue)
    {
        return new StatDef
        {
            defName = defName,
            label = defName,
            defaultBaseValue = defaultBaseValue,
            supressDisabledError = true,
            cacheable = false,
            immutable = false,
            workerClass = typeof(StatWorker),
            parts = null,
            postProcessStatFactors = null,
        };
    }

    private void BindDefOfs()
    {
        SetDefOf(typeof(TerrainDefOf), "Soil", Ground);
        SetDefOf(typeof(FleshTypeDefOf), "Normal", OrganicFlesh);
        SetDefOf(typeof(FleshTypeDefOf), "Mechanoid", MechFlesh);
        SetDefOf(typeof(DMS_HediffDefOf), "DMS_MechCustomization", MechCustomizationDef);
        foreach (KeyValuePair<string, StatDef> pair in Stats)
        {
            SetDefOf(typeof(StatDefOf), pair.Key, pair.Value);
        }
    }

    private static void SetDefOf(Type type, string fieldName, Def def)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(type.Name + "." + fieldName + " was not found.");
        field.SetValue(null, def);
    }
}
