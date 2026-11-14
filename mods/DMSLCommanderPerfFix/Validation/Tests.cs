using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMS_Legion;
using DMS_Legion.AXF12;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using WhiteGiver.DMSL.CommanderPerfFix;

namespace WhiteGiver.DMSL.CommanderPerfFix.Validation;

internal static class Tests
{
    private const string UpstreamHarmonyId = "DMS_Legion";
    private const string ProbeHarmonyId = "whitegiver.dmsl.commanderperffix.validation.probes";

    private static readonly Type UtilityType = AccessTools.TypeByName("DMS_Legion.CommanderAutonomyUtility")!;
    private static readonly Type LifecycleType = AccessTools.TypeByName("DMS_Legion.CommanderAutonomyLifecycle")!;
    private static readonly MethodInfo OverseerGetter = AccessTools.DeclaredMethod(typeof(CompOverseerSubject), "get_Overseer")!;
    private static readonly Func<CompOverseerSubject, Pawn?> GetSubjectOverseer =
        (Func<CompOverseerSubject, Pawn?>)OverseerGetter.CreateDelegate(typeof(Func<CompOverseerSubject, Pawn?>));
    private static readonly Func<Pawn, bool> IsAutonomous =
        (Func<Pawn, bool>)AccessTools.DeclaredMethod(UtilityType, "IsAutonomous", new[] { typeof(Pawn) })!
            .CreateDelegate(typeof(Func<Pawn, bool>));
    private static readonly Func<Pawn, Pawn?> FindOverseer =
        (Func<Pawn, Pawn?>)AccessTools.DeclaredMethod(UtilityType, "FindOverseer", new[] { typeof(Pawn) })!
            .CreateDelegate(typeof(Func<Pawn, Pawn?>));
    private static readonly Func<Pawn, Pawn, bool> HasAssignment =
        (Func<Pawn, Pawn, bool>)AccessTools.DeclaredMethod(LifecycleType, "HasAssignment", new[] { typeof(Pawn), typeof(Pawn) })!
            .CreateDelegate(typeof(Func<Pawn, Pawn, bool>));

    private static FieldInfo compQueries = null!;
    private static FieldInfo componentQueries = null!;
    private static PropertyInfo reconScans = null!;
    private static int checks;
    private static int failures;
    private static int shouldRemoveCalls;

    internal static void Run(string mode)
    {
        Type probes = typeof(Game).Assembly.GetType("DMSLCommanderPerfFix.Validation.Probes", throwOnError: true)!;
        compQueries = probes.GetField("CompQueries", BindingFlags.Public | BindingFlags.Static)!;
        componentQueries = probes.GetField("ComponentQueries", BindingFlags.Public | BindingFlags.Static)!;
        reconScans = typeof(CommanderPerfFix).Assembly.GetType("WhiteGiver.DMSL.CommanderPerfFix.ReconManagerCache", throwOnError: true)!
            .GetProperty("ScanCount", BindingFlags.NonPublic | BindingFlags.Static)!;

        switch (mode)
        {
            case "red":
                Red();
                break;
            case "green":
                Green();
                break;
            default:
                throw new ArgumentException("Unknown mode: " + mode);
        }

        Console.WriteLine();
        Console.WriteLine($"{(failures == 0 ? "OK" : "FAILED")}: {checks - failures}/{checks} checks passed.");
        if (failures != 0)
        {
            throw new InvalidOperationException($"{failures} validation check(s) failed.");
        }
    }

    private static void Red()
    {
        Info("=== RED: exact real Legion prefixes, no DMSLCommanderPerfFix patches ===");
        InstallUpstreamHotspots();
        InstallRemovalProbe();
        Fixture fixture = Fixture.Create();

        Require(fixture.NonCommander.AllComps.Count == Fixture.CompCount, "non-commander fixture has exactly 20 real ThingComps");
        Require(!typeof(CompMechCommanderMarker).IsSealed, "marker comp is non-sealed, so a compsByType miss falls through to the loop");
        Dictionary<Type, ThingComp[]> byType = (Dictionary<Type, ThingComp[]>)AccessTools
            .DeclaredField(typeof(ThingWithComps), "compsByType")!.GetValue(fixture.NonCommander)!;
        Require(!byType.ContainsKey(typeof(CompMechCommanderMarker)), "non-commander compsByType has no marker entry");
        Require(fixture.NonCommanderSubject.State == OverseerSubjectState.RequiresOverseer, "upstream non-commander state baseline");
        Require(GetSubjectOverseer(fixture.NonCommanderSubject) == null, "upstream non-commander overseer baseline");

        int compBefore = CompQueries();
        Stopwatch watch = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            _ = fixture.NonCommanderSubject.State;
            _ = GetSubjectOverseer(fixture.NonCommanderSubject);
        }

        watch.Stop();
        int compDelta = CompQueries() - compBefore;
        Info($"RED 100 State + 100 Overseer: {compDelta} marker-miss GetComp calls, each taking the 20-comp fallback loop; {watch.Elapsed.TotalMilliseconds:0.###} ms");
        Require(compDelta >= 200, "RED: every non-commander State/Overseer query reaches the marker comps walk");
        Require(compDelta * Fixture.CompCount >= 4000, "RED: at least 4,000 ThingComp type checks are forced by the tiny query sample");

        fixture.Settings.enableCommanderAutonomy = false;
        Require(FindOverseer(fixture.Commander) == fixture.Overseer, "upstream non-autonomous commander finds its assigned mechanitor");
        for (int i = 0; i < 100; i++)
        {
            _ = FindOverseer(fixture.Commander);
        }

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            _ = FindOverseer(fixture.Commander);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        Info($"RED 2,000 x FindOverseer(non-autonomous): {allocated:N0} B ({allocated / 2000d:0.0} B/call)");
        Require(allocated > 0, "RED: nested HasAssignment predicates allocate closures on every lookup");

        Require(fixture.Observed.HasMap, "observed settlement really has a registered map");
        int componentBefore = ComponentQueries();
        int shouldBefore = shouldRemoveCalls;
        watch.Restart();
        foreach (Settlement settlement in fixture.Settlements)
        {
            settlement.CheckRemoveMapNow();
        }

        watch.Stop();
        int componentDelta = ComponentQueries() - componentBefore;
        Info($"RED 1,300 x CheckRemoveMapNow: {componentDelta} Game.GetComponent walks; {watch.Elapsed.TotalMilliseconds:0.###} ms");
        Require(componentDelta == Fixture.SettlementCount, "RED: Legion performs one Game.components lookup per settlement per tick");
        Require(shouldRemoveCalls == shouldBefore, "RED: the observed map is blocked before Settlement.ShouldRemoveMapNow");
        Require(fixture.Game.Maps.Contains(fixture.ObservedMap), "RED: observed map remains registered");
    }

    private static void Green()
    {
        Info("=== GREEN: real Legion PatchAll, then normal DMSLCommanderPerfFix.Apply(true) ===");
        Fixture fixture = Fixture.Create();
        Type upstreamBootstrap = AccessTools.TypeByName("DMS_Legion.HarmonyPatches")!;
        RuntimeHelpers.RunClassConstructor(upstreamBootstrap.TypeHandle);
        InstallRemovalProbe();

        fixture.Settings.enableCommanderAutonomy = true;
        OverseerSubjectState referenceAutonomousState = fixture.CommanderSubject.State;
        Pawn? referenceAutonomousOverseer = GetSubjectOverseer(fixture.CommanderSubject);
        fixture.Settings.enableCommanderAutonomy = false;
        OverseerSubjectState referenceControlledState = fixture.CommanderSubject.State;
        Pawn? referenceControlledOverseer = GetSubjectOverseer(fixture.CommanderSubject);
        Require(referenceAutonomousState == OverseerSubjectState.Overseen && referenceAutonomousOverseer == null,
            "reference autonomous commander: State=Overseen, Overseer=null");
        Require(referenceControlledState == OverseerSubjectState.Overseen && referenceControlledOverseer == fixture.Overseer,
            "reference non-autonomous commander: assigned mechanitor retained, State=Overseen");

        int installed = CommanderPerfFix.Apply(ensureUpstreamPatched: true);
        Info("Installed patch groups: " + string.Join(", ", CommanderPerfFix.InstalledPatches));
        Require(installed == 11, "all 11 optimization patches install");
        VerifyReplacements();

        fixture.Settings.enableCommanderAutonomy = true;
        Require(fixture.CommanderSubject.State == referenceAutonomousState, "GREEN autonomous State parity");
        Require(GetSubjectOverseer(fixture.CommanderSubject) == referenceAutonomousOverseer, "GREEN autonomous Overseer=null parity");
        Require(MechanitorUtility.GetOverseer(fixture.Commander) == null, "GREEN autonomous MechanitorUtility.GetOverseer=null parity");
        Require(IsAutonomous(fixture.Commander), "GREEN autonomous utility remains true");

        fixture.Settings.enableCommanderAutonomy = false;
        Require(fixture.CommanderSubject.State == referenceControlledState, "GREEN non-autonomous State parity");
        Require(GetSubjectOverseer(fixture.CommanderSubject) == referenceControlledOverseer, "GREEN assigned Overseer parity");
        Require(MechanitorUtility.GetOverseer(fixture.Commander) == fixture.Overseer, "GREEN assigned MechanitorUtility.GetOverseer parity");
        Require(!IsAutonomous(fixture.Commander), "GREEN disabled autonomy remains false");
        Require(HasAssignment(fixture.Overseer, fixture.Commander), "GREEN allocation-free HasAssignment finds the real assignment");

        Require(fixture.NonCommanderSubject.State == OverseerSubjectState.RequiresOverseer, "GREEN non-commander State parity");
        Require(GetSubjectOverseer(fixture.NonCommanderSubject) == null, "GREEN non-commander Overseer parity");
        Require(!CompMechCommanderMarker.PawnHasMarker(fixture.NonCommander), "GREEN non-commander marker is false without a comps walk");

        Pawn future = fixture.CreateDynamicMarkerPawn();
        Require(CompMechCommanderMarker.PawnHasMarker(future), "DefCount change rebuilds the set and supports a future/XML-added commander def");

        // Warm every path before allocation measurements (including CWT and all Harmony detours).
        fixture.Settings.enableCommanderAutonomy = true;
        for (int i = 0; i < 200_000; i++)
        {
            WarmNonCommander(fixture);
        }

        Stopwatch watch = Stopwatch.StartNew();
        watch.Stop();
        int compBefore = CompQueries();
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        watch.Restart();
        int mismatches = 0;
        for (int i = 0; i < 200_000; i++)
        {
            if (CompMechCommanderMarker.PawnHasMarker(fixture.NonCommander)
                || IsAutonomous(fixture.NonCommander)
                || fixture.NonCommanderSubject.State != OverseerSubjectState.RequiresOverseer
                || GetSubjectOverseer(fixture.NonCommanderSubject) != null
                || MechanitorUtility.GetOverseer(fixture.NonCommander) != null)
            {
                mismatches++;
            }
        }

        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        int compDelta = CompQueries() - compBefore;
        Info($"GREEN 200,000 iterations of five non-commander queries: {allocated} B, {compDelta} GetComp calls, {mismatches} mismatches; {watch.Elapsed.TotalMilliseconds:0.###} ms");
        Require(mismatches == 0, "GREEN 200,000-iteration non-commander result parity");
        Require(compDelta == 0, "GREEN non-commander marker-def gate causes zero comps scans");
        Require(allocated == 0, "GREEN non-commander hot path is exactly zero-allocation");

        fixture.Settings.enableCommanderAutonomy = false;
        for (int i = 0; i < 2000; i++)
        {
            WarmControlledCommander(fixture);
        }

        compBefore = CompQueries();
        allocBefore = GC.GetAllocatedBytesForCurrentThread();
        watch.Restart();
        mismatches = 0;
        for (int i = 0; i < 200_000; i++)
        {
            if (FindOverseer(fixture.Commander) != fixture.Overseer
                || !HasAssignment(fixture.Overseer, fixture.Commander)
                || GetSubjectOverseer(fixture.CommanderSubject) != fixture.Overseer
                || fixture.CommanderSubject.State != OverseerSubjectState.Overseen
                || MechanitorUtility.GetOverseer(fixture.Commander) != fixture.Overseer)
            {
                mismatches++;
            }
        }

        watch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        compDelta = CompQueries() - compBefore;
        Info($"GREEN 200,000 iterations of five controlled-commander queries: {allocated} B, {compDelta} GetComp calls, {mismatches} mismatches; {watch.Elapsed.TotalMilliseconds:0.###} ms");
        Require(mismatches == 0, "GREEN 200,000-iteration controlled-commander result parity");
        Require(compDelta == 0, "GREEN per-pawn CWT removes repeated commander comps lookups");
        Require(allocated == 0, "GREEN FindOverseer/HasAssignment nested closures are completely eliminated");

        int scansBefore = ReconScans();
        int componentsBefore = ComponentQueries();
        int shouldBefore = shouldRemoveCalls;
        watch.Restart();
        foreach (Settlement settlement in fixture.Settlements)
        {
            settlement.CheckRemoveMapNow();
        }

        watch.Stop();
        int scansDelta = ReconScans() - scansBefore;
        int componentsDelta = ComponentQueries() - componentsBefore;
        Info($"GREEN first 1,300 x CheckRemoveMapNow: {scansDelta} real list walk + {componentsDelta} original Game.GetComponent calls; {watch.Elapsed.TotalMilliseconds:0.###} ms");
        Require(scansDelta == 1 && componentsDelta == 0, "GREEN first world pass performs exactly one components lookup total");
        Require(shouldRemoveCalls == shouldBefore, "GREEN observed-map prefix still blocks the real removal path");
        Require(fixture.Game.Maps.Contains(fixture.ObservedMap), "GREEN observed map remains registered");

        scansBefore = ReconScans();
        componentsBefore = ComponentQueries();
        foreach (Settlement settlement in fixture.Settlements)
        {
            settlement.CheckRemoveMapNow();
        }

        Require(ReconScans() == scansBefore && ComponentQueries() == componentsBefore,
            "GREEN second 1,300-settlement pass performs zero components lookups");
        Require(ReferenceEquals(AXF12ReconMissionManager.Instance, fixture.Manager), "GREEN cached Instance returns the exact real manager");

        List<GameComponent> previousComponents = fixture.Game.components;
        fixture.Game.components = new List<GameComponent>(previousComponents);
        scansBefore = ReconScans();
        Require(ReferenceEquals(AXF12ReconMissionManager.Instance, fixture.Manager), "GREEN replacement components list still resolves the same manager");
        Require(ReconScans() == scansBefore + 1, "GREEN components-list identity change invalidates the cache");
        fixture.Game.components = previousComponents;
        _ = AXF12ReconMissionManager.Instance;

        RunParallelPhase(fixture, autonomous: true);
        RunParallelPhase(fixture, autonomous: false);
        Require(shouldRemoveCalls == shouldBefore, "GREEN parallel observed-map reads never enter removal");

        int again = CommanderPerfFix.Apply(ensureUpstreamPatched: true);
        Require(again == installed, "GREEN patch installation is idempotent");
    }

    private static void WarmNonCommander(Fixture fixture)
    {
        _ = CompMechCommanderMarker.PawnHasMarker(fixture.NonCommander);
        _ = IsAutonomous(fixture.NonCommander);
        _ = fixture.NonCommanderSubject.State;
        _ = GetSubjectOverseer(fixture.NonCommanderSubject);
        _ = MechanitorUtility.GetOverseer(fixture.NonCommander);
    }

    private static void WarmControlledCommander(Fixture fixture)
    {
        _ = FindOverseer(fixture.Commander);
        _ = HasAssignment(fixture.Overseer, fixture.Commander);
        _ = GetSubjectOverseer(fixture.CommanderSubject);
        _ = fixture.CommanderSubject.State;
        _ = MechanitorUtility.GetOverseer(fixture.Commander);
    }

    private static void RunParallelPhase(Fixture fixture, bool autonomous)
    {
        fixture.Settings.enableCommanderAutonomy = autonomous;
        const int workerCount = 16;
        const int iterations = 20_000;
        int mismatches = 0;
        int errors = 0;
        Exception? firstError = null;
        using ManualResetEventSlim start = new(false);
        Task[] workers = new Task[workerCount];
        for (int worker = 0; worker < workerCount; worker++)
        {
            workers[worker] = Task.Factory.StartNew(() =>
            {
                try
                {
                    start.Wait();
                    for (int i = 0; i < iterations; i++)
                    {
                        bool mismatch = CompMechCommanderMarker.PawnHasMarker(fixture.NonCommander)
                            || IsAutonomous(fixture.NonCommander)
                            || fixture.NonCommanderSubject.State != OverseerSubjectState.RequiresOverseer
                            || GetSubjectOverseer(fixture.NonCommanderSubject) != null;

                        if (autonomous)
                        {
                            mismatch |= !IsAutonomous(fixture.Commander)
                                || fixture.CommanderSubject.State != OverseerSubjectState.Overseen
                                || GetSubjectOverseer(fixture.CommanderSubject) != null
                                || !CompMechCommanderMarker.PawnHasMarker(fixture.ColdCommander)
                                || fixture.ColdCommanderSubject.State != OverseerSubjectState.Overseen
                                || GetSubjectOverseer(fixture.ColdCommanderSubject) != null;
                        }
                        else
                        {
                            mismatch |= IsAutonomous(fixture.Commander)
                                || FindOverseer(fixture.Commander) != fixture.Overseer
                                || fixture.CommanderSubject.State != OverseerSubjectState.Overseen
                                || GetSubjectOverseer(fixture.CommanderSubject) != fixture.Overseer;
                        }

                        mismatch |= !ReferenceEquals(AXF12ReconMissionManager.Instance, fixture.Manager);
                        fixture.Observed.CheckRemoveMapNow();
                        if (mismatch)
                        {
                            Interlocked.Increment(ref mismatches);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref errors);
                    Interlocked.CompareExchange(ref firstError, exception, null);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        start.Set();
        Task.WaitAll(workers);
        Info($"GREEN 16-thread {(autonomous ? "autonomous" : "controlled")} stress: {workerCount * iterations:N0} iterations, {errors} errors, {mismatches} mismatches");
        if (firstError != null)
        {
            Info("First parallel error: " + firstError);
        }

        Require(errors == 0 && mismatches == 0, $"GREEN 16-thread {(autonomous ? "autonomous" : "controlled")} reads are exception-free and equivalent");
    }

    private static void InstallUpstreamHotspots()
    {
        Harmony harmony = new(UpstreamHarmonyId);
        PatchUpstream(harmony,
            AccessTools.DeclaredMethod(typeof(CompOverseerSubject), "get_State")!,
            "DMS_Legion.CommanderAutonomy_State");
        PatchUpstream(harmony,
            OverseerGetter,
            "DMS_Legion.CommanderAutonomy_SubjectOverseer");
        PatchUpstream(harmony,
            AccessTools.DeclaredMethod(typeof(MechanitorUtility), "GetOverseer", new[] { typeof(Pawn) })!,
            "DMS_Legion.CommanderAutonomy_Overseer");
        PatchUpstream(harmony,
            AccessTools.DeclaredMethod(typeof(MapParent), "CheckRemoveMapNow")!,
            "DMS_Legion.AXF12.AXF12ObservedMap_CheckRemoveMapNow_Patch");
    }

    private static void PatchUpstream(Harmony harmony, MethodInfo original, string patchTypeName)
    {
        Type patchType = AccessTools.TypeByName(patchTypeName)
            ?? throw new TypeLoadException(patchTypeName);
        MethodInfo prefix = AccessTools.DeclaredMethod(patchType, "Prefix")
            ?? throw new MissingMethodException(patchTypeName, "Prefix");
        harmony.Patch(original, prefix: new HarmonyMethod(prefix));
    }

    private static void InstallRemovalProbe()
    {
        Harmony harmony = new(ProbeHarmonyId);
        MethodInfo shouldRemove = AccessTools.DeclaredMethod(typeof(Settlement), "ShouldRemoveMapNow")!;
        harmony.Patch(shouldRemove, prefix: new HarmonyMethod(typeof(Tests), nameof(ShouldRemoveProbe)));
    }

    private static bool ShouldRemoveProbe(ref bool __result, out bool alsoRemoveWorldObject)
    {
        Interlocked.Increment(ref shouldRemoveCalls);
        alsoRemoveWorldObject = false;
        __result = false;
        return false;
    }

    private static void VerifyReplacements()
    {
        VerifyReplacement(typeof(CompOverseerSubject), "get_State", "DMS_Legion.CommanderAutonomy_State", "SubjectStatePrefix");
        VerifyReplacement(typeof(CompOverseerSubject), "get_Overseer", "DMS_Legion.CommanderAutonomy_SubjectOverseer", "SubjectOverseerPrefix");
        VerifyReplacement(typeof(MechanitorUtility), "GetOverseer", "DMS_Legion.CommanderAutonomy_Overseer", "MechanitorGetOverseerPrefix");
        VerifyReplacement(typeof(MapParent), "CheckRemoveMapNow", "DMS_Legion.AXF12.AXF12ObservedMap_CheckRemoveMapNow_Patch", null);
    }

    private static void VerifyReplacement(Type owner, string methodName, string upstreamTypeName, string? replacementName)
    {
        MethodInfo original = AccessTools.DeclaredMethod(owner, methodName)!;
        MethodInfo upstream = AccessTools.DeclaredMethod(AccessTools.TypeByName(upstreamTypeName)!, "Prefix")!;
        Require(!HasPatch(original, upstream), "upstream hot prefix detached exactly: " + upstreamTypeName);

        MethodInfo replacement = replacementName == null
            ? AccessTools.DeclaredMethod(AccessTools.TypeByName("WhiteGiver.DMSL.CommanderPerfFix.ReconManagerCache")!, "CheckRemoveMapNowPrefix")!
            : AccessTools.DeclaredMethod(AccessTools.TypeByName("WhiteGiver.DMSL.CommanderPerfFix.CommanderPatches")!, replacementName)!;
        Require(HasPatch(original, replacement), "lean replacement attached: " + owner.Name + "." + methodName);
    }

    private static bool HasPatch(MethodBase original, MethodInfo method)
    {
        Patches? info = Harmony.GetPatchInfo(original);
        if (info == null)
        {
            return false;
        }

        foreach (Patch patch in info.Prefixes)
        {
            if (patch.PatchMethod == method)
            {
                return true;
            }
        }

        return false;
    }

    private static int CompQueries() => (int)compQueries.GetValue(null)!;
    private static int ComponentQueries() => (int)componentQueries.GetValue(null)!;
    private static int ReconScans() => (int)reconScans.GetValue(null)!;

    private static void Info(string message) => Console.WriteLine(message);

    private static void Require(bool condition, string message)
    {
        checks++;
        if (condition)
        {
            Console.WriteLine("  PASS  " + message);
            return;
        }

        failures++;
        Console.WriteLine("  FAIL  " + message);
    }
}
