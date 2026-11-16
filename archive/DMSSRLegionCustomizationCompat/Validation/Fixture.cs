using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMSSR.Hediffs;
using Fortified;
using RimWorld;
using Verse;
using Verse.AI;

namespace DMSSRLegionCustomizationCompat.Validation;

/// <summary>
/// Offline RimWorld 1.7 fixture built from the real game DLL, the real DMSSR/Fortified DLLs and
/// the real BodyPartDef / BodyDef / race XML shipped by DMS Core and DMS Legion. It deliberately
/// does not load a map scene or any textures; only the data and code paths under test are real.
/// </summary>
internal sealed class Fixture
{
    private static readonly FieldInfo FleshTypeField =
        typeof(RaceProperties).GetField("fleshType", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo MapIndexField =
        typeof(Thing).GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo PositionField =
        typeof(Thing).GetField("positionInt", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo FactionField =
        typeof(Thing).GetField("factionInt", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo MapPawnsSpawnedField =
        typeof(MapPawns).GetField("pawnsSpawned", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly FieldInfo AgeCachedLifeStageField =
        typeof(Pawn_AgeTracker).GetField("cachedLifeStageIndex", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private int nextThingId = 1;

    internal DefBank Defs = null!;
    internal List<LegionMechEntry> LegionMechs = null!;
    internal string? DroneDefName;

    internal Map Map = null!;
    internal Faction PlayerFaction = null!;
    internal FleshTypeDef MechFlesh = null!;
    internal FleshTypeDef OrganicFlesh = null!;
    internal HediffDef CustomizationDef = null!;
    internal ResearchProjectDef MicroelectronicsBasics = null!;
    internal readonly Dictionary<string, ThingDef> ControlDefs = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, HediffDef> ModificationDefs = new(StringComparer.Ordinal);

    internal static Fixture Create(string gameRoot, string legionRoot, string dmsCoreRoot, string ancientCorpsRoot)
    {
        Fixture fixture = new();
        FieldInfo bindingNow = typeof(DefOfHelper).GetField("bindingNow", BindingFlags.NonPublic | BindingFlags.Static)!;
        bindingNow.SetValue(null, true);
        try
        {
            fixture.Defs = XmlDefLoader.Load(gameRoot, legionRoot, dmsCoreRoot, ancientCorpsRoot);
            fixture.LegionMechs = XmlDefLoader.LoadLegionMechs(legionRoot);
            fixture.DroneDefName = XmlDefLoader.LoadDroneDefName(legionRoot);
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

    internal ThingDef MechDef(LegionMechEntry entry)
    {
        if (!Defs.Bodies.TryGetValue(entry.BodyDefName, out BodyDef? body))
        {
            throw new InvalidOperationException(
                entry.DefName + " refers to body " + entry.BodyDefName + ", which was not found in the installed XML.");
        }

        return NewPawnDef(entry.DefName, body, MechFlesh, Intelligence.ToolUser);
    }

    internal ThingDef ControlDef(string defName, string bodyDefName = "DMS_Bipedal")
    {
        if (!ControlDefs.TryGetValue(defName, out ThingDef? def))
        {
            if (!Defs.Bodies.TryGetValue(bodyDefName, out BodyDef? body))
            {
                throw new InvalidOperationException("Control body " + bodyDefName + " was not found.");
            }

            def = NewPawnDef(defName, body, MechFlesh, Intelligence.ToolUser);
            ControlDefs.Add(defName, def);
        }

        return def;
    }

    internal ThingDef PlainDef(string defName, FleshTypeDef flesh, Intelligence intelligence)
    {
        if (!ControlDefs.TryGetValue(defName, out ThingDef? def))
        {
            def = NewPawnDef(defName, Defs.Bodies["DMS_Bipedal"], flesh, intelligence);
            ControlDefs.Add(defName, def);
        }

        return def;
    }

    internal ThingDef DefWithBody(string defName, string bodyDefName)
    {
        if (ControlDefs.TryGetValue(defName, out ThingDef? def))
        {
            return def;
        }

        if (!Defs.Bodies.TryGetValue(bodyDefName, out BodyDef? body))
        {
            throw new InvalidOperationException("Body " + bodyDefName + " was not found.");
        }

        def = NewPawnDef(defName, body, MechFlesh, Intelligence.ToolUser);
        ControlDefs.Add(defName, def);
        return def;
    }

    internal Pawn NewPawn(ThingDef def, IntVec3 position, bool spawned = true, Faction? faction = null)
    {
        Pawn pawn = new() { def = def, thingIDNumber = nextThingId++ };
        pawn.kindDef = new PawnKindDef { defName = def.defName + "Kind", race = def };
        pawn.health = new Pawn_HealthTracker(pawn);
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        AgeCachedLifeStageField.SetValue(pawn.ageTracker, 0);
        pawn.mindState = new Pawn_MindState();
        PositionField.SetValue(pawn, position);
        MapIndexField.SetValue(pawn, spawned ? (sbyte)0 : (sbyte)(-1));
        if (faction != null)
        {
            FactionField.SetValue(pawn, faction);
        }

        return pawn;
    }

    internal Pawn NewLegionPawn(LegionMechEntry entry, IntVec3 position)
    {
        return NewPawn(MechDef(entry), position);
    }

    internal Pawn NewOpenableLegionPawn(LegionMechEntry entry, IntVec3 position)
    {
        Pawn pawn = NewLegionPawn(entry, position);
        PrepareOpenable(pawn);
        AddToMap(pawn);
        return pawn;
    }

    internal void PrepareOpenable(Pawn pawn)
    {
        FactionField.SetValue(pawn, PlayerFaction);
        MapIndexField.SetValue(pawn, (sbyte)0);
    }

    internal void AddToMap(Pawn pawn)
    {
        List<Pawn> spawned = (List<Pawn>)MapPawnsSpawnedField.GetValue(Map.mapPawns)!;
        if (!spawned.Contains(pawn))
        {
            spawned.Add(pawn);
        }
    }

    internal void RemoveFromMap(Pawn pawn)
    {
        List<Pawn> spawned = (List<Pawn>)MapPawnsSpawnedField.GetValue(Map.mapPawns)!;
        spawned.Remove(pawn);
    }

    internal BodyPartRecord Part(Pawn pawn, string bodyPartDefName)
    {
        BodyPartRecord? part = pawn.RaceProps.body.AllParts
            .FirstOrDefault(record => string.Equals(record.def.defName, bodyPartDefName, StringComparison.Ordinal));
        if (part == null)
        {
            throw new InvalidOperationException(
                pawn.def.defName + " body " + pawn.RaceProps.body.defName + " has no " + bodyPartDefName);
        }

        return part;
    }

    internal HediffComp_Modification AddModification(Pawn pawn, string hediffDefName, string bodyPartDefName)
    {
        if (!ModificationDefs.TryGetValue(hediffDefName, out HediffDef? def))
        {
            def = new HediffDef
            {
                defName = hediffDefName,
                label = hediffDefName,
                hediffClass = typeof(HediffWithComps),
                comps = new List<HediffCompProperties>
                {
                    new HediffCompProperties_Modification(),
                },
            };
            ModificationDefs.Add(hediffDefName, def);
        }

        BodyPartRecord part = Part(pawn, bodyPartDefName);
        HediffWithComps hediff = new()
        {
            def = def,
            pawn = pawn,
            comps = new List<HediffComp>(),
        };
        HediffComp_Modification comp = new() { parent = hediff };
        hediff.comps!.Add(comp);
        hediff.Part = part;
        pawn.health.hediffSet.hediffs.Add(hediff);
        return comp;
    }

    internal void AddHediffDirect(Pawn pawn, HediffDef def)
    {
        HediffWithComps hediff = new() { def = def, pawn = pawn, comps = new List<HediffComp>() };
        pawn.health.hediffSet.hediffs.Add(hediff);
    }

    internal void AddToResearchProgress(ResearchProjectDef def)
    {
        // Do not call ResearchManager.AddProgress: at 100% it runs FinishProject, which touches
        // ModLister/Prefs and the Unity UI. The offline harness only needs IsFinished to be true.
        FieldInfo progressField = typeof(ResearchManager).GetField(
            "progress", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Dictionary<ResearchProjectDef, float> progress =
            (Dictionary<ResearchProjectDef, float>)progressField.GetValue(Current.Game.researchManager)!;
        progress[def] = def.baseCost;
    }

    private ThingDef NewPawnDef(string defName, BodyDef body, FleshTypeDef flesh, Intelligence intelligence)
    {
        RaceProperties race = new()
        {
            intelligence = intelligence,
            body = body,
            lifeStageAges = new List<LifeStageAge>
            {
                new() { def = new LifeStageDef { defName = defName + "Stage" }, minAge = 0f },
            },
        };
        FleshTypeField.SetValue(race, flesh);

        return new ThingDef
        {
            defName = defName,
            label = defName,
            category = ThingCategory.Pawn,
            thingClass = typeof(Pawn),
            race = race,
            statBases = new List<StatModifier>(),
            comps = new List<CompProperties>(),
        };
    }

    private void CreateWorld()
    {
        // A few Verse utilities (Scribe's DeepProfiler wrapper, for example) read Prefs.data.
        // The real Prefs static constructor is never run in this host, so provide the defaults.
        typeof(Prefs).GetField("data", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, new PrefsData());

        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, new List<Map>());
        game.tickManager = new TickManager();
        game.researchManager = new ResearchManager();
        game.uniqueIDsManager = new UniqueIDsManager();
        Current.Game = game;

        Map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        Map.uniqueID = 1;
        Map.info = new MapInfo { Size = new IntVec3(10, 1, 10) };
        Map.cellIndices = new CellIndices(10, 10);
        Map.components = new List<MapComponent>();
        Map.mapPawns = new MapPawns(Map);
        game.Maps.Add(Map);

        PlayerFaction = new Faction
        {
            def = new FactionDef { defName = "ValidationPlayer", isPlayer = true },
        };
        game.InitData = new GameInitData { playerFaction = PlayerFaction };
    }

    private void CreateDefs()
    {
        MechFlesh = new FleshTypeDef { defName = "ValidationMechanoid", isOrganic = false };
        OrganicFlesh = new FleshTypeDef { defName = "ValidationOrganic", isOrganic = true };

        CustomizationDef = new HediffDef
        {
            defName = "DMS_MechCustomization",
            label = "Chassis tuning",
            hediffClass = typeof(HediffWithComps),
            comps = new List<HediffCompProperties>
            {
                new HediffCompProperties_MechCustomization(),
            },
        };

        MicroelectronicsBasics = new ResearchProjectDef
        {
            defName = "MicroelectronicsBasics",
            label = "Microelectronics basics",
            baseCost = 1f,
        };
        AddToResearchProgress(MicroelectronicsBasics);

        DefDatabase<HediffDef>.Add(CustomizationDef);
    }

    private void CreateStats()
    {
        string[] stats =
        {
            "MoveSpeed",
            "ShootingAccuracyPawn",
            "MeleeDodgeChance",
            "AimingDelayFactor",
            "MeleeCooldownFactor",
            "MechEnergyUsageFactor",
            "MechEnergyLossPerHP",
            "WastepacksPerRecharge",
            "BandwidthCost",
            "MeleeDamageFactor",
            "ArmorRating_Sharp",
            "MarketValue",
            "MeleeDamageFactor",
        };

        foreach (string stat in stats.Distinct(StringComparer.Ordinal))
        {
            StatDef def = new()
            {
                defName = stat,
                label = stat,
                defaultBaseValue = stat == "MarketValue" ? 1000f : 1f,
                supressDisabledError = true,
                cacheable = false,
                immutable = false,
                workerClass = typeof(StatWorker),
                parts = null,
                postProcessStatFactors = null,
            };
            DefDatabase<StatDef>.Add(def);
        }
    }

    private void BindDefOfs()
    {
        SetDefOf(typeof(FleshTypeDefOf), "Mechanoid", MechFlesh);
        SetDefOf(typeof(FleshTypeDefOf), "Normal", OrganicFlesh);
        SetDefOf(typeof(DMS_HediffDefOf), "DMS_MechCustomization", CustomizationDef);
        SetDefOf(typeof(ResearchProjectDefOf), "MicroelectronicsBasics", MicroelectronicsBasics);

        foreach (FieldInfo field in typeof(StatDefOf).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(StatDef))
            {
                continue;
            }

            StatDef? def = DefDatabase<StatDef>.GetNamedSilentFail(field.Name);
            if (def != null)
            {
                field.SetValue(null, def);
            }
        }

        foreach (FieldInfo field in typeof(BodyPartTagDefOf).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(BodyPartTagDef))
            {
                continue;
            }

            field.SetValue(null, Defs.GetOrCreateTag(field.Name));
        }

        foreach (FieldInfo field in typeof(BodyPartGroupDefOf).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType == typeof(BodyPartGroupDef))
            {
                field.SetValue(null, Defs.GetOrCreateGroup(field.Name));
            }
        }

        foreach (FieldInfo field in typeof(BodyPartDefOf).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(BodyPartDef))
            {
                continue;
            }

            if (Defs.BodyParts.TryGetValue(field.Name, out BodyPartDef? def))
            {
                field.SetValue(null, def);
            }
        }

        foreach (FieldInfo field in typeof(PawnCapacityDefOf).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(PawnCapacityDef))
            {
                continue;
            }

            PawnCapacityDef def = new() { defName = field.Name, label = field.Name };
            DefDatabase<PawnCapacityDef>.Add(def);
            field.SetValue(null, def);
        }

        SetDefOf(typeof(SoundDefOf), "DialogBoxAppear", new SoundDef { defName = "DialogBoxAppear" });
        SetDefOf(typeof(SoundDefOf), "Click", new SoundDef { defName = "Click" });
    }

    private static void SetDefOf(Type type, string fieldName, Def def)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException(type.Name + "." + fieldName + " was not found.");
        field.SetValue(null, def);
    }
}
