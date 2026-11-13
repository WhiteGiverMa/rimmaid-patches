using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using CeleTech.Base;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using WhiteGiver.CeleTechTurretUpgradePerfFix;

namespace CeleTechTurretUpgradePerfFix.Validation;

internal static class Tests
{
    private static int checks;

    internal static void Run(string mode, Type modType)
    {
        if (mode == "red")
        {
            RunRed();
        }
        else
        {
            RunGreen(modType);
        }

        Console.WriteLine();
        Console.WriteLine($"PASS: {checks} assertions.");
    }

    private static void RunRed()
    {
        Section("RED - upstream behaviour before the fix");
        Fixture fx = Fixture.Create();
        JobGiver_Work jobGiver = new();

        Check(!AnyOwnerPatches(), "RED loaded a patch owned by the mod.");
        Check(PawnCanUse(jobGiver, fx.Pawn, fx.Giver), "upstream PawnCanUseWorkGiver should accept WorkGiver_TurretUpgrade.");
        Check(fx.Giver.PotentialWorkThingsGlobal(fx.Pawn) == null, "upstream WorkGiver_TurretUpgrade should not override PotentialWorkThingsGlobal.");

        int calls = CountValidated(fx.Map, fx.Pawn, fx.Giver, out Thing? found);
        Check(calls == Fixture.WallCount, $"upstream validated {calls} BuildingArtificial candidates instead of {Fixture.WallCount} walls.");
        Check(found == null, "upstream found a job on a map with 5,000 walls and no CeleTech turrets.");
        Console.WriteLine($"       upstream zero-turret scan: {calls:N0} candidates validated, 0 jobs.");

        Building turret = fx.AddTurret(fx.Map, enqueue: true, postSpawnSetup: false);
        Check(fx.Giver.HasJobOnThing(fx.Pawn, turret), "upstream HasJobOnThing rejected a turret with a queued upgrade.");
        Job? upstreamJob = fx.Giver.JobOnThing(fx.Pawn, turret);
        Check(upstreamJob != null && ReferenceEquals(upstreamJob.targetB.Thing, turret), "upstream JobOnThing did not target the queued turret.");

        calls = CountValidated(fx.Map, fx.Pawn, fx.Giver, out found);
        Check(calls == Fixture.WallCount + 1, $"upstream validated {calls} candidates with one queued turret instead of {Fixture.WallCount + 1}.");
        Check(ReferenceEquals(found, turret), "upstream global scan did not end on the queued turret.");
        Console.WriteLine($"       upstream one-turret scan: {calls:N0} candidates validated (5,000 walls + 1 turret).");
    }

    private static void RunGreen(Type modType)
    {
        Section("GREEN - applying CeleTechTurretUpgradePerfFix");
        Fixture fx = Fixture.Create();
        JobGiver_Work jobGiver = new();
        int calls;

        object mod = Activator.CreateInstance(modType, new object?[] { null })!;
        Check(mod != null, "Mod constructor returned null.");

        MethodInfo pawnCanUse = AccessTools.Method(
            typeof(JobGiver_Work),
            "PawnCanUseWorkGiver",
            new[] { typeof(Pawn), typeof(WorkGiver) })!;
        MethodInfo potentialThings = AccessTools.DeclaredMethod(
                typeof(WorkGiver_TurretUpgrade),
                "PotentialWorkThingsGlobal",
                new[] { typeof(Pawn) })
            ?? AccessTools.Method(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.PotentialWorkThingsGlobal), new[] { typeof(Pawn) })!;
        MethodInfo hasJob = AccessTools.Method(
            typeof(WorkGiver_TurretUpgrade),
            "HasJobOnThing",
            new[] { typeof(Pawn), typeof(Thing), typeof(bool) })!;
        MethodInfo postSpawn = AccessTools.Method(
            typeof(Comp_TurretUpgrade),
            "PostSpawnSetup",
            new[] { typeof(bool) })!;

        Check(HasPatch(pawnCanUse, "PawnCanUseWorkGiverPostfix", prefix: false), "PawnCanUseWorkGiver postfix is missing.");
        Check(HasPatch(potentialThings, "PotentialWorkThingsGlobalPrefix", prefix: true), "PotentialWorkThingsGlobal prefix is missing.");
        Check(HasPatch(hasJob, "HasJobOnThingPrefix", prefix: true), "HasJobOnThing prefix is missing.");
        Check(HasPatch(postSpawn, "PostSpawnSetupPostfix", prefix: false), "PostSpawnSetup postfix is missing.");

        HashSet<MethodBase> owned = Harmony.GetAllPatchedMethods()
            .Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Program.Owner) == true)
            .ToHashSet();
        Check(owned.SetEquals(new MethodBase[] { pawnCanUse, potentialThings, hasJob, postSpawn }), "The mod patched a method outside the four intended targets.");
        Console.WriteLine("       four patches registered on the intended methods only.");

        // The exact reported scenario: 5,000 walls, zero CeleTech turrets. The first query initializes the
        // empty registry and must answer without touching a single wall.
        Check(!PawnCanUse(jobGiver, fx.Pawn, fx.Giver), "PawnCanUseWorkGiver accepted 5,000 walls with zero turrets.");
        Check(!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map), "zero-turret map reports queued upgrades.");
        IReadOnlyList<Thing>? noTurrets = MapTurretUpgradeTracker.GetQueuedTurrets(fx.Map);
        Check(noTurrets != null && noTurrets.Count == 0, "zero-turret map produced a non-empty search set.");
        IReadOnlyList<Thing>? noTurretsGiver = fx.Giver.PotentialWorkThingsGlobal(fx.Pawn) as IReadOnlyList<Thing>;
        Check(noTurretsGiver != null && noTurretsGiver.Count == 0, "zero-turret giver produced a non-empty search set.");
        Check(CountValidatedSet(noTurretsGiver!, fx.Pawn, fx.Giver, out _) == 0, "zero-turret map still validated walls.");
        Console.WriteLine("       zero-turret case: 5,000 walls present, 0 candidates validated.");

        // A turret spawned after the tracker was initialized must be registered by the PostSpawnSetup postfix.
        Building turret = fx.AddTurret(fx.Map, enqueue: true, postSpawnSetup: true);
        Comp_TurretUpgrade comp = turret.GetComp<Comp_TurretUpgrade>();

        Check(MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map), "PostSpawnSetup postfix did not register the queued turret.");
        IReadOnlyList<Thing>? queued = MapTurretUpgradeTracker.GetQueuedTurrets(fx.Map);
        Check(queued != null && queued.Count == 1 && ReferenceEquals(queued[0], turret), "queued-turret list is not exactly the one turret.");
        Check(PawnCanUse(jobGiver, fx.Pawn, fx.Giver), "PawnCanUseWorkGiver was rejected while a turret had a queued upgrade.");
        IReadOnlyList<Thing>? viaGiver = fx.Giver.PotentialWorkThingsGlobal(fx.Pawn) as IReadOnlyList<Thing>;
        Check(viaGiver != null && viaGiver.Count == 1 && ReferenceEquals(viaGiver[0], turret), "PotentialWorkThingsGlobal did not return exactly the queued turret.");

        calls = CountValidatedSet(queued!, fx.Pawn, fx.Giver, out Thing? found);
        Check(calls == 1 && ReferenceEquals(found, turret), $"patched search validated {calls} things instead of only the queued turret.");
        Console.WriteLine("       queued case: 1 candidate (the turret), 0 walls.");

        Check(fx.Giver.HasJobOnThing(fx.Pawn, turret), "patched HasJobOnThing no longer accepts the queued turret.");
        Job? job = fx.Giver.JobOnThing(fx.Pawn, turret);
        Check(job != null && ReferenceEquals(job.targetB.Thing, turret), "patched JobOnThing does not return the upstream job.");
        Check(!fx.Giver.HasJobOnThing(fx.Pawn, fx.Walls[0]), "HasJobOnThing accepted a plain wall.");

        // Completion must immediately turn the giver off.
        Check(comp.TryCompleteFromCarried(fx.Upgrade, null), "TryCompleteFromCarried failed for a no-cost upgrade.");
        Check(!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map), "completed upgrade is still reported as queued.");
        Check(!PawnCanUsePrivate(jobGiver, fx.Pawn, fx.Giver), "PawnCanUseWorkGiver still accepts the giver after completion.");
        IReadOnlyList<Thing>? empty = MapTurretUpgradeTracker.GetQueuedTurrets(fx.Map);
        Check(empty != null && empty.Count == 0, "queued-turret list is not empty after completion.");
        calls = CountValidatedSet(empty!, fx.Pawn, fx.Giver, out _);
        Check(calls == 0, "patched search still validated candidates after completion.");
        IReadOnlyList<Thing>? completedGlobal = fx.Giver.PotentialWorkThingsGlobal(fx.Pawn) as IReadOnlyList<Thing>;
        Check(completedGlobal != null && completedGlobal.Count == 0, "PotentialWorkThingsGlobal is not empty after completion.");
        Check(!fx.Giver.HasJobOnThing(fx.Pawn, turret), "HasJobOnThing still accepts the completed turret.");

        // An unspawned pawn has no map; the postfix must leave the upstream answer alone.
        fx.SetDespawned(fx.Pawn);
        Check(PawnCanUse(jobGiver, fx.Pawn, fx.Giver), "unspawned pawn no longer follows the upstream path.");
        fx.SetSpawned(fx.Pawn, 0);
        Console.WriteLine("       completed case: 0 candidates, 0 walls; unspawned-pawn fallback preserved.");

        // Re-enqueue with a different def (the first one is already applied), then despawn: the pruner must
        // drop it, and spawn setup must add it back.
        comp.Enqueue(fx.Upgrade2);
        Check(MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map), "re-enqueued upgrade was not observed.");
        fx.SetDespawned(turret);
        Check(!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map), "despawned turret is still reported as queued.");
        fx.SetSpawned(turret, 0);
        comp.PostSpawnSetup(true);
        Check(MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map), "respawned turret was not registered again.");

        comp.DevComplete(fx.Upgrade2);
        Check(!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map), "DevComplete did not clear the queued state.");

        // A brand-new turret spawned after the tracker was initialized must be registered by the postfix.
        comp.Enqueue(fx.Upgrade3);
        fx.SetDestroyed(turret);
        Check(!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map), "destroyed turret is still reported as queued.");

        Building second = fx.AddTurret(fx.Map, enqueue: true, postSpawnSetup: true);
        IReadOnlyList<Thing>? secondList = MapTurretUpgradeTracker.GetQueuedTurrets(fx.Map);
        Check(secondList != null && secondList.Count == 1 && ReferenceEquals(secondList[0], second), "newly spawned turret was not registered incrementally.");
        Check(!secondList!.Contains(turret), "destroyed turret was not pruned from the registry.");
        Console.WriteLine("       despawn/respawn/destroy/new-spawn registration all agree with upstream state.");
        second.GetComp<Comp_TurretUpgrade>().DevComplete(fx.Upgrade);

        // A second map must get its own lazily initialized registry.
        Map secondMap = fx.CreateAdditionalMap();
        Building third = fx.AddTurret(secondMap, enqueue: true, postSpawnSetup: false);
        Check(MapTurretUpgradeTracker.HasAnyQueuedUpgrade(secondMap), "second map was not lazily initialized.");
        IReadOnlyList<Thing>? thirdList = MapTurretUpgradeTracker.GetQueuedTurrets(secondMap);
        Check(thirdList != null && thirdList.Count == 1 && ReferenceEquals(thirdList[0], third), "second map registry is not isolated.");
        Pawn secondPawn = fx.NewPawn(1, new IntVec3(2, 0, 2));
        Check(PawnCanUse(jobGiver, secondPawn, fx.Giver), "second-map pawn was rejected while its turret had a queued upgrade.");
        IReadOnlyList<Thing>? secondGlobal = fx.Giver.PotentialWorkThingsGlobal(secondPawn) as IReadOnlyList<Thing>;
        Check(secondGlobal != null && secondGlobal.Count == 1 && ReferenceEquals(secondGlobal[0], third), "second-map PotentialWorkThingsGlobal is not isolated.");

        // The prefix is scoped to WorkGiver_TurretUpgrade; other scanners still get the upstream null.
        ValidationScanner other = new() { def = new WorkGiverDef { defName = "Validation_OtherScanner" } };
        Check(other.PotentialWorkThingsGlobal(fx.Pawn) == null, "PotentialWorkThingsGlobal prefix leaked to a non-CeleTech scanner.");

        Check(!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(null), "null map should not report queued upgrades.");
        Check(MapTurretUpgradeTracker.GetQueuedTurrets(null) == null, "null map should not produce a search set.");

        // A map with no CeleTech turrets at all: first query initializes to an empty registry.
        Map emptyMap = fx.CreateAdditionalMap();
        Pawn emptyPawn = fx.NewPawn(2, new IntVec3(3, 0, 3));
        Check(!PawnCanUse(jobGiver, emptyPawn, fx.Giver), "PawnCanUseWorkGiver accepted a map with zero turrets.");
        Check(!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(emptyMap), "zero-turret map reports queued upgrades.");
        IReadOnlyList<Thing>? emptyMapGlobal = fx.Giver.PotentialWorkThingsGlobal(emptyPawn) as IReadOnlyList<Thing>;
        Check(emptyMapGlobal != null && emptyMapGlobal.Count == 0, "zero-turret map produced a non-empty search set.");
        Check(CountValidatedSet(emptyMapGlobal!, emptyPawn, fx.Giver, out _) == 0, "zero-turret map validated candidates.");

        MeasureZeroQueued(fx);
    }

    private static void MeasureZeroQueued(Fixture fx)
    {
        _ = fx.AddTurret(fx.Map, enqueue: false, postSpawnSetup: true);
        Stopwatch stopwatch = Stopwatch.StartNew();
        const int iterations = 100_000;
        for (int i = 0; i < iterations; i++)
        {
            _ = MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map);
        }

        stopwatch.Stop();
        Check(!MapTurretUpgradeTracker.HasAnyQueuedUpgrade(fx.Map), "zero-queued map still reports work.");
        Console.WriteLine($"       O(1) zero-queued tracker query: {stopwatch.Elapsed.TotalMilliseconds / iterations * 1000:F3} us/call over {iterations:N0} calls.");
    }

    private static bool PawnCanUse(JobGiver_Work jobGiver, Pawn pawn, WorkGiver giver)
    {
        return PawnCanUsePrivate(jobGiver, pawn, giver);
    }

    private static bool PawnCanUsePrivate(JobGiver_Work jobGiver, Pawn pawn, WorkGiver giver)
    {
        MethodInfo method = AccessTools.Method(
            typeof(JobGiver_Work),
            "PawnCanUseWorkGiver",
            new[] { typeof(Pawn), typeof(WorkGiver) })!;
        try
        {
            return (bool)method.Invoke(jobGiver, new object?[] { pawn, giver })!;
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException("PawnCanUseWorkGiver threw: " + exception.InnerException, exception.InnerException ?? exception);
        }
    }

    private static int CountValidated(Map map, Pawn pawn, WorkGiver_TurretUpgrade giver, out Thing? found)
    {
        List<Thing> candidates = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
        return CountValidatedSet(candidates, pawn, giver, out found);
    }

    private static int CountValidatedSet(IEnumerable<Thing> candidates, Pawn pawn, WorkGiver_TurretUpgrade giver, out Thing? found)
    {
        int calls = 0;
        found = GenClosest.ClosestThing_Global(
            pawn.Position,
            candidates,
            99_999f,
            thing =>
            {
                calls++;
                return giver.HasJobOnThing(pawn, thing);
            });
        return calls;
    }

    private static bool AnyOwnerPatches()
    {
        return Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Program.Owner) == true);
    }

    private static bool HasPatch(MethodInfo method, string patchName, bool prefix)
    {
        Patches? info = Harmony.GetPatchInfo(method);
        if (info == null)
        {
            return false;
        }

        IEnumerable<Patch> patches = prefix ? info.Prefixes : info.Postfixes;
        return patches.Any(patch => patch.owner == Program.Owner && patch.PatchMethod.Name == patchName);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        checks++;
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("== " + title + " ==");
    }
}

internal sealed class ValidationScanner : WorkGiver_Scanner
{
}
