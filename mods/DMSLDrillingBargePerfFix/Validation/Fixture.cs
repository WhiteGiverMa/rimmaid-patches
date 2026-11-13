using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMS_Legion;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

/// <summary>
/// Offline RimWorld fixture: a real 325x325 <see cref="Map"/> with a real <see cref="DeepResourceGrid"/>,
/// real <see cref="DMS_Legion"/> defs/comp-properties, and a drilling barge <see cref="Pawn"/>. No game loop,
/// no Unity scene, no XML loading - only the managed state that the patched code paths touch.
/// </summary>
internal sealed class Fixture
{
    internal const int MapSize = 325;
    internal const string BargeDefName = "DMSL_Mech_DrillingBarge";

    internal readonly IntVec3 AllowedVeinCell = new(50, 0, 50);
    internal readonly IntVec3 ExcludedVeinCell = new(120, 0, 120);
    internal readonly IntVec3 EmptyCell = new(200, 0, 200);
    internal readonly IntVec3 DynamicVeinCell = new(250, 0, 250);

    internal Map Map = null!;
    internal BiomeDef Biome = null!;
    internal ThingDef BargeDef = null!;
    internal ThingDef AllowedOre = null!;
    internal ThingDef ExcludedOre = null!;
    internal Pawn BargePawn = null!;
    internal Pawn OtherPawn = null!;
    internal WorkGiver_DrillingBargeDeepDrill Giver = null!;
    internal DMSL_ModSettings Settings = null!;

    internal static Fixture Create()
    {
        Fixture fixture = new();
        fixture.CreateWorld();
        fixture.CreateDefs();
        fixture.CreatePawns();
        fixture.ApplyUpstreamSetAtPatch();
        fixture.CreateSettings();
        fixture.SeedVeins();
        fixture.Giver = new WorkGiver_DrillingBargeDeepDrill();
        return fixture;
    }

    internal void SetVein(IntVec3 cell, ThingDef def, int count)
    {
        Map.deepResourceGrid.SetAt(cell, def, count);
    }

    private void CreateWorld()
    {
        Biome = new BiomeDef { defName = "DMSL_PerfFix_TestBiome", hasBedrock = true };

        Game game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        typeof(Game).GetField("maps", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(game, new List<Map>());
        Current.Game = game;

        Map map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.uniqueID = 1;
        map.info = new MapInfo { Size = new IntVec3(MapSize, 1, MapSize), isPocketMap = true };
        map.pocketTileInfo = new Tile { PrimaryBiome = Biome };
        map.cellIndices = new CellIndices(MapSize, MapSize);
        map.components = new List<MapComponent>();
        map.mapPawns = new MapPawns(map);
        map.deepResourceGrid = new DeepResourceGrid(map);

        game.Maps.Add(map);
        Map = map;
    }

    private void CreateDefs()
    {
        AllowedOre = NewResourceDef("DMSL_PerfFix_AllowedOre", 40001);
        ExcludedOre = NewResourceDef("DMSL_PerfFix_ExcludedOre", 40002);

        ThingDef barge = new() { defName = BargeDefName };
        barge.comps.Add(new CompProperties_DrillingBargeDeepResourceOverlay
        {
            excludedDeepResourceDefNames = new List<string> { ExcludedOre.defName },
        });

        DefDatabase<ThingDef>.Add(AllowedOre);
        DefDatabase<ThingDef>.Add(ExcludedOre);
        DefDatabase<ThingDef>.Add(barge);
        DefDatabase<ThingDef>.InitializeShortHashDictionary();

        BargeDef = barge;
    }

    private void CreatePawns()
    {
        BargePawn = NewPawn(BargeDef);
        OtherPawn = NewPawn(new ThingDef { defName = "DMSL_PerfFix_OtherPawn" });

        typeof(MapPawns)
            .GetField("pawnsSpawned", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(Map.mapPawns, new List<Pawn> { BargePawn, OtherPawn });
    }

    private void CreateSettings()
    {
        Settings = new DMSL_ModSettings
        {
            enableDrillingBargeDeepDrill = true,
            enableDrillingBargeExperimentalWorkLogic = false,
        };

        DMSL_ModSettings.settings = Settings;
    }

    /// <summary>
    /// Applies the upstream <c>DeepResourceGrid.SetAt</c> tracker patch, exactly like the real mod does at load.
    /// This keeps <c>DeepResourceGridTrackerComponent.HasAnyDeepResource</c> honest while the test mutates veins,
    /// which is the state the in-game guard sees.
    /// </summary>
    private void ApplyUpstreamSetAtPatch()
    {
        Harmony harmony = new("dmsl.drillingbargeperffix.validation.upstream-seta");
        MethodInfo setAt = AccessTools.Method(typeof(DeepResourceGrid), "SetAt", new[] { typeof(IntVec3), typeof(ThingDef), typeof(int) })!;

        harmony.Patch(
            setAt,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(DeepResourceGrid_SetAt_Patch), "Prefix")),
            postfix: new HarmonyMethod(AccessTools.Method(typeof(DeepResourceGrid_SetAt_Patch), "Postfix")));
    }

    private void SeedVeins()
    {
        SetVein(AllowedVeinCell, AllowedOre, 500);
        SetVein(new IntVec3(AllowedVeinCell.x + 1, 0, AllowedVeinCell.z), AllowedOre, 500);

        SetVein(ExcludedVeinCell, ExcludedOre, 500);

        SetVein(new IntVec3(0, 0, 0), AllowedOre, 100);
        SetVein(new IntVec3(MapSize - 1, 0, MapSize - 1), AllowedOre, 100);
    }

    private static Pawn NewPawn(ThingDef def)
    {
        Pawn pawn = new() { def = def };
        typeof(Thing)
            .GetField("mapIndexOrState", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(pawn, (sbyte)0);
        return pawn;
    }

    private static ThingDef NewResourceDef(string defName, ushort shortHash)
    {
        return new ThingDef
        {
            defName = defName,
            shortHash = shortHash,
            deepCountPerPortion = 10,
        };
    }
}
