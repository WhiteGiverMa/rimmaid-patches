using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

internal static class Program
{
    private static int Main(string[] args) => Bootstrap.MainEntry(args);
}

internal static class Bootstrap
{
    private static string gameRoot = "";
    private static string qaRoot = "";
    private static string harmonyPath = "";

    internal static int MainEntry(string[] args)
    {
        if (args.Length != 5)
        {
            Console.Error.WriteLine("Usage: Validation <game-root> <qa-root> <harmony-dll> <mod-dll> <legacy|maintained>");
            return 2;
        }
        gameRoot = Path.GetFullPath(args[0]);
        qaRoot = Path.GetFullPath(args[1]);
        harmonyPath = Path.GetFullPath(args[2]);
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string path = name.Name switch
            {
                "Assembly-CSharp" => Path.Combine(qaRoot, "Assembly-CSharp.dll"),
                "0Harmony" => harmonyPath,
                _ => Path.Combine(gameRoot, name.Name + ".dll")
            };
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        try
        {
            typeof(Program).Assembly.GetType("Driver", true)!.GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, [Path.GetFullPath(args[3]), args[4] == "legacy", qaRoot]);
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

}

internal static class Driver
{
    private static bool legacy;

    private static Assembly mod = null!;
    private static Type settingsType = null!;
    private static object settings = null!;
    private static MethodInfo postfix = null!;
    private static Pawn pawn = null!;
    private static JobGiver_Work giver = null!;
    private static readonly Dictionary<WorkTypeDef, int> Priorities = new();
    private static readonly List<Job> Returned = new();
    private static readonly HashSet<IntVec3> Forbidden = new();
    private static readonly HashSet<IntVec3> Unreachable = new();
    private static int reachChecks;
    private static int assertions;
    private static bool colonyWorker = true;
    private static bool mentalState;
    private static Job? endedJob;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(string modPath, bool legacyMode, string qaRoot)
    {
        legacy = legacyMode;
        settingsRoot = Path.Combine(qaRoot, "settings-fixture");
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>());
        game.uniqueIDsManager = new UniqueIDsManager();
        game.components = new List<GameComponent>();
        game.tickManager = new TickManager();
        Current.Game = game;
        AccessTools.Field(typeof(Prefs), "data").SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));
        var fixture = new Harmony("whitegiver.nearby.validation");
        Prefix(fixture, AccessTools.Method(typeof(Log), nameof(Log.Error), [typeof(string)]), nameof(UnexpectedError));
        Prefix(fixture, AccessTools.Method(typeof(Log), nameof(Log.Warning), [typeof(string)]), nameof(UnexpectedError));
        Prefix(fixture, AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonist)), nameof(IsColonist));
        Prefix(fixture, AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonyMechPlayerControlled)), nameof(False));
        Prefix(fixture, AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.InMentalState)), nameof(InMentalState));
        Prefix(fixture, AccessTools.Method(typeof(Pawn), nameof(Pawn.WorkTypeIsDisabled)), nameof(False));
        Prefix(fixture, AccessTools.Method(typeof(JobGiver_Work), "PawnCanUseWorkGiver"), nameof(CanUse));
        Prefix(fixture, AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.GetPriority)), nameof(Priority));
        Prefix(fixture, AccessTools.Method(typeof(RimWorld.ForbidUtility), nameof(RimWorld.ForbidUtility.IsForbidden), [typeof(IntVec3), typeof(Pawn)]), nameof(IsForbidden));
        Prefix(fixture, AccessTools.Method(typeof(ReachabilityUtility), nameof(ReachabilityUtility.CanReach), [typeof(Pawn), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(Danger), typeof(bool), typeof(bool), typeof(TraverseMode)]), nameof(CanReach));
        fixture.Patch(AccessTools.Method(typeof(JobMaker), nameof(JobMaker.ReturnToPool)), postfix: new HarmonyMethod(typeof(Driver), nameof(RecordReturn)));
        fixture.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob)), prefix: new HarmonyMethod(typeof(Driver), nameof(RecordEnd)));
        Prefix(fixture, AccessTools.Method(typeof(Pawn_JobTracker), "TryFindAndStartJob"), nameof(Skip));

        mod = Assembly.LoadFrom(modPath);
        settingsType = mod.GetType("MjRimMods.WhileYouAreNearby.SimpleSettings", true)!;
        settings = Activator.CreateInstance(settingsType, true)!;
        mod.GetType("MjRimMods.WhileYouAreNearby.Utils", true)!.GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, settings);
        postfix = mod.GetType("MjRimMods.WhileYouAreNearby.JobGiver_Work_TryIssueJobPackagePatch", true)!.GetMethod("After_TryIssueJobPackage", BindingFlags.Static | BindingFlags.NonPublic)!;
        Check(GetSetting("longDistanceThreshold") == 32 && GetSetting("maxSearchRadius") == 12 && GetSetting("priorityBoost") == 1 && GetSetting("vanillaChancePercentage") == 5, "original numeric defaults retained");
        SetSetting("vanillaChancePercentage", 0);
        pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(pawn, IntVec3.Zero);
        pawn.mindState = (Pawn_MindState)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_MindState));
        pawn.mindState.priorityWork = (PriorityWork)RuntimeHelpers.GetUninitializedObject(typeof(PriorityWork));
        AccessTools.Field(typeof(PriorityWork), "prioritizedCell").SetValue(pawn.mindState.priorityWork, IntVec3.Invalid);
        pawn.workSettings = new Pawn_WorkSettings(pawn);
        pawn.jobs = new Pawn_JobTracker(pawn);
        pawn.stances = new Pawn_StanceTracker(pawn);
        pawn.carryTracker = new Pawn_CarryTracker(pawn);
        pawn.def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        pawn.def.race = new RaceProperties();
        giver = (JobGiver_Work)RuntimeHelpers.GetUninitializedObject(typeof(JobGiver_Work));
        AccessTools.Field(typeof(Pawn_WorkSettings), "workGiversDirty").SetValue(pawn.workSettings, false);
        Console.WriteLine($"Mode: {(legacy ? "legacy RED evidence" : "maintained GREEN")}. Real mod method; environment boundaries are fixtures, not a TPS benchmark.");

        TestCells();
        TestGuards();
        TestSelection();
        if (!legacy)
        {
            TestOwnership();
            TestThings(fixture);
            TestSettings();
            TestPatchBinding(fixture);
        }
        Console.WriteLine($"PASS: {assertions} checks. No real config/save I/O or game startup.");
    }

    private static void TestCells()
    {
        CountingScanner scanner = Scanner("Cells");
        for (int i = 0; i < 10000; i++) scanner.Cells.Add(new IntVec3(40 + i % 250, 0, 40 + i / 250));
        scanner.Cells.Add(new IntVec3(10, 0, 0));
        scanner.Cells.Add(new IntVec3(4, 0, 0));
        Job old = Original(scanner);
        ThinkResult result = Invoke(old, scanner);
        Check(!ReferenceEquals(old, result.Job) && result.Job.targetA.Cell == Cell(4), "same nearby target at distance 4");
        Check(scanner.Enumerated == 10002, "candidate enumeration is still global");
        Check(scanner.Checks == (legacy ? 10002 : 2) && reachChecks == (legacy ? 10002 : 2), "expensive cell checks are distance-filtered only in maintained build");
        Check(legacy ? result.Job.workGiverDef == null : result.Job.workGiverDef == scanner.def, "replacement work giver metadata");
        Check(Returned.Count == (legacy ? 0 : 1) && (legacy || ReferenceEquals(Returned[0], old)), "discarded original returned exactly once only in maintained build");
        Job replacement = result.Job;
        pawn.jobs.curJob = replacement;
        pawn.jobs.curDriver = new JobDriver_Wait { pawn = pawn, job = replacement };
        int freeBefore = SimplePool<Job>.FreeItemsCount;
        pawn.jobs.Notify_WorkTypeDisabled(scanner.def.workType);
        Check(legacy ? endedJob == null : ReferenceEquals(endedJob, replacement), "real work-type cancellation recognizes maintained replacement");
        if (!legacy)
        {
            Check(pawn.jobs.curJob == null && pawn.jobs.curDriver == null, "real EndCurrentJob cleanup clears job and driver");
            Check(replacement.def == null && replacement.loadID == -1 && SimplePool<Job>.FreeItemsCount == freeBefore + 1, "real cancellation returns replacement to native pool");
        }
        pawn.jobs.curJob = null;
        pawn.jobs.curDriver = null;
        Console.WriteLine($"CELLS: enumerated={scanner.Enumerated}, checks={scanner.Checks}, reach={reachChecks}, pool returns={Returned.Count}");
    }

    private static void TestGuards()
    {
        CountingScanner scanner = Scanner("Guards");
        scanner.Cells.Add(Cell(4));
        NoScan(Original(scanner, 10), scanner, "short original job");
        NoScan(null, scanner, "NoJob");
        giver.emergency = true;
        NoScan(Original(scanner), scanner, "emergency work");
        giver.emergency = false;
        colonyWorker = false;
        NoScan(Original(scanner), scanner, "non-colony worker");
        colonyWorker = true;
        mentalState = true;
        NoScan(Original(scanner), scanner, "mental state");
        mentalState = false;
        SetSetting("vanillaChancePercentage", 100);
        NoScan(Original(scanner), scanner, "100 percent vanilla chance");
        SetSetting("vanillaChancePercentage", 0);
        AccessTools.Field(typeof(PriorityWork), "prioritizedCell").SetValue(pawn.mindState.priorityWork, Cell(1));
        NoScan(Original(scanner), scanner, "player-prioritized work");
        AccessTools.Field(typeof(PriorityWork), "prioritizedCell").SetValue(pawn.mindState.priorityWork, IntVec3.Invalid);
        Job noSource = Original(scanner);
        noSource.workGiverDef = null;
        NoScan(noSource, scanner, "non-work job");
        SetExclusion("excludedLongDistanceWorkTypes", scanner.def.workType, true);
        NoScan(Original(scanner), scanner, "excluded distant work type");
        SetExclusion("excludedLongDistanceWorkTypes", scanner.def.workType, false);
        SetExclusion("excludedNearbyWorkTypes", scanner.def.workType, true);
        NoScan(Original(scanner), scanner, "excluded nearby work type");
        SetExclusion("excludedNearbyWorkTypes", scanner.def.workType, false);
        scanner.Usable = false;
        NoScan(Original(scanner), scanner, "work giver permission gate");
        scanner.Usable = true;
    }

    private static void TestSelection()
    {
        CountingScanner scanner = Scanner("Selection");
        scanner.Cells.AddRange([Cell(12), Cell(11), Cell(4)]);
        Forbidden.Add(Cell(11));
        Unreachable.Add(Cell(4));
        Job old = Original(scanner);
        ThinkResult result = Invoke(old, scanner);
        Check(ReferenceEquals(old, result.Job) && Returned.Count == 0, "radius boundary, forbidden and unreachable cells retain original");
        scanner.UnreachableAllowed = true;
        result = Invoke(Original(scanner), scanner);
        Check(result.Job.targetA.Cell == Cell(4) && reachChecks == 0, "cell AllowUnreachable bypass remains intact");
        Forbidden.Clear();
        Unreachable.Clear();
        scanner.UnreachableAllowed = false;
        scanner.Cells.Clear();
        scanner.Cells.Add(Cell(4));
        scanner.ReturnNull = true;
        old = Original(scanner);
        result = Invoke(old, scanner);
        Check(ReferenceEquals(old, result.Job) && old.workGiverDef == scanner.def && Returned.Count == 0, "failed replacement keeps original intact");
        scanner.ReturnNull = false;

        CountingScanner lower = Scanner("LowerPriority");
        lower.Cells.Add(Cell(2));
        Priorities[lower.def.workType] = 3;
        scanner.Cells.Clear();
        old = Original(scanner);
        result = Invoke(old, scanner, lower);
        Check(ReferenceEquals(result.Job, old) && lower.Enumerated == 0, "priority boost limit stops lower-priority work");
        Priorities[lower.def.workType] = 2;
        result = Invoke(Original(scanner), scanner, lower);
        Check(result.Job.targetA.Cell == Cell(2), "priority boost permits next priority tier");
        CountingScanner first = Scanner("DistanceScoreFirst");
        CountingScanner later = Scanner("DistanceScoreLater");
        first.Cells.Add(Cell(10));
        later.Cells.Add(Cell(9));
        result = Invoke(Original(first), first, later);
        Check(result.Job.targetA.Cell == Cell(10), "original cross-scanner distance score retained");
    }

    private static void TestOwnership()
    {
        CountingScanner scanner = Scanner("Ownership");
        scanner.Cells.Add(Cell(4));
        Job old = Original(scanner);
        scanner.ExistingJob = old;
        ThinkResult result = Invoke(old, scanner);
        Check(ReferenceEquals(result.Job, old) && Returned.Count == 0 && old.workGiverDef == scanner.def, "same-reference replacement must not be pooled");
        scanner.ExistingJob = null;
        old = Original(scanner);
        Invoke(old, scanner);
        Check(old.def == null && old.workGiverDef == null && !old.targetA.IsValid, "actual JobMaker pool clears discarded job");
        old = Original(scanner);
        pawn.jobs.curJob = old;
        Invoke(old, scanner);
        Check(Returned.Count == 0 && old.workGiverDef == scanner.def, "current job is never pooled");
        pawn.jobs.curJob = null;
        old = Original(scanner);
        var driver = (JobDriver_Wait)RuntimeHelpers.GetUninitializedObject(typeof(JobDriver_Wait));
        driver.job = old;
        pawn.jobs.curDriver = driver;
        Invoke(old, scanner);
        Check(Returned.Count == 0 && old.workGiverDef == scanner.def, "driver-owned job is never pooled");
        pawn.jobs.curDriver = null;
        old = Original(scanner);
        pawn.jobs.jobQueue.EnqueueLast(old);
        Invoke(old, scanner);
        Check(Returned.Count == 0 && pawn.jobs.jobQueue.Contains(old) && old.workGiverDef == scanner.def, "queued job is never pooled");
        pawn.jobs.jobQueue.Extract(old);
        old = Original(scanner);
        ThinkResult queued = InvokeResult(new ThinkResult(old, giver, fromQueue: true), scanner);
        Check(Returned.Count == 0 && !ReferenceEquals(queued.Job, old) && old.workGiverDef == scanner.def, "FromQueue result is never pooled");
        scanner.ThrowOnCreate = true;
        old = Original(scanner);
        try
        {
            Invoke(old, scanner);
            throw new InvalidOperationException("Expected fixture creation exception");
        }
        catch (TargetInvocationException exception) when (exception.InnerException is FixtureException)
        {
            Check(Returned.Count == 0 && old.workGiverDef == scanner.def, "creation exception leaves original owned and intact");
        }
        scanner.ThrowOnCreate = false;
    }

    private static void TestThings(Harmony fixture)
    {
        // Exercise the unchanged native global search; only map reachability/forbidden boundaries are stubbed.
        Prefix(fixture, AccessTools.Method(typeof(GenClosest), "EarlyOutSearch"), nameof(False));
        Prefix(fixture, AccessTools.PropertyGetter(typeof(Thing), nameof(Thing.Map)), nameof(FixtureMap));
        Prefix(fixture, AccessTools.Method(typeof(RimWorld.ForbidUtility), nameof(RimWorld.ForbidUtility.IsForbidden), [typeof(Thing), typeof(Pawn)]), nameof(False));
        Prefix(fixture, AccessTools.Method(typeof(Reachability), nameof(Reachability.CanReach), [typeof(IntVec3), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(TraverseParms)]), nameof(True));
        Prefix(fixture, AccessTools.Method(typeof(TraverseParms), nameof(TraverseParms.For), [typeof(Pawn), typeof(Danger), typeof(TraverseMode), typeof(bool), typeof(bool), typeof(bool), typeof(bool)]), nameof(FixtureTraverseParms));
        var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
        map.reachability = (Reachability)RuntimeHelpers.GetUninitializedObject(typeof(Reachability));
        Find.Maps.Add(map);
        thingMap = map;
        CountingScanner scanner = Scanner("Things");
        scanner.def.scanCells = false;
        scanner.def.scanThings = true;
        scanner.Things.AddRange([ThingAt(30), ThingAt(10), ThingAt(4)]);
        Job old = Original(scanner);
        ThinkResult result = Invoke(old, scanner);
        Check(ReferenceEquals(result.Job.targetA.Thing, scanner.Things[2]), "thing branch uses actual GenClosest nearest reachable search");
        Check(result.Job.workGiverDef == scanner.def && Returned.Count == 1 && ReferenceEquals(Returned[0], old), "thing replacement metadata and old-job return");
        Check(scanner.ThingChecks == 2 && scanner.ThingJobs == 1, "thing distance filtering and single final job creation retained");
    }

    private static void TestSettings()
    {
        string root = settingsRoot;
        Directory.CreateDirectory(root);
        AccessTools.Field(typeof(GenFilePaths), "saveDataPath").SetValue(null, root);
        Prefix(new Harmony("whitegiver.nearby.validation.types"), AccessTools.PropertyGetter(typeof(GenTypes), "AllActiveAssemblies"), nameof(ActiveAssemblies));
        MethodInfo read = typeof(LoadedModManager).GetMethod(nameof(LoadedModManager.ReadModSettings))!.MakeGenericMethod(settingsType);
        object restored = read.Invoke(null, ["MissingFixture", "ModMain"])!;
        Check((int)settingsType.GetField("longDistanceThreshold")!.GetValue(restored)! == 32, "missing settings retain defaults via native loader");
        settings = Activator.CreateInstance(settingsType, true)!;
        mod.GetType("MjRimMods.WhileYouAreNearby.Utils", true)!.GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, settings);
        var excludedWork = new WorkTypeDef { defName = "FixtureExcludedWork" };
        DefDatabase<WorkTypeDef>.Add(excludedWork);
        SetExclusion("excludedNearbyWorkTypes", excludedWork, true);
        SetExclusion("excludedLongDistanceWorkTypes", excludedWork, true);
        settingsType.GetField("longDistanceThreshold")!.SetValue(settings, 45);
        settingsType.GetField("maxSearchRadius")!.SetValue(settings, 9);
        settingsType.GetField("priorityBoost")!.SetValue(settings, 2);
        settingsType.GetField("vanillaChancePercentage")!.SetValue(settings, 17);
        LoadedModManager.WriteModSettings("Fixture", "ModMain", (ModSettings)settings);
        restored = read.Invoke(null, ["Fixture", "ModMain"])!;
        Check((int)settingsType.GetField("longDistanceThreshold")!.GetValue(restored)! == 45
            && (int)settingsType.GetField("maxSearchRadius")!.GetValue(restored)! == 9
            && (int)settingsType.GetField("priorityBoost")!.GetValue(restored)! == 2
            && (int)settingsType.GetField("vanillaChancePercentage")!.GetValue(restored)! == 17, "native Scribe settings round trip");
        Check(ExclusionContains(restored, "excludedNearbyWorkTypes", excludedWork)
            && ExclusionContains(restored, "excludedLongDistanceWorkTypes", excludedWork), "native Scribe retains enabled Def-backed exclusions");
        string oldFormat = "<?xml version=\"1.0\"?><SettingsBlock><ModSettings Class=\"MjRimMods.WhileYouAreNearby.SimpleSettings\"><ExcludedLongDistanceWorkTypesSavedDefs/><ExcludedNearbyWorkTypesSavedDefs/></ModSettings></SettingsBlock>";
        File.WriteAllText(Path.Combine(root, "Config", "Mod_Legacy_ModMain.xml"), oldFormat);
        restored = read.Invoke(null, ["Legacy", "ModMain"])!;
        Check((int)settingsType.GetField("longDistanceThreshold")!.GetValue(restored)! == 32
            && (int)settingsType.GetField("maxSearchRadius")!.GetValue(restored)! == 12
            && (int)settingsType.GetField("priorityBoost")!.GetValue(restored)! == 1
            && (int)settingsType.GetField("vanillaChancePercentage")!.GetValue(restored)! == 5, "original settings class and omitted default fields remain readable");
        settingsType.GetField("longDistanceThreshold")!.SetValue(restored, -1);
        settingsType.GetField("maxSearchRadius")!.SetValue(restored, 0);
        settingsType.GetField("priorityBoost")!.SetValue(restored, -2);
        settingsType.GetField("vanillaChancePercentage")!.SetValue(restored, 999);
        Scribe.mode = LoadSaveMode.PostLoadInit;
        ((ModSettings)restored).ExposeData();
        Scribe.mode = LoadSaveMode.Inactive;
        Check((int)settingsType.GetField("longDistanceThreshold")!.GetValue(restored)! == 1
            && (int)settingsType.GetField("maxSearchRadius")!.GetValue(restored)! == 1
            && (int)settingsType.GetField("priorityBoost")!.GetValue(restored)! == 0
            && (int)settingsType.GetField("vanillaChancePercentage")!.GetValue(restored)! == 100, "settings file boundary clamps invalid numeric input");
    }

    private static void TestPatchBinding(Harmony fixture)
    {
        MethodInfo original = AccessTools.Method(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage));
        CountingScanner scanner = Scanner("PatchedEntry");
        scanner.Cells.Add(Cell(4));
        Job old = Original(scanner);
        entryResult = new ThinkResult(old, giver);
        ResetInvocation(scanner);
        SetSetting("longDistanceThreshold", 32);
        SetSetting("vanillaChancePercentage", 0);
        Prefix(fixture, original, nameof(OriginalEntry));
        FieldInfo settingsField = mod.GetType("MjRimMods.WhileYouAreNearby.Utils", true)!.GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic)!;
        settingsField.SetValue(null, null);
        Type modType = mod.GetType("MjRimMods.WhileYouAreNearby.ModMain", true)!;
        object modInstance = Activator.CreateInstance(modType, new object?[] { null })!;
        Check(settingsField.GetValue(null) == null, "real Mod constructor defers settings until definitions are available");
        Check(((Mod)modInstance).SettingsCategory() == "While You Are Nearby - Maintained", "maintained mod exposes its settings category");
        settingsField.SetValue(null, settings);
        Patches patches = Harmony.GetPatchInfo(original);
        Check(patches.Postfixes.Count(patch => patch.PatchMethod.DeclaringType?.Assembly == mod) == 1, "one real Harmony postfix binds to native JobGiver entry");
        Check(Harmony.GetAllPatchedMethods().Count(method => Harmony.GetPatchInfo(method).Postfixes.Any(patch => patch.PatchMethod.DeclaringType?.Assembly == mod)) == 1, "no GenClosest debug hooks installed");
        ThinkResult result = giver.TryIssueJobPackage(pawn, default);
        Check(result.Job.targetA.Cell == Cell(4) && result.Job.workGiverDef == scanner.def && Returned.Count == 1, "actual patched JobGiver invocation reaches maintained behavior");
    }

    private static void NoScan(Job? job, CountingScanner scanner, string name)
    {
        ThinkResult result = Invoke(job, scanner);
        Check(ReferenceEquals(result.Job, job) && scanner.Enumerated == 0 && Returned.Count == 0, name + " leaves original untouched");
    }

    private static CountingScanner Scanner(string name)
    {
        var workType = new WorkTypeDef { defName = name };
        Priorities[workType] = 1;
        return new CountingScanner { def = new WorkGiverDef { defName = name, workType = workType, scanCells = true, scanThings = false } };
    }

    private static readonly JobDef FixtureJobDef = new() { defName = "FixtureJob", driverClass = typeof(JobDriver_Wait) };

    private static Job Original(CountingScanner scanner, int distance = 100)
    {
        Job job = JobMaker.MakeJob(FixtureJobDef, Cell(distance));
        job.workGiverDef = scanner.def;
        return job;
    }

    private static ThinkResult Invoke(Job? job, params CountingScanner[] scanners)
        => InvokeResult(job == null ? ThinkResult.NoJob : new ThinkResult(job, giver), scanners);

    private static ThinkResult InvokeResult(ThinkResult result, params CountingScanner[] scanners)
    {
        ResetInvocation(scanners);
        object?[] arguments = [result, giver, pawn, default(JobIssueParams)];
        postfix.Invoke(null, arguments);
        return (ThinkResult)arguments[0]!;
    }

    private static void ResetInvocation(params CountingScanner[] scanners)
    {
        Returned.Clear();
        endedJob = null;
        reachChecks = 0;
        foreach (CountingScanner scanner in scanners) scanner.Checks = scanner.Enumerated = scanner.Jobs = 0;
        AccessTools.Field(typeof(Pawn_WorkSettings), "workGiversInOrderNormal").SetValue(pawn.workSettings, scanners.Cast<WorkGiver>().ToList());
    }

    private static void SetExclusion(string field, WorkTypeDef workType, bool enabled)
    {
        object selection = settingsType.GetField(field)!.GetValue(settings)!;
        FieldInfo enabledField = AccessTools.Field(selection.GetType(), legacy ? "_enabled" : "Enabled");
        FieldInfo defsField = AccessTools.Field(selection.GetType(), legacy ? "_defs" : "Defs");
        enabledField.SetValue(selection, enabled);
        ((HashSet<WorkTypeDef>)defsField.GetValue(selection)!).Add(workType);
    }

    private static bool ExclusionContains(object targetSettings, string field, WorkTypeDef workType)
    {
        object selection = settingsType.GetField(field)!.GetValue(targetSettings)!;
        return (bool)selection.GetType().GetMethod("Contains")!.Invoke(selection, [workType])!;
    }

    private static void SetSetting(string name, int value) => settingsType.GetField(name)!.SetValue(settings, value);
    private static int GetSetting(string name) => (int)settingsType.GetField(name)!.GetValue(settings)!;
    private static IntVec3 Cell(int x) => new(x, 0, 0);
    private static Thing ThingAt(int x)
    {
        var thing = (Thing)RuntimeHelpers.GetUninitializedObject(typeof(Thing));
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(thing, Cell(x));
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(thing, (sbyte)0);
        return thing;
    }
    private static Map? thingMap;
    private static string settingsRoot = "";
    private static ThinkResult entryResult;
    private static bool FixtureMap(ref Map? __result) { __result = thingMap; return false; }
    private static bool ActiveAssemblies(ref IEnumerable<Assembly> __result) { __result = [typeof(Pawn).Assembly, mod]; return false; }
    private static bool True(ref bool __result) { __result = true; return false; }
    private static bool FixtureTraverseParms(ref TraverseParms __result) { __result = default; return false; }
    private static bool OriginalEntry(ref ThinkResult __result) { __result = entryResult; return false; }
    private static bool IsColonist(ref bool __result) { __result = colonyWorker; return false; }
    private static bool InMentalState(ref bool __result) { __result = mentalState; return false; }
    private static bool False(ref bool __result) { __result = false; return false; }
    private static bool CanUse(WorkGiver giver, ref bool __result) { __result = giver is not CountingScanner scanner || scanner.Usable; return false; }
    private static bool Priority(WorkTypeDef w, ref int __result) { __result = Priorities[w]; return false; }
    private static bool IsForbidden(IntVec3 c, ref bool __result) { __result = Forbidden.Contains(c); return false; }
    private static bool CanReach(LocalTargetInfo dest, ref bool __result) { reachChecks++; __result = !Unreachable.Contains(dest.Cell); return false; }
    private static void RecordReturn(Job job) => Returned.Add(job);
    private static void RecordEnd(Pawn_JobTracker __instance) => endedJob = __instance.curJob;
    private static bool Skip() => false;
    private static bool UnexpectedError(string text) => throw new InvalidOperationException("Unexpected game log: " + text);
    private static void Prefix(Harmony harmony, MethodInfo target, string method) => harmony.Patch(target, prefix: new HarmonyMethod(typeof(Driver), method));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        assertions++;
        Console.WriteLine("PASS: " + message);
    }

    private sealed class CountingScanner : WorkGiver_Scanner
    {
        public readonly List<IntVec3> Cells = new();
        public int Enumerated;
        public int Checks;
        public int Jobs;
        public bool Usable = true;
        public bool ReturnNull;
        public bool ThrowOnCreate;
        public bool UnreachableAllowed;
        public Job? ExistingJob;
        public readonly List<Thing> Things = new();
        public int ThingChecks;
        public int ThingJobs;
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Undefined);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) => Things;
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) { ThingChecks++; return true; }
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false) { ThingJobs++; return JobMaker.MakeJob(FixtureJobDef, thing); }
        public override bool AllowUnreachable => UnreachableAllowed;
        public override Danger MaxPathDanger(Pawn pawn) => Danger.None;
        public override IEnumerable<IntVec3> PotentialWorkCellsGlobal(Pawn pawn)
        {
            foreach (IntVec3 cell in Cells) { Enumerated++; yield return cell; }
        }
        public override bool HasJobOnCell(Pawn pawn, IntVec3 c, bool forced = false) { Checks++; return true; }
        public override Job? JobOnCell(Pawn pawn, IntVec3 cell, bool forced = false)
        {
            Jobs++;
            if (ThrowOnCreate) throw new FixtureException();
            return ReturnNull ? null : ExistingJob ?? JobMaker.MakeJob(FixtureJobDef, cell);
        }
    }

    private sealed class FixtureException : Exception;
}
