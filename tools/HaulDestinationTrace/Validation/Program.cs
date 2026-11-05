using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;
using Verse.AI;
using WhiteGiver.HaulDestinationTrace;

internal static class EntryPoint
{
    private static readonly string GameRoot = Environment.GetEnvironmentVariable("RIMWORLD_ROOT") ?? @"A:\SteamLibrary\steamapps\common\RimWorld";
    private static readonly string HarmonyDirectory = Environment.GetEnvironmentVariable("HARMONY_DIRECTORY") ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|run QA_DIRECTORY");
            Console.WriteLine("Only bind writes QA copies; installed assemblies and user configuration stay unchanged.");
            return 0;
        }
        if (args.Length != 2 || args[0] is not ("bind" or "run"))
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }
        string bound = Path.Combine(Path.GetFullPath(args[1]), "bound");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { bound, GameRoot, HarmonyDirectory })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            if (args[0] == "bind") { Bind(bound); return 0; }
            Assembly.GetExecutingAssembly().GetType("Program", true)!
                .GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind(string bound)
    {
        if (bound.StartsWith(GameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("QA output must be outside the installed game.");
        Directory.CreateDirectory(bound);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(GameRoot);
        resolver.AddSearchDirectory(HarmonyDirectory);
        using var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(GameRoot, "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = resolver });
        foreach (string name in new[] { "Verse.ModsConfig", "Verse.ModLister", "Verse.BaseContent" })
        {
            MethodDefinition initializer = assembly.MainModule.GetType(name).Methods.Single(method => method.Name == ".cctor");
            initializer.Body.Instructions.Clear();
            initializer.Body.ExceptionHandlers.Clear();
            initializer.Body.Variables.Clear();
            initializer.Body.GetILProcessor().Emit(OpCodes.Ret);
        }
        assembly.Write(Path.Combine(bound, "Assembly-CSharp.dll"));
        Console.WriteLine("PASS: QA-only config/art initialization isolation; all observed job/path methods and installed assemblies unchanged.");
    }
}

internal static class Program
{
    private const string Owner = "whitegiver.diagnostics.hauldestination";
    private static readonly List<string> Logs = new();
    private static readonly List<Job> Created = new();
    private static readonly IntVec3 StoreCell = new(6, 0, 7);
    private static readonly Exception Sentinel = new InvalidOperationException("validation-sentinel");
    private static Map map = null!;
    private static Pawn pawn = null!;
    private static Thing item = null!;
    private static bool failCreation, throwLogging, reenterLogging, skipPath, throwPath, redirectPath;
    private static int pathCalls;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run()
    {
        var boundary = new Harmony("validation.hauldestination.boundaries");
        Hook(boundary, typeof(Log), nameof(Log.Message), nameof(Capture), new[] { typeof(string) });
        Hook(boundary, typeof(DefOfHelper), nameof(DefOfHelper.EnsureInitializedInCtor), nameof(Skip));
        Hook(boundary, typeof(HaulAIUtility), nameof(HaulAIUtility.CanHaulAside), nameof(CanHaul));
        Hook(boundary, typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath), nameof(PathBoundary));
        boundary.Patch(AccessTools.Method(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulAsideJobFor)),
            postfix: new HarmonyMethod(typeof(Program), nameof(RecordCreated)));
        Setup();
        new HaulDestinationTraceMod(null!);
        AssertRegistration();
        Check(Logs.Single().Contains("Loaded 1.0.0", StringComparison.Ordinal), "Missing startup receipt.");

        Job job = HaulAIUtility.HaulAsideJobFor(pawn, item);
        Check(job.targetB.Cell == StoreCell && job.haulMode == HaulMode.ToCellNonStorage && job.count == 99999,
            "Diagnostic changed actual haul-aside creation.");
        Check(Logs.Count == 1, "Normal creation produced log spam.");
        SetCurrent(job);
        skipPath = true;
        int calls = pathCalls;
        pawn.pather.StartPath(StoreCell, PathEndMode.ClosestTouch);
        Check(pathCalls == calls + 1 && Logs.Count == 1 && job.targetB.Cell == StoreCell, "Normal path outcome changed.");

        var before = job.targetB;
        job.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
        Check(!job.targetB.IsValid && before.Cell == StoreCell, "Real SetTarget did not retain its original behavior.");
        ExpectInvalidPath();
        string report = Logs.Last();
        Check(report.Contains("set_B before=", StringComparison.Ordinal) && report.Contains("path_enter", StringComparison.Ordinal) &&
              report.Contains("path_exit", StringComparison.Ordinal) && report.Contains("sameJob=True", StringComparison.Ordinal) &&
              report.Contains("exception=System.NullReferenceException", StringComparison.Ordinal), "Diagnostic omitted target transition or failure state.");
        Check(Reports() == 1 && PathsCount() == 0, "Failure report or path pairing incorrect.");
        Console.WriteLine("PASS: actual Mod/Harmony registration; real haul-aside creation, SetTarget and invalid GenPath exception unchanged; transition reported.");

        string defName = job.def.defName;
        int oldId = job.loadID;
        JobMaker.ReturnToPool(job);
        Check(job.loadID == -1 && job.def == null && !job.targetB.IsValid, "Diagnostic altered actual pooled Clear.");
        Job reused = HaulAIUtility.HaulAsideJobFor(pawn, item);
        Check(ReferenceEquals(job, reused) && reused.loadID != oldId && reused.def.defName == defName, "Fixture did not exercise actual pool reuse.");
        SetCurrent(reused);
        reused.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
        ExpectInvalidPath();
        report = Logs.Last();
        Check(report.Contains("prior_tail=", StringComparison.Ordinal) && report.Contains("clear_before", StringComparison.Ordinal) &&
              report.Contains("rebind_missing_create", StringComparison.Ordinal), "Pool generation history was lost.");
        Console.WriteLine("PASS: actual ReturnToPool/Clear and reused job generation remain distinct in diagnostic history.");

        Reset();
        Job prior = HaulAIUtility.HaulAsideJobFor(pawn, item);
        JobMaker.ReturnToPool(prior);
        Job wait = JobMaker.MakeJob(new JobDef { defName = "Wait", driverClass = typeof(JobDriver_Wait) }, 150);
        Check(ReferenceEquals(prior, wait), "Non-hauling reuse fixture did not reuse the same instance.");
        SetCurrent(wait);
        skipPath = true;
        pawn.pather.StartPath(LocalTargetInfo.Invalid, PathEndMode.ClosestTouch);
        Check(Reports() == 0 && PathsCount() == 0, "Reused non-hauling job consumed a diagnostic report.");
        Console.WriteLine("PASS: a former hauling instance reused for Wait is not mistaken for an invalid hauling task.");

        Reset();
        Job activeClear = HaulAIUtility.HaulAsideJobFor(pawn, item);
        SetCurrent(activeClear);
        activeClear.Clear();
        Check(activeClear.loadID == -1 && activeClear.def == null && !activeClear.targetB.IsValid,
            "Active Clear no longer clears the real job.");
        ExpectInvalidPath();
        Check(Reports() == 1 && PathsCount() == 0 && Logs.Last().Contains("clear_before", StringComparison.Ordinal) &&
              Logs.Last().Contains("callers=", StringComparison.Ordinal) && Logs.Last().Contains("exception=System.NullReferenceException", StringComparison.Ordinal),
            "Active Clear lost the original generation or clear caller evidence.");
        Console.WriteLine("PASS: a real active Job.Clear preserves its original trace through identity reset and propagates the path NRE.");

        Reset();
        Job unobserved = JobMaker.MakeJob(JobDefOf.HaulToCell, item, LocalTargetInfo.Invalid);
        SetCurrent(unobserved);
        ExpectInvalidPath();
        Check(Reports() == 1 && PathsCount() == 0 && Logs.Last().Contains("missing_creation_observation; producer_unknown", StringComparison.Ordinal),
            "A pre-existing hauling job failed without an explicit missing-creation diagnostic.");
        Console.WriteLine("PASS: an existing HaulToCell without a creation observation still reports its real failure, without inventing a producer.");

        Reset();
        failCreation = true;
        Check(HaulAIUtility.HaulAsideJobFor(pawn, item) == null && Reports() == 0, "Null creation result changed or was tracked.");
        failCreation = false;
        var bench = new Building_WorkTable { def = new ThingDef { defName = "ValidationBench", size = new IntVec2(1, 1), thingClass = typeof(Building_WorkTable) } };
        Put(bench, new IntVec3(3, 0, 3));
        map.thingGrid.ThingsListAt(bench.Position).Add(item);
        Job benchJob = WorkGiverUtility.HaulStuffOffBillGiverJob(pawn, bench, null);
        Check(Created.Contains(benchJob), "Outer bench method did not return the exact inner job.");
        SetCurrent(benchJob);
        benchJob.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
        ExpectInvalidPath();
        Check(Logs.Last().Contains("bench=(ValidationBench", StringComparison.Ordinal), "Bench association missing.");
        Console.WriteLine("PASS: actual workbench wrapper associates only the returned inner job; null creation remains null.");

        Reset();
        Job stepJob = HaulAIUtility.HaulAsideJobFor(pawn, item);
        SetCurrent(stepJob);
        var driver = pawn.jobs.curDriver;
        var toil = new Toil { actor = pawn, debugName = "ValidationClearB", defaultCompleteMode = ToilCompleteMode.PatherArrival };
        toil.initAction = () => stepJob.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
        AccessTools.Field(typeof(JobDriver), "toils").SetValue(driver, new List<Toil> { toil });
        driver.ReadyForNextToil();
        Check(driver.CurToilIndex == 0 && !stepJob.targetB.IsValid && Reports() == 1 &&
              Logs.Last().Contains("toil_after index=0", StringComparison.Ordinal), "Real toil transition was not observed.");
        Console.WriteLine("PASS: actual private toil transition observes a target cleared by initAction without changing the transition.");

        Reset();
        Job first = HaulAIUtility.HaulAsideJobFor(pawn, item);
        SetCurrent(first);
        pawn.jobs.curDriver.job = new Job { def = JobDefOf.HaulToCell, loadID = 90001, targetB = StoreCell };
        first.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
        ExpectInvalidPath();
        Check(Logs.Last().Contains("sameJob=False", StringComparison.Ordinal), "Driver/current job divergence missing.");
        Reset();
        Job finalizer = HaulAIUtility.HaulAsideJobFor(pawn, item);
        SetCurrent(finalizer);
        skipPath = true;
        throwPath = true;
        try { pawn.pather.StartPath(StoreCell, PathEndMode.ClosestTouch); throw new Exception("Sentinel exception disappeared."); }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, Sentinel)) { }
        Check(PathsCount() == 0 && Reports() == 1, "Finalizer did not preserve exception identity or unwind.");
        throwPath = false;
        redirectPath = true;
        pawn.pather.StartPath(StoreCell, PathEndMode.ClosestTouch);
        redirectPath = false;
        Check(PathsCount() == 0 && Reports() == 2, "Finalizer did not observe a later invalid input.");
        Console.WriteLine("PASS: current/driver identity mismatch and late target rewrite are reported; original exception object survives finalizer.");

        Reset();
        for (int i = 0; i < 150; i++) HaulAIUtility.HaulAsideJobFor(pawn, item);
        Check(GenerationCount() == 128 && JobsCount() <= 128 && Reports() == 0, "Trace cache is not bounded or normal jobs spammed.");
        Reset();
        for (int i = 0; i < 12; i++)
        {
            Job capped = HaulAIUtility.HaulAsideJobFor(pawn, item);
            SetCurrent(capped);
            capped.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
            ExpectInvalidPath();
        }
        Check(Reports() == 8 && PathsCount() == 0, "Report cap or path cleanup failed.");
        Console.WriteLine("PASS: 128-generation memory bound and eight-report session cap; unrelated/normal jobs do not spam.");

        Reset();
        Job logging = HaulAIUtility.HaulAsideJobFor(pawn, item);
        SetCurrent(logging);
        logging.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
        reenterLogging = true;
        ExpectInvalidPath();
        reenterLogging = false;
        Check(PathsCount() == 0, "Logging reentry leaked path frames.");
        Reset();
        logging = HaulAIUtility.HaulAsideJobFor(pawn, item);
        SetCurrent(logging);
        logging.SetTarget(TargetIndex.B, LocalTargetInfo.Invalid);
        throwLogging = true;
        ExpectInvalidPath();
        throwLogging = false;
        Check(PathsCount() == 0, "Logging failure leaked path frames.");
        Check(!GetField<bool>("disabled"), "Normal fixture disabled the observer.");
        Console.WriteLine("PASS: logging reentry and logging exceptions cannot suppress or replace the real path exception.");
        Console.WriteLine("NOT TESTED: live map movement, actual cooking UI, or the user's still-unattributed target invalidation.");
    }

    private static void Setup()
    {
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Current.Game = game;
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>());
        AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(game, new UniqueIDsManager());
        AccessTools.Field(typeof(Game), "tickManager").SetValue(game, new TickManager());
        AccessTools.Field(typeof(Game), "components").SetValue(game, new List<GameComponent>());
        JobDefOf.HaulToCell = new JobDef { defName = "HaulToCell", driverClass = typeof(JobDriver_HaulToCell) };
        map = new Map();
        map.uniqueID = 42;
        map.info.Size = new IntVec3(16, 1, 16);
        map.cellIndices = new CellIndices(map);
        map.thingGrid = new ThingGrid(map);
        game.Maps.Add(map);
        pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        pawn.def = new ThingDef { defName = "ValidationMech", thingClass = typeof(Pawn) };
        Put(pawn, new IntVec3(2, 0, 2));
        pawn.jobs = new Pawn_JobTracker(pawn);
        pawn.pather = new Pawn_PathFollower(pawn);
        pawn.stances = new Pawn_StanceTracker(pawn);
        item = new Thing { def = new ThingDef { defName = "ValidationMeat", thingClass = typeof(Thing), category = ThingCategory.Item } };
        Put(item, new IntVec3(3, 0, 3));
    }

    private static void Put(Thing thing, IntVec3 cell)
    {
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(thing, (sbyte)0);
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(thing, cell);
    }

    private static void SetCurrent(Job job)
    {
        pawn.jobs.curJob = job;
        pawn.jobs.curDriver = new JobDriver_HaulToCell { job = job, pawn = pawn };
    }

    private static void ExpectInvalidPath()
    {
        skipPath = false;
        try { pawn.pather.StartPath(LocalTargetInfo.Invalid, PathEndMode.ClosestTouch); throw new Exception("Actual path NRE was suppressed."); }
        catch (NullReferenceException exception)
        {
            Check(exception.StackTrace?.Contains("ShouldNotEnterCell", StringComparison.Ordinal) == true,
                "Fixture hit a different NRE.");
        }
    }

    private static void AssertRegistration()
    {
        var methods = Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true).ToArray();
        Check(methods.Length == 7, $"Expected seven observer targets, got {methods.Length}.");
        foreach (MethodBase method in methods)
        {
            Patches info = Harmony.GetPatchInfo(method)!;
            Check(info.Transpilers.All(patch => patch.owner != Owner), "Diagnostic registered a behavior-rewriting transpiler.");
            foreach (Patch patch in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers).Where(patch => patch.owner == Owner))
            {
                Check(patch.PatchMethod.ReturnType == typeof(void) && patch.PatchMethod.GetParameters().All(parameter => !parameter.ParameterType.IsByRef),
                    "Observer can mutate an argument/result/exception or skip the original.");
            }
        }
    }

    private static bool Capture(string text)
    {
        if (throwLogging) throw new InvalidOperationException("validation-log-failure");
        Logs.Add(text);
        if (reenterLogging) pawn.pather.StartPath(StoreCell, PathEndMode.OnCell);
        return false;
    }

    private static bool CanHaul(out IntVec3 storeCell, ref bool __result)
    {
        storeCell = StoreCell;
        __result = !failCreation;
        return false;
    }

    private static bool PathBoundary(ref LocalTargetInfo dest)
    {
        pathCalls++;
        if (throwPath) throw Sentinel;
        if (redirectPath) dest = LocalTargetInfo.Invalid;
        return !skipPath;
    }

    private static void RecordCreated(Job? __result) { if (__result != null) Created.Add(__result); }
    private static bool Skip() => false;
    private static Type TraceType => typeof(HaulDestinationTraceMod).Assembly.GetType("WhiteGiver.HaulDestinationTrace.TraceLog", true)!;
    private static T GetField<T>(string name) => (T)AccessTools.Field(TraceType, name).GetValue(null)!;
    private static int Count(string field) => (int)GetField<object>(field).GetType().GetProperty("Count")!.GetValue(GetField<object>(field))!;
    private static int GenerationCount() => Count("Generations");
    private static int JobsCount() => Count("Jobs");
    private static int PathsCount() => Count("Paths");
    private static int Reports() => Logs.Count(text => text.Contains("[HaulDestinationTrace] report=", StringComparison.Ordinal));
    private static void Reset()
    {
        TraceType.GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        Logs.Clear();
        Created.Clear();
        skipPath = throwPath = redirectPath = reenterLogging = throwLogging = failCreation = false;
    }

    private static void Hook(Harmony harmony, Type type, string method, string callback, Type[]? arguments = null) =>
        harmony.Patch(AccessTools.Method(type, method, arguments), prefix: new HarmonyMethod(typeof(Program), callback));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
