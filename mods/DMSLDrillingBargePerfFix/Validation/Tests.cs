using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using DMS_Legion;
using HarmonyLib;
using RimWorld;
using Verse;
using PerfFix = WhiteGiver.DMSL.DrillingBargePerfFix.DrillingBargePerfFix;

internal static class Tests
{
    private static int checks;
    private static int failures;

    public static int Run()
    {
        Fixture fx = Fixture.Create();
        Map map = fx.Map;
        int numCells = map.cellIndices.NumGridCells;

        Section("RED - upstream behaviour before the fix is applied");
        Check("fixture is a 325x325 map (" + numCells.ToString("N0") + " cells)", numCells == 105_625);

        (int redCells, TimeSpan redElapsed) = MeasureJobScan(fx);
        Check("upstream PotentialWorkCellsGlobal + validation yields every map cell (" + redCells.ToString("N0") + ")", redCells == numCells);
        Console.WriteLine("       upstream full scan: " + redCells.ToString("N0") + " cells in " + redElapsed.TotalMilliseconds.ToString("F1") + " ms");

        Check("upstream IsExcludedDeepResource(excluded ore) == true", CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource(fx.ExcludedOre));
        Check("upstream IsExcludedDeepResource(allowed ore) == false", !CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource(fx.AllowedOre));
        Check("upstream IsExcludedDeepResource(null) == false", !CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource(null));

        Check("upstream CellHasValuableDeepResource on an allowed vein", WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(fx.AllowedVeinCell, map, fx.BargePawn));
        Check("upstream !CellHasValuableDeepResource on an empty cell", !WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(fx.EmptyCell, map, fx.BargePawn));
        Check("upstream !CellHasValuableDeepResource next to an excluded vein (barge)", !WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(fx.ExcludedVeinCell, map, fx.BargePawn));
        Check("upstream CellHasValuableDeepResource next to an excluded vein (no pawn)", WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(fx.ExcludedVeinCell, map, null));
        Check("upstream !CellHasValuableDeepResource out of bounds", !WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(new IntVec3(-1, 0, 0), map, fx.BargePawn));
        Check("upstream yields nothing for a non-barge pawn", CountCells(fx.Giver.PotentialWorkCellsGlobal(fx.OtherPawn)) == 0);

        fx.Settings.enableDrillingBargeExperimentalWorkLogic = true;
        DrillingBargeValidDrillCellsCacheComponent component = DrillingBargeValidDrillCellsCacheComponent.GetOrCreate(map)!;
        int scanBeforeRed = ReadScanIndex(component);
        bool enteredPerTickScan = false;
        try
        {
            component.MapComponentTick();
        }
        catch (NullReferenceException)
        {
            // The fixture has no live PathGrid/TerrainGrid, so CanDrillAt cannot complete. Reaching it proves
            // that upstream really performs the 4-cells-per-tick walk instead of short-circuiting.
            enteredPerTickScan = true;
        }

        int scanAfterRed = ReadScanIndex(component);
        Check("upstream MapComponentTick enters CanDrillAt and advances its scan (" + scanBeforeRed + " -> " + scanAfterRed + ")", enteredPerTickScan && scanAfterRed == scanBeforeRed + 1);
        fx.Settings.enableDrillingBargeExperimentalWorkLogic = false;

        Section("GREEN - applying DMSLDrillingBargePerfFix");
        PerfFix.Apply();

        MethodInfo potentialWorkCells = AccessTools.Method(typeof(WorkGiver_DrillingBargeDeepDrill), "PotentialWorkCellsGlobal", new[] { typeof(Pawn) })!;
        MethodInfo cellHasResource = AccessTools.Method(typeof(WorkGiver_DrillingBargeDeepDrill), "CellHasValuableDeepResource", new[] { typeof(IntVec3), typeof(Map), typeof(Pawn) })!;
        MethodInfo isExcluded = AccessTools.Method(typeof(CompDrillingBargeDeepResourceOverlay), "IsExcludedDeepResource", new[] { typeof(ThingDef) })!;
        MethodInfo mapComponentTick = AccessTools.Method(typeof(DrillingBargeValidDrillCellsCacheComponent), "MapComponentTick", Type.EmptyTypes)!;
        MethodInfo setAt = AccessTools.Method(typeof(DeepResourceGrid), "SetAt", new[] { typeof(IntVec3), typeof(ThingDef), typeof(int) })!;
        MethodInfo exposeData = AccessTools.Method(typeof(DeepResourceGrid), "ExposeData", Type.EmptyTypes)!;

        Check("PotentialWorkCellsGlobal patched by " + PerfFix.HarmonyId, HasPatch(potentialWorkCells, PerfFix.HarmonyId, "PotentialWorkCellsGlobalPrefix", prefix: true));
        Check("CellHasValuableDeepResource patched by " + PerfFix.HarmonyId, HasPatch(cellHasResource, PerfFix.HarmonyId, "CellHasValuableDeepResourcePrefix", prefix: true));
        Check("IsExcludedDeepResource patched by " + PerfFix.HarmonyId, HasPatch(isExcluded, PerfFix.HarmonyId, "IsExcludedDeepResourcePrefix", prefix: true));
        Check("MapComponentTick patched by " + PerfFix.HarmonyId, HasPatch(mapComponentTick, PerfFix.HarmonyId, "MapComponentTickPrefix", prefix: true));
        Check("DeepResourceGrid.SetAt postfix installed", HasPatch(setAt, PerfFix.HarmonyId, "DeepResourceGridSetAtPostfix", prefix: false));
        Check("DeepResourceGrid.ExposeData postfix installed", HasPatch(exposeData, PerfFix.HarmonyId, "DeepResourceGridExposeDataPostfix", prefix: false));

        IEnumerable<IntVec3> patchedEnumerable = fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn);
        Check("patched PotentialWorkCellsGlobal returns IList<IntVec3>", patchedEnumerable is IList<IntVec3>);
        IList<IntVec3> patchedCells = (IList<IntVec3>)patchedEnumerable;

        List<IntVec3> truthExcluded = ComputeTruth(fx, applyExclusions: true);
        Check("patched list is a small candidate set, not the map (" + patchedCells.Count + " cells)", patchedCells.Count < numCells);
        Check("patched list has exactly the upstream truth size (" + truthExcluded.Count + ")", patchedCells.Count == truthExcluded.Count);
        Check("patched list is cell-for-cell identical, in upstream order", SameCells(patchedCells, truthExcluded));
        Check("no allocation when nothing changed (same list instance reused)", ReferenceEquals(patchedCells, fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn)));

        (int greenCells, TimeSpan greenElapsed) = MeasureJobScan(fx);
        Check("patched job scan visits only the candidate cells (" + greenCells.ToString("N0") + ")", greenCells == truthExcluded.Count);
        Console.WriteLine("       patched  full scan: " + greenCells.ToString("N0") + " cells in " + greenElapsed.TotalMilliseconds.ToString("F3") + " ms");

        Section("GREEN - 105,625-cell equivalence with upstream TryGetDeepResource");
        int mismatches = 0;
        for (int i = 0; i < numCells; i++)
        {
            IntVec3 cell = map.cellIndices.IndexToCell(i);

            bool upstreamExcluded = WorkGiver_DrillingBargeDeepDrill.TryGetDeepResource(cell, map, out _, out _, out _, true);
            bool upstreamRaw = WorkGiver_DrillingBargeDeepDrill.TryGetDeepResource(cell, map, out _, out _, out _, false);

            if (WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(cell, map, fx.BargePawn) != upstreamExcluded)
            {
                mismatches++;
            }

            if (WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(cell, map, null) != upstreamRaw)
            {
                mismatches++;
            }
        }

        Check("CellHasValuableDeepResource matches upstream for all " + (numCells * 2).ToString("N0") + " (cell, exclusions) pairs", mismatches == 0);

        IntVec3[] outside =
        {
            new(-1, 0, 0),
            new(0, 0, -1),
            new(Fixture.MapSize, 0, 0),
            new(0, 0, Fixture.MapSize),
            new(Fixture.MapSize, 0, Fixture.MapSize),
            new(int.MinValue, 0, int.MinValue),
            new(int.MaxValue, 0, int.MaxValue),
        };

        bool outsideOk = true;
        foreach (IntVec3 cell in outside)
        {
            if (WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(cell, map, fx.BargePawn)
                || WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(cell, map, null)
                || WorkGiver_DrillingBargeDeepDrill.TryGetDeepResource(cell, map, out _, out _, out _, true))
            {
                outsideOk = false;
            }
        }

        Check("out-of-bounds cells stay false (patched and upstream)", outsideOk);

        Section("GREEN - dynamic DeepResourceGrid.SetAt invalidation");
        int baseline = truthExcluded.Count;

        fx.SetVein(fx.DynamicVeinCell, fx.AllowedOre, 300);
        List<IntVec3> truthAfterAdd = ComputeTruth(fx, true);
        IList<IntVec3> patchedAfterAdd = (IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn);
        Check("new vein is visible immediately (" + baseline + " -> " + truthAfterAdd.Count + ")", SameCells(patchedAfterAdd, truthAfterAdd));
        Check("a single new vein adds at most its 21 radial cells", truthAfterAdd.Count - baseline <= 21);
        Check("new vein cell is valuable for the barge", WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(fx.DynamicVeinCell, map, fx.BargePawn));

        fx.SetVein(fx.DynamicVeinCell, fx.ExcludedOre, 300);
        List<IntVec3> truthAfterExclude = ComputeTruth(fx, true);
        IList<IntVec3> patchedAfterExclude = (IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn);
        Check("re-defining the vein as excluded removes it from the barge list", SameCells(patchedAfterExclude, truthAfterExclude));
        Check("excluded vein is not valuable for the barge", !WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(fx.DynamicVeinCell, map, fx.BargePawn));
        Check("excluded vein is still valuable without exclusions", WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(fx.DynamicVeinCell, map, null));

        fx.SetVein(fx.DynamicVeinCell, fx.AllowedOre, 0);
        List<IntVec3> truthAfterDeplete = ComputeTruth(fx, true);
        IList<IntVec3> patchedAfterDeplete = (IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn);
        Check("depleting to 0 removes the cells again", SameCells(patchedAfterDeplete, truthAfterDeplete) && patchedAfterDeplete.Count == baseline);
        Check("depleted cell is not valuable for anyone", !WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(fx.DynamicVeinCell, map, fx.BargePawn)
            && !WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(fx.DynamicVeinCell, map, null));

        IList<IntVec3> beforeCountChange = (IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn);
        fx.SetVein(fx.AllowedVeinCell, fx.AllowedOre, 400);
        IList<IntVec3> afterCountChange = (IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn);
        Check("count-only change above zero keeps the list identical", ReferenceEquals(beforeCountChange, afterCountChange) && SameCells(afterCountChange, truthAfterDeplete));

        IList<IntVec3> snapshot = (IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn);
        int snapshotCount = snapshot.Count;
        fx.SetVein(fx.DynamicVeinCell, fx.AllowedOre, 300);
        Check("a previously returned list is never mutated", snapshot.Count == snapshotCount);
        fx.SetVein(fx.DynamicVeinCell, fx.AllowedOre, 0);
        Check("cache is consistent again after the snapshot check", SameCells((IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn), ComputeTruth(fx, true)));

        Section("GREEN - experimental 4-cells-per-tick scan disabled");
        fx.Settings.enableDrillingBargeExperimentalWorkLogic = true;
        int scanBeforeGreen = ReadScanIndex(component);
        IList<IntVec3> experimentalCells = (IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn);
        for (int i = 0; i < 10_000; i++)
        {
            component.MapComponentTick();
        }

        int scanAfterGreen = ReadScanIndex(component);
        IList<IntVec3> experimentalAfter = (IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn);
        Check("MapComponentTick no longer advances its scan (" + scanBeforeGreen + " -> " + scanAfterGreen + ")", scanAfterGreen == scanBeforeGreen);
        Check("upstream component cache stays empty", component.GetCachedCells().Count == 0);
        Check("experimental mode returns the same exact list", SameCells(experimentalAfter, ComputeTruth(fx, true)) && experimentalAfter.Count == experimentalCells.Count);
        fx.Settings.enableDrillingBargeExperimentalWorkLogic = false;

        Section("GREEN - guard fidelity");
        fx.Settings.enableDrillingBargeDeepDrill = false;
        Check("disabled deep drill setting yields nothing", CountCells(fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn)) == 0);
        fx.Settings.enableDrillingBargeDeepDrill = true;

        fx.Biome.hasBedrock = false;
        Check("biome without bedrock yields nothing", CountCells(fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn)) == 0);
        fx.Biome.hasBedrock = true;

        fx.SetVein(fx.AllowedVeinCell, fx.AllowedOre, 0);
        fx.SetVein(new IntVec3(fx.AllowedVeinCell.x + 1, 0, fx.AllowedVeinCell.z), fx.AllowedOre, 0);
        fx.SetVein(fx.ExcludedVeinCell, fx.ExcludedOre, 0);
        fx.SetVein(new IntVec3(0, 0, 0), fx.AllowedOre, 0);
        fx.SetVein(new IntVec3(Fixture.MapSize - 1, 0, Fixture.MapSize - 1), fx.AllowedOre, 0);
        Check("map without any deep resource yields nothing", CountCells(fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn)) == 0 && ComputeTruth(fx, true).Count == 0);

        fx.SetVein(fx.AllowedVeinCell, fx.AllowedOre, 500);
        Check("restored vein is visible again", SameCells((IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn), ComputeTruth(fx, true)));

        Check("final list still matches upstream after all mutations", SameCells((IList<IntVec3>)fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn), ComputeTruth(fx, true)));

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "ALL GREEN - " + checks + " checks passed"
            : failures + " of " + checks + " checks FAILED");
        return failures == 0 ? 0 : 1;
    }

    private static (int Cells, TimeSpan Elapsed) MeasureJobScan(Fixture fx)
    {
        // Warm up the Harmony detour and the JIT so the reported number is the steady-state cost.
        int warmup = 0;
        foreach (IntVec3 cell in fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn))
        {
            WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(cell, fx.Map, fx.BargePawn);
            if (++warmup >= 64)
            {
                break;
            }
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        int cells = 0;

        foreach (IntVec3 cell in fx.Giver.PotentialWorkCellsGlobal(fx.BargePawn))
        {
            cells++;
            WorkGiver_DrillingBargeDeepDrill.CellHasValuableDeepResource(cell, fx.Map, fx.BargePawn);
        }

        stopwatch.Stop();
        return (cells, stopwatch.Elapsed);
    }

    private static List<IntVec3> ComputeTruth(Fixture fx, bool applyExclusions)
    {
        Map map = fx.Map;
        List<IntVec3> result = new();

        for (int i = 0; i < map.cellIndices.NumGridCells; i++)
        {
            IntVec3 cell = map.cellIndices.IndexToCell(i);
            if (WorkGiver_DrillingBargeDeepDrill.TryGetDeepResource(cell, map, out _, out _, out _, applyExclusions))
            {
                result.Add(cell);
            }
        }

        return result;
    }

    private static int CountCells(IEnumerable<IntVec3> cells)
    {
        int count = 0;
        foreach (IntVec3 _ in cells)
        {
            count++;
        }

        return count;
    }

    private static bool SameCells(IList<IntVec3> actual, List<IntVec3> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (int i = 0; i < actual.Count; i++)
        {
            if (!actual[i].Equals(expected[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static int ReadScanIndex(DrillingBargeValidDrillCellsCacheComponent component)
    {
        return (int)typeof(DrillingBargeValidDrillCellsCacheComponent)
            .GetField("_scanIndex", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(component)!;
    }

    private static bool HasPatch(MethodInfo method, string owner, string patchMethodName, bool prefix)
    {
        Patches? info = Harmony.GetPatchInfo(method);
        if (info == null)
        {
            return false;
        }

        IReadOnlyCollection<HarmonyLib.Patch> patches = prefix ? info.Prefixes : info.Postfixes;
        foreach (HarmonyLib.Patch patch in patches)
        {
            if (patch.owner == owner && patch.PatchMethod.Name == patchMethodName)
            {
                return true;
            }
        }

        return false;
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("=== " + title + " ===");
    }

    private static void Check(string label, bool ok)
    {
        checks++;
        if (!ok)
        {
            failures++;
        }

        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + label);
    }
}
