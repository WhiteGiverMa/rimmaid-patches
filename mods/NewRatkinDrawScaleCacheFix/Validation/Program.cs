using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;
using Verse;
using WhiteGiver.NewRatkinDrawScaleCacheFix;

internal static class Entry
{
    private static readonly string GameRoot =
        Environment.GetEnvironmentVariable("RIMWORLD_ROOT") ?? @"A:\SteamLibrary\steamapps\common\RimWorld";
    private static readonly string HarmonyDirectory =
        Environment.GetEnvironmentVariable("HARMONY_DIRECTORY") ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";
    private static readonly string NewRatkinPath =
        Environment.GetEnvironmentVariable("NEWRATKIN_DLL") ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\1578693166\1.7\Assemblies\NewRatkin.dll";

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green QA_DIRECTORY");
            Console.WriteLine("Only bind writes QA copies; no game process, save, Workshop file or installed assembly is changed.");
            return 0;
        }
        if (args.Length != 2 || args[0] is not ("bind" or "red" or "green"))
        {
            Console.Error.WriteLine("Invalid arguments; use --help.");
            return 1;
        }

        string qa = Path.GetFullPath(args[1]);
        string bound = Path.Combine(qa, "bound");
        string ratkinDirectory = Path.GetDirectoryName(NewRatkinPath)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { bound, GameRoot, HarmonyDirectory, ratkinDirectory })
            {
                string candidate = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
            }
            return null;
        };

        try
        {
            if (args[0] == "bind") Bind(bound, qa, ratkinDirectory);
            else Driver.Run(args[0], bound);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind(string bound, string qa, string ratkinDirectory)
    {
        if (bound.StartsWith(GameRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || bound.StartsWith(ratkinDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("QA output must be outside the installed game and Workshop mod.");
        Directory.CreateDirectory(qa);
        Directory.CreateDirectory(bound);
        RewriteUnityReferences(Path.Combine(GameRoot, "Assembly-CSharp.dll"), Path.Combine(bound, "Assembly-CSharp.dll"));
        RewriteUnityReferences(NewRatkinPath, Path.Combine(bound, "NewRatkin.dll"));
        Console.WriteLine("PASS: QA-only Unity reference binding; real game and NewRatkin method bodies remain unchanged.");
    }

    private static void RewriteUnityReferences(string source, string destination)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(GameRoot);
        resolver.AddSearchDirectory(HarmonyDirectory);
        resolver.AddSearchDirectory(Path.GetDirectoryName(NewRatkinPath)!);
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(
            source, new ReaderParameters { AssemblyResolver = resolver });
        foreach (AssemblyNameReference reference in assembly.MainModule.AssemblyReferences)
        {
            if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
            reference.Name = "UnityStubs";
            reference.Version = new Version(1, 0, 0, 0);
        }
        assembly.Write(destination);
    }
}

internal static class Driver
{
    private const BindingFlags StaticAny = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int ThreadCount = 16;
    private const int IterationsPerThread = 10_000;
    private static readonly List<string> Messages = new();
    private static readonly string ValidationHarmonyId = "validation.newratkin.drawscalecachefix";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(string mode, string bound)
    {
        Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bound, "Assembly-CSharp.dll"));
        Assembly ratkin = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bound, "NewRatkin.dll"));

        Type logType = game.GetType("Verse.Log", true)!;
        HookLog(logType, "Message", nameof(LogMessage));
        HookLog(logType, "Warning", nameof(LogWarning));
        HookLog(logType, "Error", nameof(LogError));

        Type pawnType = game.GetType("Verse.Pawn", true)!;
        Type cacheType = ratkin.GetType("NewRatkin.GeneDrawScaleCache", true)!;
        MethodInfo get = cacheType.GetMethod("Get", StaticAny, null, new[] { pawnType }, null)
            ?? throw new MissingMethodException("NewRatkin.GeneDrawScaleCache.Get(Pawn)");
        MethodInfo recalculate = cacheType.GetMethod(
            "Recalculate", StaticAny, null, new[] { pawnType, typeof(float).MakeByRefType() }, null)
            ?? throw new MissingMethodException("NewRatkin.GeneDrawScaleCache.Recalculate(Pawn, out float)");
        FieldInfo cacheField = cacheType.GetField("drawScaleByPawn", StaticAny)
            ?? throw new MissingFieldException("NewRatkin.GeneDrawScaleCache.drawScaleByPawn");

        Type thingDefType = game.GetType("Verse.ThingDef", true)!;
        var sharedDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(thingDefType);
        sharedDef.defName = "ValidationPawn";

        if (mode == "green")
        {
            DrawScaleCacheThreadSafety.Apply();
            CheckPatched(get);
            CheckPatched(recalculate);
            Check(Messages.Any(message => message.Contains("Patched GeneDrawScaleCache.Get", StringComparison.Ordinal)),
                "Registration did not report success: " + string.Join("\n", Messages));
        }
        else
        {
            CheckNotPatched(get);
            CheckNotPatched(recalculate);
        }

        var getDelegate = (Func<Pawn, float>)Delegate.CreateDelegate(typeof(Func<Pawn, float>), get);
        long expected = (long)ThreadCount * IterationsPerThread;
        Exception? firstFailure = null;
        int failures = 0;
        int scaleMismatches = 0;
        Stopwatch watch = Stopwatch.StartNew();
        Task storm = Task.Run(() => Parallel.For(0, ThreadCount, thread =>
        {
            try
            {
                for (int i = 0; i < IterationsPerThread; i++)
                {
                    int id = thread * IterationsPerThread + i + 1;
                    Pawn pawn = NewPawn(pawnType, sharedDef, id);
                    if (getDelegate(pawn) != 1f) Interlocked.Increment(ref scaleMismatches);
                }
            }
            catch (Exception exception)
            {
                Interlocked.Increment(ref failures);
                Interlocked.CompareExchange(ref firstFailure, exception, null);
            }
        }));
        if (!storm.Wait(TimeSpan.FromMinutes(3)))
            throw new TimeoutException("Concurrency storm did not finish; possible deadlock in the patched cache.");
        watch.Stop();

        int count = ReadCount(cacheField);
        Console.WriteLine($"storm={watch.ElapsedMilliseconds}ms threads={ThreadCount} expected={expected} " +
                          $"count={count} failures={failures} scaleMismatches={scaleMismatches}");

        if (mode == "red")
        {
            if (failures == 0 && count == expected)
            {
                throw new InvalidOperationException(
                    "RED baseline did not reproduce the race; the concurrency storm is not strong enough.");
            }
            Console.WriteLine($"RED: unpatched cache failed under concurrency (first failure: " +
                              $"{firstFailure?.GetType().Name ?? "none"}, count {count}/{expected}).");
            return;
        }

        Check(failures == 0, $"GREEN cache threw {failures} time(s): {firstFailure}");
        Check(scaleMismatches == 0, $"GREEN returned an unexpected draw scale {scaleMismatches} time(s).");
        Check(count == expected, $"GREEN cache count {count} != expected {expected}.");
        Check(!Messages.Any(message => message.StartsWith("ERROR:", StringComparison.Ordinal)),
            "Unexpected game error: " + string.Join("\n", Messages));
        Console.WriteLine("GREEN: patched Get/Recalculate stayed consistent under 16-thread cache misses; " +
                          "reentrant Get -> Recalculate completed without deadlock.");
    }

    private static Pawn NewPawn(Type pawnType, ThingDef def, int id)
    {
        var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(pawnType);
        pawn.def = def;
        pawn.thingIDNumber = id;
        return pawn;
    }

    private static void CheckPatched(MethodInfo method)
    {
        Patches? info = Harmony.GetPatchInfo(method);
        Check(info != null && info.Owners.Contains(DrawScaleCacheThreadSafety.HarmonyId),
            $"{method.DeclaringType?.Name}.{method.Name} was not registered by the patch.");
        Check(info!.Prefixes.Count(patch => patch.owner == DrawScaleCacheThreadSafety.HarmonyId) == 1,
            $"{method.Name} does not have exactly one lock prefix.");
        Check(info.Finalizers.Count(patch => patch.owner == DrawScaleCacheThreadSafety.HarmonyId) == 1,
            $"{method.Name} does not have exactly one lock finalizer.");
    }

    private static void CheckNotPatched(MethodInfo method)
    {
        Patches? info = Harmony.GetPatchInfo(method);
        Check(info == null || !info.Owners.Contains(DrawScaleCacheThreadSafety.HarmonyId),
            $"{method.Name} was patched in RED mode.");
    }

    private static int ReadCount(FieldInfo cacheField)
    {
        object? cache = cacheField.GetValue(null);
        if (cache == null) return -1;
        try
        {
            return (int)cache.GetType().GetProperty("Count")!.GetValue(cache)!;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Cache Count unavailable after storm: {exception.GetType().Name}: {exception.Message}");
            return -1;
        }
    }

    private static void HookLog(Type logType, string methodName, string prefixName)
    {
        MethodInfo target = AccessTools.Method(logType, methodName, new[] { typeof(string) })
            ?? throw new MissingMethodException($"Verse.Log.{methodName}(string)");
        MethodInfo prefix = typeof(Driver).GetMethod(prefixName, StaticAny)
            ?? throw new MissingMethodException($"Driver.{prefixName}");
        new Harmony(ValidationHarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private static bool LogMessage(string text)
    {
        Messages.Add("MESSAGE: " + text);
        return false;
    }

    private static bool LogWarning(string text)
    {
        Messages.Add("WARNING: " + text);
        return false;
    }

    private static bool LogError(string text)
    {
        Messages.Add("ERROR: " + text);
        return false;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
