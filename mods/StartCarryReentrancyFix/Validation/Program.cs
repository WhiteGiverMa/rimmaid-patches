using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;
using Mono.Cecil.Cil;
using RimWorld;
using Verse;
using Verse.AI;
using WhiteGiver.StartCarryReentrancyFix;

internal static class Entry
{
    private static readonly string GameRoot = Environment.GetEnvironmentVariable("RIMWORLD_ROOT") ?? @"A:\SteamLibrary\steamapps\common\RimWorld";
    private static readonly string HarmonyDirectory = Environment.GetEnvironmentVariable("HARMONY_DIRECTORY") ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green QA_DIRECTORY");
            Console.WriteLine("Only bind writes QA copies; no game process, save or installed assembly is changed.");
            return 0;
        }
        if (args.Length != 2 || args[0] is not ("bind" or "red" or "green"))
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }
        string bound = Path.Combine(Path.GetFullPath(args[1]), "bound");
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { bound, GameRoot, HarmonyDirectory })
            {
                string candidate = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
            }
            return null;
        };
        try
        {
            if (args[0] == "bind") Bind(bound);
            else Assembly.GetExecutingAssembly().GetType("Driver", true)!.GetMethod("Run")!.Invoke(null, new object[] { args[0] });
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
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
        Console.WriteLine("PASS: QA-only configuration/art initialization isolation; actual carry callback and Job pooling unchanged.");
    }
}

public static class Driver
{
    private static readonly IntVec3 Destination = new(6, 0, 7);
    private static readonly List<string> Messages = new();
    private static readonly Exception Sentinel = new InvalidOperationException("validation-carry-error");
    private static Pawn pawn = null!;
    private static Thing item = null!;
    private static Job current = null!;
    private static Thing carried = null!;
    private static int records, endings, releases, take, replacementSequence = 100;
    private static bool swapInside, changeId, changeDriver, replaceReference, alreadyEnded, throwCarry;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(string mode)
    {
        var qa = new Harmony("validation.startcarry.boundaries");
        Hook(qa, typeof(Log), nameof(Log.Message), nameof(Message), new[] { typeof(string) });
        Hook(qa, typeof(Log), nameof(Log.Error), nameof(Message), new[] { typeof(string) });
        Hook(qa, typeof(DefOfHelper), nameof(DefOfHelper.EnsureInitializedInCtor), nameof(Skip));
        Hook(qa, typeof(Toils_Haul), nameof(Toils_Haul.ErrorCheckForCarry), nameof(NoCarryError));
        Hook(qa, typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.AvailableStackSpace), nameof(Space));
        Hook(qa, typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), nameof(Carry),
            new[] { typeof(Thing), typeof(int), typeof(bool) });
        Hook(qa, typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob), nameof(ReplaceJob));
        Hook(qa, typeof(Pawn_RecordsTracker), nameof(Pawn_RecordsTracker.Increment), nameof(Record));
        Hook(qa, typeof(ReservationManager), nameof(ReservationManager.ReservedBy), nameof(Reserved),
            new[] { typeof(LocalTargetInfo), typeof(Pawn), typeof(Job) });
        Hook(qa, typeof(ReservationManager), nameof(ReservationManager.Release), nameof(Release),
            new[] { typeof(LocalTargetInfo), typeof(Pawn), typeof(Job) });
        Setup();

        if (mode == "green")
        {
            new StartCarryReentrancyFixMod(null!);
            MethodBase[] patched = Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(CarryReentrancyFix.HarmonyId) == true).ToArray();
            Check(patched.Length == 1 && patched[0].Name.StartsWith("<StartCarryThing>b__", StringComparison.Ordinal), "Patch did not register only on the actual callback.");
            Check(Harmony.GetPatchInfo(patched[0])!.Transpilers.Count(patch => patch.owner == CarryReentrancyFix.HarmonyId) == 1,
                "Expected one callback transpiler.");
            Check(Messages.Any(message => message.Contains("Patched StartCarryThing", StringComparison.Ordinal)), "Mod registration did not report success: " + string.Join("\n", Messages));
        }

        Toil callback = Prepare();
        callback.initAction();
        if (mode == "red")
        {
            Check(endings == 1 && current.def.defName == "HaulToCell" && !current.targetB.IsValid && records == 1,
                "Original callback did not corrupt the reused job.");
            Console.WriteLine("RED: actual StartCarryThing(B) continues after zero carry and real pooled Job reuse, clearing the new HaulToCell destination.");
            return;
        }
        Check(endings == 1 && current.targetB.Cell == Destination && current.count == 21 && records == 0 &&
              current.targetQueueB?.Count == 1 && current.countQueue?.Single() == 9, "Zero-carry path corrupted the replacement job.");
        Console.WriteLine("GREEN: actual callback ends zero-carry task once and preserves the same-object replacement's B/count/queues/records.");

        foreach (int result in new[] { 0, 3 })
        {
            callback = Prepare();
            swapInside = true;
            take = result;
            callback.initAction();
            Check(endings == 0 && current.targetB.Cell == Destination && current.count == 21 && records == 0 &&
                  current.countQueue?.Single() == 9, "Carry-time job replacement was altered or ended again.");
        }
        callback = Prepare();
        take = 3;
        changeId = true;
        callback.initAction();
        Check(endings == 0 && records == 0 && current.targetB.Cell == Destination, "Same object/new generation was not detected.");
        callback = Prepare();
        take = 3;
        changeDriver = true;
        callback.initAction();
        Check(endings == 0 && records == 0 && current.targetB.Thing == item, "Changed driver was not detected.");
        callback = Prepare();
        take = 3;
        replaceReference = true;
        callback.initAction();
        Check(endings == 0 && records == 0 && current.targetB.Cell == Destination, "Changed job reference was not detected.");
        callback = Prepare();
        take = 3;
        alreadyEnded = true;
        callback.initAction();
        Check(endings == 0 && records == 0 && current.targetB.Thing == item, "Ended driver continued mutation.");
        Console.WriteLine("GREEN: zero/nonzero carry reentry, changed loadID, changed driver/reference and ended-driver paths abort without touching a new job.");

        int comparisons = 0;
        foreach (TargetIndex index in new[] { TargetIndex.A, TargetIndex.B })
        foreach (bool subtract in new[] { false, true })
        foreach (bool queue in new[] { false, true })
        foreach (int result in new[] { 3, 10 })
        {
            callback = Prepare(index, queue, subtract, 10);
            take = result;
            callback.initAction();
            Check(endings == 0 && records == 1 && current.GetTarget(index).Thing == carried,
                "Normal successful carry target or record changed.");
            Check(current.count == (subtract ? 10 - result : 10), "Successful carry job count changed.");
            int expectedRemainder = queue && result < 10 ? 10 - result : 0;
            Check((current.countQueue?.FirstOrDefault() ?? 0) == expectedRemainder &&
                  (current.GetTargetQueue(index).Count > 0) == (expectedRemainder > 0), "Partial carry remainder changed.");
            Check(releases == (!queue && result < 10 ? 1 : 0), "Partial carry reservation release changed.");
            comparisons++;
        }
        Console.WriteLine($"GREEN: {comparisons} normal full/partial carry combinations preserve A/B targets, queue remainder, subtraction, reservations and record semantics.");

        callback = Prepare();
        throwCarry = true;
        try { callback.initAction(); throw new Exception("Original carry exception disappeared."); }
        catch (InvalidOperationException exception) when (ReferenceEquals(exception, Sentinel)) { }
        Check(endings == 0 && records == 0, "Carry exception caused new side effects.");
        CheckDrift(callback.initAction.Method);
        Console.WriteLine("GREEN: actual carry exception identity preserved; missing/duplicate call and already-fixed failure-branch drift rejected.");
        Console.WriteLine("NOT TESTED: live cooking/map movement or why the user's initial TryStartCarry returned zero; no RimIOT blame is inferred.");
    }

    private static void Setup()
    {
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        Current.Game = game;
        AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map>());
        AccessTools.Field(typeof(Game), "uniqueIDsManager").SetValue(game, new UniqueIDsManager());
        AccessTools.Field(typeof(Game), "tickManager").SetValue(game, new TickManager());
        AccessTools.Field(typeof(Game), "components").SetValue(game, new List<GameComponent>());
        var map = new Map();
        game.Maps.Add(map);
        map.reservationManager = new ReservationManager(map);
        pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
        pawn.jobs = new Pawn_JobTracker(pawn);
        pawn.carryTracker = new Pawn_CarryTracker(pawn);
        pawn.records = (Pawn_RecordsTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_RecordsTracker));
    }

    private static Toil Prepare(TargetIndex index = TargetIndex.B, bool queue = true, bool subtract = false, int count = 1)
    {
        records = endings = releases = take = 0;
        swapInside = changeId = changeDriver = replaceReference = alreadyEnded = throwCarry = false;
        pawn.carryTracker.innerContainer.Clear();
        item = new Thing { def = new ThingDef { defName = "ValidationMeat", thingClass = typeof(Thing), stackLimit = 75 }, stackCount = 10 };
        current = new Job { def = new JobDef { defName = "DoBill" }, loadID = ++replacementSequence, count = count };
        current.SetTarget(index, item);
        pawn.jobs.curJob = current;
        pawn.jobs.curDriver = new JobDriver_DoBill { job = current, pawn = pawn };
        Toil callback = Toils_Haul.StartCarryThing(index, putRemainderInQueue: queue,
            subtractNumTakenFromJobCount: subtract, failIfStackCountLessThanJobCount: true, reserve: false, canTakeFromInventory: true);
        callback.actor = pawn;
        return callback;
    }

    public static bool ReplaceJob(Pawn_JobTracker __instance)
    {
        endings++;
        ReplaceCurrent(__instance);
        return false;
    }

    private static void ReplaceCurrent(Pawn_JobTracker tracker)
    {
        Job previous = tracker.curJob;
        tracker.curDriver.ended = true;
        tracker.curJob = null;
        tracker.curDriver = null;
        JobMaker.ReturnToPool(previous);
        Job replacement = JobMaker.MakeJob(new JobDef { defName = "HaulToCell" }, new Thing(), Destination);
        Check(ReferenceEquals(previous, replacement), "Fixture did not reuse the real pooled Job instance.");
        replacement.count = 21;
        replacement.targetQueueB = new List<LocalTargetInfo> { item };
        replacement.countQueue = new List<int> { 9 };
        current = replacement;
        tracker.curJob = replacement;
        tracker.curDriver = new JobDriver_HaulToCell { job = replacement, pawn = pawn };
    }

    public static bool Carry(ref int __result)
    {
        if (throwCarry) throw Sentinel;
        if (swapInside) ReplaceCurrent(pawn.jobs);
        if (changeId) { current.loadID++; current.targetB = Destination; }
        if (changeDriver) pawn.jobs.curDriver = new JobDriver_DoBill { job = current, pawn = pawn };
        if (replaceReference)
        {
            current = new Job { def = new JobDef { defName = "HaulToCell" }, loadID = ++replacementSequence, targetB = Destination };
            pawn.jobs.curJob = current;
        }
        if (alreadyEnded) pawn.jobs.curDriver.ended = true;
        if (take > 0)
        {
            carried = new Thing { def = item.def, stackCount = take };
            pawn.carryTracker.innerContainer.TryAdd(carried, canMergeWithExistingStacks: false);
        }
        __result = take;
        return false;
    }

    private static void CheckDrift(MethodInfo callback)
    {
        MethodInfo validation = typeof(CarryReentrancyFix).GetMethod("Validate", BindingFlags.Static | BindingFlags.NonPublic)!;
        MethodInfo carry = AccessTools.Method(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry), new[] { typeof(Thing), typeof(int), typeof(bool) });
        MethodInfo end = AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob));
        foreach (int variant in new[] { 0, 1, 2 })
        {
            List<CodeInstruction> codes = PatchProcessor.GetOriginalInstructions(callback);
            int call = codes.FindIndex(code => code.Calls(carry));
            int ending = codes.FindIndex(code => code.Calls(end));
            if (variant == 0) codes.RemoveAt(call);
            else if (variant == 1) codes.Insert(call, new CodeInstruction(System.Reflection.Emit.OpCodes.Callvirt, carry));
            else codes.Insert(ending + 1, new CodeInstruction(System.Reflection.Emit.OpCodes.Ret));
            try { validation.Invoke(null, new object[] { codes, callback }); throw new Exception("Unsafe IL drift was accepted."); }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { }
        }
    }

    public static bool Skip() => false;
    public static bool NoCarryError(ref bool __result) { __result = false; return false; }
    public static bool Space(ref int __result) { __result = 75; return false; }
    public static bool Record() { records++; return false; }
    public static bool Reserved(ref bool __result) { __result = true; return false; }
    public static bool Release() { releases++; return false; }
    public static bool Message(string text) { Messages.Add(text); return false; }
    private static void Hook(Harmony harmony, Type type, string name, string callback, Type[]? args = null) =>
        harmony.Patch(AccessTools.Method(type, name, args), prefix: new HarmonyMethod(typeof(Driver), callback));
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
