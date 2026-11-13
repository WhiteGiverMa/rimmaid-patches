using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMSE;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace DMSEWorldPerfFix.Validation;

/// <summary>
/// Offline RimWorld 1.7 world fixture: a real <see cref="World"/> with a real registered planet layer, a real
/// <see cref="WorldObjectsHolder"/> carrying 1,300 <see cref="Settlement"/> instances, real factions, real
/// <see cref="DefDatabase{T}"/> entries and the three real DMSE world components.
///
/// Composition (chosen to mirror the reported 100%-globe world):
///   0     .. 1027  1,028 Industrial/Spacer/Ultra settlements with settlement air defense (baseline 2)
///   1028  .. 1271    244 Tribal settlements without radar             (baseline 1)
///   1272  .. 1295     24 military sites with a real WorldObjectComp_AirDefense (baseline 3)
///   1296  .. 1297      2 player settlements
///   1298               1 destroyed settlement
///   1299               1 factionless settlement
/// </summary>
internal sealed class WorldFixture
{
    internal const int SettlementCount = 1300;
    internal const int DefenseSettlementCount = 1028;
    internal const int TribalSettlementCount = 244;
    internal const int MilitarySiteCount = 24;
    internal const int PlayerSiteCount = 2;

    internal const int FirstTribalIndex = DefenseSettlementCount;
    internal const int FirstMilitaryIndex = FirstTribalIndex + TribalSettlementCount;
    internal const int FirstPlayerIndex = FirstMilitaryIndex + MilitarySiteCount;
    internal const int DestroyedIndex = FirstPlayerIndex + PlayerSiteCount;
    internal const int FactionlessIndex = DestroyedIndex + 1;

    private static readonly FieldInfo TickManagerField =
        typeof(TickManager).GetField("ticksGameInt", BindingFlags.NonPublic | BindingFlags.Instance)!;

    internal World World = null!;
    internal WorldComponent_FactionAirDefenseAlert Alert = null!;
    internal WorldComponent_BVRNetwork Network = null!;
    internal WorldComponent_SettlementDefense SettlementDefense = null!;

    internal Faction PlayerFaction = null!;
    internal Faction TribalFaction = null!;
    internal Faction IndustrialFaction = null!;
    internal Faction SpacerFaction = null!;
    internal Faction UltraFaction = null!;

    internal readonly List<Settlement> Settlements = new();

    internal TickManager TickManager => Find.TickManager;

    internal int Now => Find.TickManager.TicksGame;

    internal static WorldFixture Create()
    {
        WorldFixture fixture = new();
        fixture.BuildGameAndWorld();
        fixture.BuildDefs();
        fixture.BuildFactions();
        fixture.BuildSettlements();
        fixture.BuildComponents();
        return fixture;
    }

    // ------------------------------------------------------------------ game / world

    private void BuildGameAndWorld()
    {
        WorldGrid grid = NewWorldGrid();
        World world = new()
        {
            info = new WorldInfo { seedString = "dmse-world-perf-validation" },
            grid = grid,
            worldObjects = new WorldObjectsHolder(),
            components = new List<WorldComponent>(),
        };

        FactionManager factionManager = new();
        world.factionManager = factionManager;
        World = world;

        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(game, new List<Map>());
        game.tickManager = new TickManager
        {
            gameStartAbsTick = 0,
        };
        game.researchManager = new ResearchManager();
        game.letterStack = new LetterStack();
        game.World = world;

        Current.Game = game;
    }

    private static WorldGrid NewWorldGrid()
    {
        WorldGrid grid = new();
        PlanetLayerDef surfaceDef = new()
        {
            defName = "DMSEWorldPerfFix_ValidationSurface",
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
            subdivisions: 5);

        typeof(WorldGrid).GetField("surface", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(grid, surface);

        // WorldObject.Tile's setter dirties the target tile through the layer, so the layer needs real Tile
        // instances (InitializeLayer only builds the vertex/neighbor index, not the tile objects).
        for (int i = 0; i < surface.TilesCount; i++)
        {
            surface.Tiles.Add(new Tile(surface.PlanetTileForID(i)));
        }

        return grid;
    }

    // ------------------------------------------------------------------ defs

    private static ResearchProjectDef? radarProject;

    private void BuildDefs()
    {
        EnsureDefs();
        SetResearchProgress(radarProject!, 1f);
    }

    /// <summary>
    /// Defs are process-wide, so they are created once and reused by every fixture (a second
    /// <c>DefDatabase.Add</c> with the same defName would log a duplicate and rename the def).
    /// </summary>
    private static void EnsureDefs()
    {
        if (radarProject != null)
        {
            return;
        }

        radarProject = new ResearchProjectDef
        {
            defName = "DMSE_RadarNetwork",
            label = "radar network",
            description = "validation stub",
            baseCost = 1f,
            techLevel = TechLevel.Industrial,
        };
        DefDatabase<ResearchProjectDef>.Add(radarProject);

        AddDoctrine("DMSEWorldPerfFix_Validation_Tribal", TechLevel.Neolithic, TechLevel.Medieval, withDefense: false);
        AddDoctrine("DMSEWorldPerfFix_Validation_Industrial", TechLevel.Industrial, TechLevel.Industrial, withDefense: true);
        AddDoctrine("DMSEWorldPerfFix_Validation_Spacer", TechLevel.Spacer, TechLevel.Spacer, withDefense: true);
        AddDoctrine("DMSEWorldPerfFix_Validation_Ultra", TechLevel.Ultra, TechLevel.Archotech, withDefense: true);
    }

    private static void AddDoctrine(string defName, TechLevel min, TechLevel max, bool withDefense)
    {
        MissileDoctrineDef doctrine = new()
        {
            defName = defName,
            label = defName,
            defaultMinTech = min,
            defaultMaxTech = max,
            settlementDefense = withDefense
                ? new SettlementAirDefenseProps
                {
                    detectionRange = 6f,
                    antiStealthLevel = 1,
                    channels = 1,
                    maxStock = 2,
                    restockPerDay = 0.5f,
                    cooldownTicks = 900,
                    threatAssessment = false,
                }
                : null,
        };

        DefDatabase<MissileDoctrineDef>.Add(doctrine);
    }

    private void SetResearchProgress(ResearchProjectDef project, float progress)
    {
        FieldInfo field = typeof(ResearchManager).GetField("progress", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Dictionary<ResearchProjectDef, float> map =
            (Dictionary<ResearchProjectDef, float>)field.GetValue(Find.ResearchManager)!;
        map[project] = progress;
    }

    // ------------------------------------------------------------------ factions

    private void BuildFactions()
    {
        PlayerFaction = NewFaction("Player", TechLevel.Spacer, isPlayer: true);
        TribalFaction = NewFaction("Tribal", TechLevel.Neolithic, isPlayer: false);
        IndustrialFaction = NewFaction("Industrial", TechLevel.Industrial, isPlayer: false);
        SpacerFaction = NewFaction("Spacer", TechLevel.Spacer, isPlayer: false);
        UltraFaction = NewFaction("Ultra", TechLevel.Ultra, isPlayer: false);

        FactionManager manager = Find.FactionManager;
        List<Faction> all = manager.AllFactionsListForReading;
        all.Add(PlayerFaction);
        all.Add(TribalFaction);
        all.Add(IndustrialFaction);
        all.Add(SpacerFaction);
        all.Add(UltraFaction);
        typeof(FactionManager).GetField("ofPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(manager, PlayerFaction);
    }

    private static Faction NewFaction(string label, TechLevel tech, bool isPlayer)
    {
        FactionDef def = new()
        {
            defName = "DMSEWorldPerfFix_Validation_" + label,
            label = label,
            techLevel = tech,
            isPlayer = isPlayer,
            humanlikeFaction = true,
        };

        Faction faction = (Faction)RuntimeHelpers.GetUninitializedObject(typeof(Faction));
        faction.def = def;
        faction.loadID = -1;
        faction.randomKey = 0;
        typeof(Faction).GetField("name", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(faction, label + " faction");
        typeof(Faction).GetField("relations", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(faction, new List<FactionRelation>());
        return faction;
    }

    // ------------------------------------------------------------------ settlements

    private void BuildSettlements()
    {
        WorldObjectDef settlementDef = new()
        {
            defName = "DMSEWorldPerfFix_Validation_Settlement",
            label = "validation settlement",
            description = "validation settlement",
            worldObjectClass = typeof(Settlement),
            canHaveFaction = true,
            useDynamicDrawer = false,
            selectable = true,
        };

        int tiles = Find.WorldGrid.TilesCount;
        List<WorldObject> all = Find.WorldObjects.AllWorldObjects;

        for (int i = 0; i < SettlementCount; i++)
        {
            Settlement settlement = new()
            {
                def = settlementDef,
                ID = 1000 + i,
                Name = "Settlement " + i,
                Tile = new PlanetTile(i % tiles),
            };

            Faction faction;
            if (i < DefenseSettlementCount)
            {
                faction = i % 3 == 0 ? IndustrialFaction : i % 3 == 1 ? SpacerFaction : UltraFaction;
            }
            else if (i < FirstMilitaryIndex)
            {
                faction = TribalFaction;
            }
            else if (i < FirstPlayerIndex)
            {
                faction = IndustrialFaction;
            }
            else if (i < DestroyedIndex)
            {
                faction = PlayerFaction;
            }
            else
            {
                faction = i == DestroyedIndex ? IndustrialFaction : null!;
            }

            settlement.SetFaction(faction);

            if (i >= FirstMilitaryIndex && i < FirstPlayerIndex)
            {
                CompProperties_WorldAirDefense props = new()
                {
                    worldDetectionRange = 12f,
                    antiStealthLevel = 2,
                    channels = 2,
                    interceptorStock = 8,
                    restockPerDay = 4f,
                    minTechLevel = TechLevel.Undefined,
                    ignoreNetworkResearch = false,
                };

                WorldObjectComp_AirDefense comp = new();
                comp.Initialize(props);
                comp.parent = settlement;
                settlement.AllComps.Add(comp);
            }

            if (i == DestroyedIndex)
            {
                typeof(WorldObject).GetField("destroyed", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(settlement, true);
            }

            all.Add(settlement);
            Settlements.Add(settlement);
        }
    }

    // ------------------------------------------------------------------ components

    private void BuildComponents()
    {
        SettlementDefense = new WorldComponent_SettlementDefense(World);
        Alert = new WorldComponent_FactionAirDefenseAlert(World);
        Network = new WorldComponent_BVRNetwork(World);

        World.components.Add(SettlementDefense);
        World.components.Add(Alert);
        World.components.Add(Network);
    }

    // ------------------------------------------------------------------ helpers

    internal void SetTicks(int ticks)
    {
        TickManagerField.SetValue(TickManager, ticks);
    }

    internal void Advance(int delta)
    {
        SetTicks(Now + delta);
    }

    internal IReadOnlyList<Settlement> DefenseSettlements()
    {
        List<Settlement> result = new(DefenseSettlementCount);
        for (int i = 0; i < DefenseSettlementCount; i++)
        {
            result.Add(Settlements[i]);
        }

        return result;
    }

    internal IReadOnlyList<Settlement> MilitarySites()
    {
        List<Settlement> result = new(MilitarySiteCount);
        for (int i = FirstMilitaryIndex; i < FirstPlayerIndex; i++)
        {
            result.Add(Settlements[i]);
        }

        return result;
    }

    /// <summary>Nearest same-faction settlement within <paramref name="maxTiles"/> of <paramref name="anchor"/>.</summary>
    internal Settlement? NearestSameFaction(Settlement anchor, float maxTiles, Settlement? exclude = null)
    {
        Settlement? best = null;
        float bestDistance = float.MaxValue;
        Vector3 center = MissileWorldUtility.TileCenter(anchor.Tile);

        foreach (Settlement settlement in Settlements)
        {
            if (settlement == anchor || settlement == exclude || settlement.Destroyed || settlement.Faction != anchor.Faction)
            {
                continue;
            }

            float distance = MissileWorldUtility.SurfaceTilesBetween(
                center,
                MissileWorldUtility.TileCenter(settlement.Tile));
            if (distance <= maxTiles && distance < bestDistance)
            {
                best = settlement;
                bestDistance = distance;
            }
        }

        return best;
    }

    internal float DistanceBetween(Settlement a, Settlement b)
    {
        return MissileWorldUtility.SurfaceTilesBetween(
            MissileWorldUtility.TileCenter(a.Tile),
            MissileWorldUtility.TileCenter(b.Tile));
    }
}
