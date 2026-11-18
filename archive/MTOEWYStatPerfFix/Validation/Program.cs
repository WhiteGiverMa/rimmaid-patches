using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using WhiteGiver.MTOEWYStatPerfFix;
using MiliraTheOneEntwinedWithYou.Data;
using RimWorld;
using Verse;

namespace MTOEWYStatPerfFix.Validation;

internal static class Program
{
    private const string UpstreamPatchType = "MiliraTheOneEntwinedWithYou.Patches.IdealExclusiveStatPatch";
    private const string UpstreamHarmonyId = "validation.upstream";
    private const int Iterations = Fixture.Iterations;

    private static readonly List<string> Lines = new();
    private static int failures;
    private static int checks;

    private static readonly MethodInfo GetStatValue = AccessTools.Method(
        typeof(StatExtension),
        nameof(StatExtension.GetStatValue),
        new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) })!;

    private static int Main()
    {
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Temp",
            "opencode",
            "mtoewy-stat-perf-20261113");
        string saveData = Path.Combine(root, "savedata");
        Directory.CreateDirectory(Path.Combine(saveData, "Config"));
        File.WriteAllText(
            Path.Combine(saveData, "Config", "ModsConfig.xml"),
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><ModsConfigData><version>1.7.9810.32479</version>" +
            "<activeMods><li>ludeon.rimworld</li></activeMods><knownExpansions/></ModsConfigData>");

        try
        {
            Run(root);
        }
        catch (Exception exception)
        {
            Fail("unhandled exception: " + exception);
            Console.Error.WriteLine(exception);
        }

        Lines.Add("");
        Lines.Add(failures == 0
            ? $"PASS: {checks} checks, 0 failures."
            : $"FAIL: {checks} checks, {failures} failures.");

        string logPath = Path.Combine(root, "validation.log");
        File.WriteAllText(logPath, string.Join(Environment.NewLine, Lines) + Environment.NewLine);
        foreach (string line in Lines)
        {
            Console.WriteLine(line);
        }

        WriteReceipt(root, logPath);
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string root)
    {
        Section("0. fixture");
        Fixture fixture = Fixture.Create(Path.Combine(root, "savedata"));
        Info($"pawn defs: ideal={fixture.Ideal.GetUniqueLoadID()} plain={fixture.Plain.GetUniqueLoadID()} " +
             $"animal={fixture.Animal.GetUniqueLoadID()} mech={fixture.Mech.GetUniqueLoadID()} enemy={fixture.Enemy.GetUniqueLoadID()}");
        Info($"StatDef count = {DefDatabase<StatDef>.DefCount}; tracker={fixture.Tracker.GetType().FullName}");
        Check(ReferenceEquals(fixture.Game.GetComponent<MiliraTheOneEntwinedWithYou.GameComponents.GameComponent_IdealTracker>(), fixture.Tracker),
            "Game.GetComponent resolves the fixture tracker");
        Check(fixture.Ideal.GetStatValue(fixture.Stats[StatDispatch.MoveSpeed]) > 0f, "real GetStatValue returns a positive value");

        List<Scenario> scenarios = Scenarios.CreateAll(fixture);

        Section("1. vanilla allocation baseline (no patches)");
        foreach (MeasurementCase warm in MeasurementCases(fixture))
        {
            warm.Scenario.Prepare(fixture);
            for (int i = 0; i < 5_000; i++)
            {
                _ = warm.Scenario.Pawn.GetStatValue(warm.Stat);
            }
        }

        foreach (MeasurementCase item in MeasurementCases(fixture))
        {
            item.Scenario.Prepare(fixture);
            Measurement first = MeasureVanilla(item, 10_000);
            Measurement vanilla = MeasureVanilla(item, 10_000);
            Info($"vanilla {item.Name,-18} bytes={vanilla.Bytes,10} calls={vanilla.GetComponentCalls,7} time={vanilla.Milliseconds,9:F2} ms (first pass {first.Bytes} bytes)");
            Check(vanilla.Bytes == 0, $"vanilla GetStatValue is allocation-free in steady state for {item.Name} (got {vanilla.Bytes} bytes)");
        }

        Section("2. attach the real upstream MTOEWY postfix");
        MethodInfo upstreamPostfix = ResolveUpstreamPostfix();
        Harmony upstreamHarmony = new(UpstreamHarmonyId);
        upstreamHarmony.Patch(GetStatValue, postfix: new HarmonyMethod(upstreamPostfix));
        Check(HasPostfixOwner(UpstreamHarmonyId), "upstream postfix is attached to StatExtension.GetStatValue");
        Info($"upstream postfix: {upstreamPostfix.DeclaringType?.FullName}::{upstreamPostfix.Name}");

        Section("3. RED: upstream behaviour (allocations, scans, timing)");
        Dictionary<string, Measurement> red = new(StringComparer.Ordinal);
        foreach (MeasurementCase item in MeasurementCases(fixture))
        {
            item.Scenario.Prepare(fixture);
            Measurement measurement = MeasureRed(item, Iterations);
            red[item.Name] = measurement;
            Info($"RED     {item.Name,-18} bytes={measurement.Bytes,12} calls={measurement.GetComponentCalls,9} time={measurement.Milliseconds,9:F2} ms");
        }

        foreach (string name in new[] { "move_plain", "move_animal", "move_ideal_kit", "move_ideal_lance", "backstory_ideal" })
        {
            Check(red[name].Bytes > 0, $"upstream allocates on {name} ({red[name].Bytes} bytes / {Iterations} calls)");
        }

        Section("4. phase A matrix through the patched real GetStatValue");
        Dictionary<string, float> baseline = CaptureMatrix(fixture, scenarios);
        Info($"captured {baseline.Count} stat values across {scenarios.Count} scenarios");
        CheckNear(baseline["ideal_backstory|GlobalLearningFactor"], 1.1f, "child backstory multiplies GlobalLearningFactor by 1.1");
        CheckNear(baseline["ideal_backstory|CarryingCapacity"], 11f, "adult backstory adds +10 CarryingCapacity");
        CheckNear(baseline["ideal_no_backstory|GlobalLearningFactor"], 1f, "inactive backstory leaves GlobalLearningFactor untouched");
        CheckNear(baseline["world_self|MoveSpeed"], 1.2f, "self stasis effect multiplies MoveSpeed by 1.2");
        CheckNear(baseline["world_enemy|MoveSpeed"], 0.3f, "enemy stasis effect multiplies MoveSpeed by 0.3");
        CheckNear(baseline["world_far|MoveSpeed"], 1f, "out-of-range stasis effect leaves MoveSpeed untouched");
        CheckNear(baseline["plain_gear|MoveSpeed"], 0.7f, "non-Aidier railgun reduces MoveSpeed by 0.3");
        CheckNear(baseline["ideal_kit|MoveSpeed"], 1.7f, "Aidier traits add +0.3 and +0.4 MoveSpeed");
        CheckNear(baseline["ideal_kit|ConstructionSpeed"], 1.35f, "Repair + StarlightTinkerer multiplies ConstructionSpeed by 1.35");
        CheckNear(baseline["ideal_kit|JoyFallRateFactor"], 1.15f, "InquisitiveMind + NeedsStudy multiplies JoyFallRateFactor by 1.15");
        CheckNear(baseline["ideal_kit|AimingDelayFactor"], 0.8f, "NightwingSuit multiplies AimingDelayFactor by 0.8");
        CheckNear(baseline["saving_ideal_railgun|MoveSpeed"], 0.7f, "Saving mode keeps the railgun penalty and drops the Aidier bonuses");

        Section("5. install MTOEWYStatPerfFix");
        Bootstrap.Install();
        Check(Bootstrap.Installed, "Bootstrap.Install reports success");
        Check(HasPostfixOwner(Bootstrap.HarmonyId), "replacement postfix is attached");
        Check(!HasPostfixOwner(UpstreamHarmonyId), "upstream postfix was removed");
        Check(PostfixCount() == 1, $"exactly one postfix remains on GetStatValue (got {PostfixCount()})");

        Section("6. GREEN: equal results and zero allocations");
        Dictionary<string, float> after = CaptureMatrix(fixture, scenarios);
        CompareMatrix(baseline, after);

        foreach (MeasurementCase item in MeasurementCases(fixture))
        {
            item.Scenario.Prepare(fixture);
            Measurement measurement = MeasureGreen(item, Iterations);
            Info($"GREEN   {item.Name,-18} bytes={measurement.Bytes,12} calls={measurement.GetComponentCalls,9} time={measurement.Milliseconds,9:F2} ms " +
                 $"(upstream {red[item.Name].Milliseconds:F2} ms)");
            Check(measurement.Bytes == 0, $"zero bytes allocated on {item.Name} (got {measurement.Bytes})");
            Info($"        timing delta {item.Name}: {measurement.Milliseconds - red[item.Name].Milliseconds:+0.00;-0.00} ms (whole StatWorker call, informational)");
        }

        Check(red["move_plain"].GetComponentCalls >= Iterations * 2,
            $"upstream performs at least two Game.components scans per MoveSpeed call ({red["move_plain"].GetComponentCalls} for {Iterations} calls)");
        Check(red["move_ideal_kit"].GetComponentCalls >= Iterations * 4,
            $"upstream performs at least four Game.components scans per ideal MoveSpeed call ({red["move_ideal_kit"].GetComponentCalls} for {Iterations} calls)");
        Info("note: the GetComponent counter under-reports when the JIT inlines the tiny generic loop; scanner counts are a lower bound.");

        Section("6b. isolated postfix benchmark (no StatWorker noise)");
        MethodInfo ourPostfix = typeof(FastStatPostfix).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        PostfixCall upstreamCall = (PostfixCall)upstreamPostfix.CreateDelegate(typeof(PostfixCall));
        PostfixCall ourCall = (PostfixCall)ourPostfix.CreateDelegate(typeof(PostfixCall));
        foreach ((string label, Scenario scenario, StatDef stat) in new (string, Scenario, StatDef)[]
        {
            ("move_plain", Scenarios.CreateAll(fixture).Single(s => s.Name == "plain_gear"), fixture.Stats[StatDispatch.MoveSpeed]),
            ("move_ideal_lance", Scenarios.CreateAll(fixture).Single(s => s.Name == "ideal_lance"), fixture.Stats[StatDispatch.MoveSpeed]),
            ("backstory_ideal", Scenarios.CreateAll(fixture).Single(s => s.Name == "ideal_backstory"), fixture.Stats[StatDispatch.GlobalLearningFactor]),
            ("unrelated_plain", Scenarios.CreateAll(fixture).Single(s => s.Name == "plain_gear"), fixture.Stats["ShootingAccuracyPawn"]),
        })
        {
            scenario.Prepare(fixture);
            Micro upstreamMicro = MeasurePostfix(upstreamCall, scenario.Pawn, stat, 1_000_000);
            Micro ourMicro = MeasurePostfix(ourCall, scenario.Pawn, stat, 1_000_000);
            Info($"postfix {label,-18} upstream bytes={upstreamMicro.Bytes,10} ns/call={upstreamMicro.NanosecondsPerCall,7:F1} | " +
                 $"ours bytes={ourMicro.Bytes,6} ns/call={ourMicro.NanosecondsPerCall,6:F1}");
            if (label != "unrelated_plain")
            {
                Check(upstreamMicro.Bytes > 0, $"upstream postfix allocates for {label}");
            }
            Check(ourMicro.Bytes == 0, $"replacement postfix allocates zero bytes for {label} (got {ourMicro.Bytes})");
            Check(ourMicro.NanosecondsPerCall < upstreamMicro.NanosecondsPerCall,
                $"replacement postfix is faster than upstream for {label} ({ourMicro.NanosecondsPerCall:F1} vs {upstreamMicro.NanosecondsPerCall:F1} ns)");
        }

        Section("6c. dev-mode DefDatabase reload");
        fixture.ReloadStats();
        Check(StatDispatch.TryGet(fixture.Stats[StatDispatch.MoveSpeed], out StatId reloadedId) && reloadedId == StatId.MoveSpeed,
            "dispatch table is rebuilt when the DefDatabase instances change");
        Dictionary<string, float> reloaded = CaptureMatrix(fixture, scenarios);
        CompareMatrix(baseline, reloaded);
        CheckNear(reloaded["ideal_backstory|GlobalLearningFactor"], 1.1f, "backstory bonus survives a def reload");

        Section("7. idempotent install");
        Bootstrap.Install();
        Check(PostfixCount() == 1, "second Install does not duplicate the postfix");

        Section("8. safety net: late upstream patch is removed again");
        upstreamHarmony.Patch(GetStatValue, postfix: new HarmonyMethod(upstreamPostfix));
        Check(HasPostfixOwner(UpstreamHarmonyId), "late upstream postfix attached for the repair test");
        Bootstrap.RepairIfNeeded();
        Check(Bootstrap.Installed, "RepairIfNeeded keeps the optimization installed");
        Check(!HasPostfixOwner(UpstreamHarmonyId), "RepairIfNeeded removes the late upstream postfix");
        Dictionary<string, float> repaired = CaptureMatrix(fixture, scenarios);
        CompareMatrix(baseline, repaired);

        Section("8b. game-change cache release");
        Current.Game = null;
        Check(TrackerCache.Current() == null, "tracker cache is released when Current.Game becomes null");
        Current.Game = fixture.Game;
        Check(ReferenceEquals(TrackerCache.Current(), fixture.Tracker), "tracker cache re-resolves after Current.Game is restored");

        Section("9. engine diagnostics");
        Info($"captured {TestHooks.Errors.Count} Log.Error and {TestHooks.Warnings.Count} Log.Warning messages during validation");
        foreach (string error in TestHooks.Errors.Distinct().Take(12))
        {
            Info("  Log.Error: " + error);
        }

        foreach (string warning in TestHooks.Warnings.Distinct().Take(12))
        {
            Info("  Log.Warning: " + warning);
        }

        Info("save data root: " + root);
    }

    private readonly record struct MeasurementCase(string Name, Scenario Scenario, StatDef Stat);

    private static IEnumerable<MeasurementCase> MeasurementCases(Fixture f)
    {
        List<Scenario> all = Scenarios.CreateAll(f);
        Scenario Find(string name) => all.Single(s => s.Name == name);

        yield return new MeasurementCase("move_plain", Find("plain_gear"), f.Stats[StatDispatch.MoveSpeed]);
        yield return new MeasurementCase("move_animal", Find("animal"), f.Stats[StatDispatch.MoveSpeed]);
        yield return new MeasurementCase("move_ideal_kit", Find("ideal_kit"), f.Stats[StatDispatch.MoveSpeed]);
        yield return new MeasurementCase("move_ideal_lance", Find("ideal_lance"), f.Stats[StatDispatch.MoveSpeed]);
        yield return new MeasurementCase("backstory_ideal", Find("ideal_backstory"), f.Stats[StatDispatch.GlobalLearningFactor]);
        yield return new MeasurementCase("unrelated_plain", Find("plain_gear"), f.Stats["ShootingAccuracyPawn"]);
    }

    private static Dictionary<string, float> CaptureMatrix(Fixture fixture, List<Scenario> scenarios)
    {
        Dictionary<string, float> values = new(StringComparer.Ordinal);
        foreach (Scenario scenario in scenarios)
        {
            scenario.Prepare(fixture);
            foreach (StatDef stat in fixture.AllStats)
            {
                values[Key(scenario, stat)] = scenario.Pawn.GetStatValue(stat);
            }
        }

        Scribe.mode = LoadSaveMode.Inactive;
        return values;
    }

    private static void CompareMatrix(Dictionary<string, float> expected, Dictionary<string, float> actual)
    {
        int mismatches = 0;
        foreach (KeyValuePair<string, float> pair in expected)
        {
            if (!SameFloat(pair.Value, actual[pair.Key]))
            {
                mismatches++;
                if (mismatches <= 10)
                {
                    Info($"  mismatch {pair.Key}: upstream={Bits(pair.Value)} candidate={Bits(actual[pair.Key])}");
                }
            }
        }

        Check(mismatches == 0, $"all {expected.Count} stat values are bit-identical to upstream (mismatches={mismatches})");
    }

    private static string Key(Scenario scenario, StatDef stat) => scenario.Name + "|" + stat.defName;

    private static void CheckNear(float actual, float expected, string description)
    {
        Check(Math.Abs(actual - expected) <= 1e-5f, $"{description} (got {actual:R})");
    }

    private static bool SameFloat(float left, float right)
    {
        if (float.IsNaN(left) && float.IsNaN(right))
        {
            return true;
        }

        return BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);
    }

    private static string Bits(float value) => value.ToString("R", CultureInfo.InvariantCulture) + " (0x" + BitConverter.SingleToInt32Bits(value).ToString("X8") + ")";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float VanillaCall(Pawn pawn, StatDef stat) => pawn.GetStatValue(stat);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float RedCall(Pawn pawn, StatDef stat) => pawn.GetStatValue(stat);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float GreenCall(Pawn pawn, StatDef stat) => pawn.GetStatValue(stat);

    private static Measurement Measure(Func<Pawn, StatDef, float> call, Pawn pawn, StatDef stat, int iterations, int warmup)
    {
        float sink = 0f;
        for (int i = 0; i < warmup; i++)
        {
            sink += call(pawn, stat);
        }

        TestHooks.GetComponentCalls = 0;
        Stopwatch watch = new();
        long before = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int i = 0; i < iterations; i++)
        {
            sink += call(pawn, stat);
        }

        watch.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink);
        return new Measurement(bytes, TestHooks.GetComponentCalls, watch.Elapsed.TotalMilliseconds);
    }

    private delegate void PostfixCall(Thing thing, StatDef stat, ref float value);

    private readonly record struct Micro(long Bytes, double NanosecondsPerCall);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CallPostfix(PostfixCall call, Thing thing, StatDef stat, ref float value) => call(thing, stat, ref value);

    private static Micro MeasurePostfix(PostfixCall call, Thing thing, StatDef stat, int iterations)
    {
        float value = 0f;
        for (int i = 0; i < 20_000; i++)
        {
            value = 4.6f;
            CallPostfix(call, thing, stat, ref value);
        }

        Stopwatch watch = new();
        long before = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int i = 0; i < iterations; i++)
        {
            value = 4.6f;
            CallPostfix(call, thing, stat, ref value);
        }

        watch.Stop();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(value);
        return new Micro(bytes, watch.Elapsed.TotalMilliseconds * 1_000_000.0 / iterations);
    }

    private static Measurement MeasureVanilla(MeasurementCase item, int iterations) =>
        Measure(VanillaCall, item.Scenario.Pawn, item.Stat, iterations, 1_000);

    private static Measurement MeasureRed(MeasurementCase item, int iterations) =>
        Measure(RedCall, item.Scenario.Pawn, item.Stat, iterations, 2_000);

    private static Measurement MeasureGreen(MeasurementCase item, int iterations) =>
        Measure(GreenCall, item.Scenario.Pawn, item.Stat, iterations, 2_000);

    private readonly record struct Measurement(long Bytes, int GetComponentCalls, double Milliseconds);

    private static MethodInfo ResolveUpstreamPostfix()
    {
        Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "MiliraTheOneEntwinedWithYou")
            ?? Assembly.Load("MiliraTheOneEntwinedWithYou");
        Type type = assembly.GetType(UpstreamPatchType, throwOnError: false)
            ?? throw new InvalidOperationException("upstream patch type not found: " + UpstreamPatchType);
        return type.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException("upstream Postfix not found");
    }

    private static bool HasPostfixOwner(string owner)
    {
        Patches? patches = Harmony.GetPatchInfo(GetStatValue);
        return patches != null && patches.Postfixes.Any(patch => patch.owner == owner);
    }

    private static int PostfixCount() => Harmony.GetPatchInfo(GetStatValue)?.Postfixes.Count ?? 0;

    private static void Section(string title)
    {
        Lines.Add("");
        Lines.Add("== " + title);
    }

    private static void Info(string text) => Lines.Add(text);

    private static void Check(bool condition, string description)
    {
        checks++;
        if (condition)
        {
            Lines.Add("  [ok]   " + description);
        }
        else
        {
            failures++;
            Lines.Add("  [FAIL] " + description);
        }
    }

    private static void Fail(string text)
    {
        failures++;
        Lines.Add("  [FAIL] " + text);
    }

    private static void WriteReceipt(string root, string logPath)
    {
        StringBuilder builder = new();
        builder.AppendLine("{");
        builder.AppendLine("  \"mod\": \"MTOEWYStatPerfFix\",");
        builder.AppendLine("  \"packageId\": \"whitegiver.mtoewy.statperffix\",");
        builder.AppendLine("  \"result\": \"" + (failures == 0 ? "PASS" : "FAIL") + "\",");
        builder.AppendLine("  \"checks\": " + checks + ",");
        builder.AppendLine("  \"failures\": " + failures + ",");
        builder.AppendLine("  \"iterationsPerCase\": " + Iterations + ",");
        builder.AppendLine("  \"log\": " + Quote(logPath) + ",");
        builder.AppendLine("  \"validatedAtUtc\": " + Quote(DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
        builder.AppendLine("}");
        File.WriteAllText(Path.Combine(root, "receipt.json"), builder.ToString());
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\") + "\"";
}
