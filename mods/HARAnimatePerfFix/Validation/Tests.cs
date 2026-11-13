using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HARAnimate;
using HARAnimate.Animation;
using HarmonyLib;
using Verse;
using WhiteGiver.HARAnimatePerfFix;

namespace HARAnimatePerfFix.Validation;

internal static class Tests
{
    private const int AllocIterations = 200_000;
    private const int StressThreads = 16;
    private const int StressIterationsPerThread = 50_000;
    private const int StressKeyCount = 4;
    private const int StressSlotCount = 64;
    private const int StressBaseTick = 4000;

    private const int AddonEar = 101;
    private const int AddonRightEar = 202;
    private const int AddonTail = 303;
    private const int AddonWing = 404;

    private static int checks;

    private static readonly MethodInfo CleanupMethod =
        typeof(AnimatorCache).GetMethod("Cleanup", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AnimatorCache.Cleanup not found.");

    private static readonly MethodInfo CleanAllMethod =
        typeof(AnimatorCache).GetMethod("CleanAll", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AnimatorCache.CleanAll not found.");

    private static readonly MethodInfo CacheCountMethod =
        typeof(AnimatorCache).GetMethod("CacheCount", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("AnimatorCache.CacheCount not found.");

    internal static void Check(bool condition, string message)
    {
        checks++;

        if (!condition)
        {
            throw new InvalidOperationException("FAILED: " + message);
        }
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("--- " + title + " ---");
    }

    private static void Cleanup(int currentTick, int maxAge)
    {
        CleanupMethod.Invoke(null, new object[] { currentTick, maxAge });
    }

    private static void CleanAll()
    {
        CleanAllMethod.Invoke(null, null);
    }

    private static int CacheCount()
    {
        return (int)CacheCountMethod.Invoke(null, null)!;
    }

    // ---------------------------------------------------------------- RED

    internal static void RunRed()
    {
        Console.WriteLine("RED: unpatched HAR BodyPart Animate FrameWork (dtrndg.haranimate)");

        RawTransform.AssertLayout();
        Section("RED 1 - no patch from this mod is loaded");
        Check(OwnedPatchedMethods().Count == 0, "a patch owned by whitegiver.haranimate.perffix is already installed");
        Console.WriteLine("       Harmony sees 0 methods patched by " + Program.Owner);

        Section("RED 2 - upstream evaluation and cache semantics");
        Fixture fx = Fixture.Create(8);
        fx.RegisterStacks();

        BodyAddonTransform upstreamEar = AnimatorSystem.Evaluate(fx.Pawns[0], fx.South[0], Fixture.EarLabel, 1000);
        BodyAddonTransform oracleEar = fx.EarStack.Evaluate(fx.Pawns[0], fx.South[0], 1000, fx.Pawns[0].thingIDNumber);
        Check(RawTransform.From(upstreamEar).Equals(RawTransform.From(oracleEar)),
            "upstream AnimatorSystem.Evaluate disagrees with the animator stack");
        Check(!RawTransform.From(upstreamEar).Equals(RawTransform.From(BodyAddonTransform.Identity)),
            "the validation stack produced an identity transform; the scenario is not exercising animation");
        Console.WriteLine($"       tick 1000 ear transform: {RawTransform.From(upstreamEar)}");

        BodyAddonTransform missing = AnimatorSystem.Evaluate(fx.Pawns[0], fx.South[0], "NoSuchLabel", 1000);
        Check(RawTransform.From(missing).Equals(RawTransform.From(BodyAddonTransform.Identity)),
            "upstream unknown label is not Identity");

        Section("RED 3 - allocation per lookup (200,000 calls, tick += 4)");
        long evalBytes = MeasureEvaluate(fx);
        long getBytes = MeasureGet(fx);

        Console.WriteLine($"       AnimatorSystem.Evaluate : {evalBytes / (double)AllocIterations,8:F1} B/call  total {evalBytes,12:N0} B");
        Console.WriteLine($"       AnimatorCache.Get       : {getBytes / (double)AllocIterations,8:F1} B/call  total {getBytes,12:N0} B");

        // The bug report claims >= 6.4 MB over 200,000 lookups; the upstream path is expected to be
        // well above that (a 32-byte boxed CacheKey plus a 64-byte concatenated string, per call).
        Check(getBytes >= 6_400_000,
            $"upstream AnimatorCache.Get allocated only {getBytes:N0} bytes over {AllocIterations:N0} calls; the RED baseline no longer proves the bug");
        Check(evalBytes >= 6_400_000,
            $"upstream AnimatorSystem.Evaluate allocated only {evalBytes:N0} bytes over {AllocIterations:N0} calls");

        Section("RED 4 - cache slot / expiry / clear semantics");
        VerifySlotSemantics(fx, "upstream");
        VerifyColdTickQuirkUpstream(fx);
        VerifyExpiryAndClear("upstream");
        VerifyZeroRefreshTickUpstream(fx);

        Section("RED 5 - thread stress on the upstream cache (reported, not asserted)");
        StressResult red = Stress(fx, assertNothing: true);
        Console.WriteLine($"       16 threads x {StressIterationsPerThread / 5:N0} lookups, concurrent Cleanup:");
        Console.WriteLine($"         exceptions: {red.Exceptions}");
        Console.WriteLine($"         transforms that did not match the value for their slot: {red.Mismatches:N0}");
        Console.WriteLine("       (upstream writes entry.slot before entry.value and reads without a lock, so a");
        Console.WriteLine("        concurrent refresh can be observed half-written; a single run may or may not hit it)");
        Check(red.Exceptions == 0, "upstream ConcurrentDictionary stress threw");

        Console.WriteLine();
        Console.WriteLine($"PASS: {checks} RED assertions.");
    }

    private static long MeasureEvaluate(Fixture fx)
    {
        Pawn[] pawns = fx.Pawns;
        PawnDrawParms[] parms = fx.South;
        int count = pawns.Length;
        float sink = 0f;

        for (int i = 0; i < 1000; i++)
        {
            sink += AnimatorSystem.Evaluate(pawns[i % count], parms[i % count], Fixture.EarLabel, 1000 + i * 4).Angle;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < AllocIterations; i++)
        {
            int index = i % count;
            sink += AnimatorSystem.Evaluate(pawns[index], parms[index], Fixture.EarLabel, 1000 + i * 4).Angle;
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink);
        return bytes;
    }

    private static long MeasureGet(Fixture fx)
    {
        Pawn[] pawns = fx.Pawns;
        PawnDrawParms[] parms = fx.South;
        int count = pawns.Length;
        int[] addons = { AddonEar, AddonRightEar, AddonTail, AddonWing };
        string[] labels = { Fixture.EarLabel, Fixture.RightEarLabel, Fixture.TailLabel, Fixture.WingLabel };
        float sink = 0f;

        for (int i = 0; i < 1000; i++)
        {
            int index = i % count;
            int slot = i % addons.Length;
            sink += AnimatorCache.Get(pawns[index], parms[index], addons[slot], labels[slot], 1000 + i * 4).Angle;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < AllocIterations; i++)
        {
            int index = i % count;
            int slot = i % addons.Length;
            sink += AnimatorCache.Get(pawns[index], parms[index], addons[slot], labels[slot], 1000 + i * 4).Angle;
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink);
        return bytes;
    }

    /// <summary>
    /// Upstream rule: the cached value is refreshed only when <c>tick / CacheRefreshTick</c> changes,
    /// so two ticks inside the same slot must return the identical transform.
    /// </summary>
    private static void VerifySlotSemantics(Fixture fx, string label)
    {
        CleanAll();
        Pawn pawn = fx.Pawns[0];
        PawnDrawParms parms = fx.South[0];

        RawTransform at1000 = RawTransform.From(
            AnimatorCache.Get(pawn, parms, AddonEar, Fixture.EarLabel, 1000));
        RawTransform at1001 = RawTransform.From(
            AnimatorCache.Get(pawn, parms, AddonEar, Fixture.EarLabel, 1001));
        RawTransform at1004 = RawTransform.From(
            AnimatorCache.Get(pawn, parms, AddonEar, Fixture.EarLabel, 1004));

        RawTransform oracle1000 = RawTransform.From(
            fx.EarStack.Evaluate(pawn, parms, 1000, pawn.thingIDNumber));
        RawTransform oracle1001 = RawTransform.From(
            fx.EarStack.Evaluate(pawn, parms, 1001, pawn.thingIDNumber));
        RawTransform oracle1004 = RawTransform.From(
            fx.EarStack.Evaluate(pawn, parms, 1004, pawn.thingIDNumber));

        Check(at1000.Equals(oracle1000), $"{label}: tick 1000 value is not the freshly evaluated value");
        Check(at1001.Equals(at1000), $"{label}: tick 1001 (same slot) returned a different transform");
        Check(!oracle1001.Equals(oracle1000), "animator output does not change per tick; slot check proves nothing");
        Check(at1004.Equals(oracle1004), $"{label}: tick 1004 (next slot) did not refresh");

        Console.WriteLine(
            $"       {label}: slot caching preserved (1000 == 1001, 1004 refreshed, value {at1000})");
    }

    /// <summary>
    /// Upstream quirk that must be preserved bit for bit: a cold CacheEntry starts with slot 0 and a
    /// default transform, so the first 4 ticks of a new game return the zero transform (Scale 0,0)
    /// without ever evaluating the animators. It self-heals at tick 4.
    /// </summary>
    private static void VerifyColdTickQuirkUpstream(Fixture fx)
    {
        CleanAll();
        RawTransform cold = RawTransform.From(AnimatorCache.Get(fx.Pawns[0], fx.South[0], 900, Fixture.EarLabel, 2));
        Check(cold.Equals(default(RawTransform)),
            "upstream no longer returns the default transform for a cold entry at ticks 0..3: " + cold);
        RawTransform warm = RawTransform.From(AnimatorCache.Get(fx.Pawns[0], fx.South[0], 900, Fixture.EarLabel, 4));
        Check(!warm.Equals(default(RawTransform)), "upstream tick 4 did not refresh the cold entry");
        Console.WriteLine("       upstream cold-entry quirk at ticks 0..3 observed (default transform, no evaluation)");
    }

    private static void VerifyZeroRefreshTickUpstream(Fixture fx)
    {
        int original = HARAnimateSettings.CacheRefreshTick;
        HARAnimateSettings.CacheRefreshTick = 0;

        bool threw = false;
        try
        {
            AnimatorCache.Get(fx.Pawns[0], fx.South[0], AddonEar, Fixture.EarLabel, 5000);
        }
        catch (DivideByZeroException)
        {
            threw = true;
        }
        finally
        {
            HARAnimateSettings.CacheRefreshTick = original;
        }

        Check(threw, "upstream AnimatorCache.Get no longer throws when CacheRefreshTick is 0");
        Console.WriteLine("       upstream: CacheRefreshTick = 0 crashes with DivideByZeroException inside the render path");
    }

    private static void VerifyExpiryAndClear(string label)
    {
        CleanAll();
        Check(CacheCount() == 0, $"{label}: CleanAll did not empty the cache");

        Fixture fx = Fixture.Create(3);
        for (int i = 0; i < 3; i++)
        {
            AnimatorCache.Get(fx.Pawns[i], fx.South[i], AddonEar, Fixture.EarLabel, 1000);
        }

        Check(CacheCount() == 3, $"{label}: expected 3 cache entries, found {CacheCount()}");

        // Refresh only the first entry far in the future, then expire with a 15,000 tick window.
        AnimatorCache.Get(fx.Pawns[0], fx.South[0], AddonEar, Fixture.EarLabel, 40000);
        Cleanup(40000, AnimatorCache.CleanupMaxAge);

        int remaining = CacheCount();
        Check(remaining == 1, $"{label}: Cleanup(40000, 15000) left {remaining} entries instead of 1");

        AnimatorCache.Get(fx.Pawns[0], fx.South[0], AddonEar, Fixture.EarLabel, 40000);
        Check(CacheCount() == 1, $"{label}: a still-valid entry was duplicated");

        AnimatorCache.Get(fx.Pawns[1], fx.South[1], AddonEar, Fixture.EarLabel, 40000);
        Check(CacheCount() == 2, $"{label}: a re-added evicted entry was not cached");

        CleanAll();
        Check(CacheCount() == 0, $"{label}: CleanAll did not empty the cache a second time");

        Console.WriteLine($"       {label}: expiry 3 -> 1, re-add, CleanAll -> 0");
    }

    // -------------------------------------------------------------- GREEN

    internal static void RunGreen(Type modType)
    {
        Console.WriteLine("GREEN: HARAnimatePerfFix applied on top of the real HARAnimate.dll");

        RawTransform.AssertLayout();

        Section("GREEN 1 - registration that happened before the patch is mirrored by the startup scan");
        Fixture fx = Fixture.Create(8);

        // Simulates HARAnimate's own [StaticConstructorOnStartup] having registered stacks before this
        // mod ever ran. The upstream dictionary is the only place this entry exists.
        AnimatorSystem.Register($"{Fixture.PreExistingRace}:{Fixture.EarLabel}", fx.EarStack);
        Check(UpstreamStackCount() == 1, "the pre-existing registration did not reach AnimatorSystem.stacks");

        object mod = Activator.CreateInstance(modType, new object?[] { null })!;
        Check(mod is Mod, "the mod type is not a Verse.Mod");

        Check(FastAnimatorSystem.MirroredCount == 1,
            $"startup scan mirrored {FastAnimatorSystem.MirroredCount} registrations instead of 1");
        Check(FastAnimatorSystem.TryGet(Fixture.PreExistingRace, Fixture.EarLabel, out AnimatorStack? preExisting)
              && ReferenceEquals(preExisting, fx.EarStack),
            "the startup scan did not mirror the pre-existing registration");

        BodyAddonTransform preExistingTransform = AnimatorSystem.Evaluate(
            fx.PreExistingPawn,
            fx.PreExistingSouth,
            Fixture.EarLabel,
            1000);
        Check(RawTransform.From(preExistingTransform).Equals(RawTransform.From(
                fx.EarStack.Evaluate(fx.PreExistingPawn, fx.PreExistingSouth, 1000, fx.PreExistingPawn.thingIDNumber))),
            "the pre-existing stack is not evaluated after patching");
        Console.WriteLine("       pre-existing \"PreExistingRace:Ear\" mirrored and evaluated");

        Section("GREEN 2 - Harmony patches land on the intended methods only");
        HashSet<MethodBase> expected = new()
        {
            AnimatorCacheGet(),
            CleanupMethod,
            CleanAllMethod,
            CacheCountMethod,
            typeof(AnimatorCacheCleaner).GetMethod(nameof(AnimatorCacheCleaner.CacheCleaner))!,
            typeof(AnimatorSystem).GetMethod(nameof(AnimatorSystem.Register))!,
            typeof(AnimatorSystem).GetMethod(nameof(AnimatorSystem.Evaluate))!,
        };

        HashSet<MethodBase> owned = OwnedPatchedMethods();
        Check(owned.SetEquals(expected),
            "patched method set mismatch: " + string.Join(", ", owned.Select(m => m.DeclaringType?.Name + "." + m.Name)));
        Console.WriteLine("       7 patches: AnimatorCache.Get/Cleanup/CleanAll/CacheCount, AnimatorCacheCleaner.CacheCleaner,");
        Console.WriteLine("                  AnimatorSystem.Register (prefix+postfix), AnimatorSystem.Evaluate");

        Section("GREEN 3 - live Register is mirrored by the prefix");
        fx.RegisterStacks();
        Check(UpstreamStackCount() == 5, $"AnimatorSystem.stacks has {UpstreamStackCount()} entries instead of 5");
        Check(FastAnimatorSystem.MirroredCount == 5,
            $"mirror has {FastAnimatorSystem.MirroredCount} registrations instead of 5");
        Check(!FastAnimatorSystem.HasAmbiguousKeys, "plain race:label keys were treated as ambiguous");

        foreach ((string race, string label, AnimatorStack stack) in new[]
                 {
                     (Fixture.Race, Fixture.EarLabel, fx.EarStack),
                     (Fixture.Race, Fixture.RightEarLabel, fx.RightEarStack),
                     (Fixture.Race, Fixture.TailLabel, fx.TailStack),
                     (Fixture.OtherRace, Fixture.WingLabel, fx.WingStack),
                     (Fixture.PreExistingRace, Fixture.EarLabel, fx.EarStack),
                 })
        {
            Check(FastAnimatorSystem.TryGet(race, label, out AnimatorStack? found) && ReferenceEquals(found, stack),
                $"mirror lookup failed for {race}:{label}");
        }

        Console.WriteLine("       mirror: 5 registrations, zero ':' ambiguities");

        Section("GREEN 4 - AnimatorSystem.Evaluate parity and zero allocation");
        VerifyEvaluateParity(fx);

        long evaluateFirst = MeasureEvaluateGreen(fx);
        long evaluateBytes = MeasureEvaluateGreen(fx);
        Console.WriteLine($"       200,000 Evaluate calls: first pass {evaluateFirst:N0} B, second pass {evaluateBytes:N0} B");
        Check(evaluateBytes == 0, $"patched AnimatorSystem.Evaluate allocated {evaluateBytes:N0} bytes in steady state");

        Section("GREEN 5 - AnimatorCache.Get parity with the animator stack (bit exact)");
        VerifyCacheParity(fx);

        Section("GREEN 6 - zero allocation across 200,000 refreshes");
        long getFirst = MeasureGetGreen(fx);
        long getBytes = MeasureGetGreen(fx);
        Console.WriteLine($"       200,000 Get calls on refresh boundaries: first pass {getFirst:N0} B, second pass {getBytes:N0} B");
        Check(getBytes == 0, $"patched AnimatorCache.Get allocated {getBytes:N0} bytes over {AllocIterations:N0} calls in steady state");

        Section("GREEN 7 - cache slot / expiry / clear semantics unchanged");
        VerifySlotSemantics(fx, "patched");
        VerifyColdTickQuirkPatched(fx);
        VerifyExpiryAndClear("patched");
        VerifyZeroRefreshTickPatched(fx);

        Section("GREEN 8 - null label maps to \"race:\" exactly like upstream");
        CleanAll();
        AnimatorSystem.Register($"{Fixture.Race}:", fx.TailStack);
        BodyAddonTransform nullLabel = AnimatorCache.Get(fx.Pawns[0], fx.South[0], AddonTail, null!, 8000);
        Check(RawTransform.From(nullLabel).Equals(RawTransform.From(
                fx.TailStack.Evaluate(fx.Pawns[0], fx.South[0], 8000, fx.Pawns[0].thingIDNumber))),
            "a null label does not resolve to the stack registered as \"race:\"");
        Console.WriteLine("       label null resolved through the same \"race:\" key upstream uses");

        Section("GREEN 9 - 16-thread parallel stress with concurrent cleanup");
        StressResult green = Stress(fx, assertNothing: false);
        Console.WriteLine($"       16 threads x {StressIterationsPerThread:N0} lookups: {green.Mismatches:N0} mismatches, {green.Exceptions} exceptions");
        Check(green.Exceptions == 0, "parallel stress raised " + green.Exceptions + " exception(s)");
        Check(green.Mismatches == 0,
            $"parallel stress observed {green.Mismatches:N0} torn/stale transforms; the per-entry lock is not protecting the 24-byte value");

        Console.WriteLine();
        Console.WriteLine($"PASS: {checks} GREEN assertions.");
    }

    private static MethodInfo AnimatorCacheGet()
    {
        return typeof(AnimatorCache).GetMethod(
            "Get",
            BindingFlags.Static | BindingFlags.Public,
            new[] { typeof(Pawn), typeof(PawnDrawParms), typeof(int), typeof(string), typeof(int) })!;
    }

    private static int UpstreamStackCount()
    {
        FieldInfo field = typeof(AnimatorSystem).GetField("stacks", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((System.Collections.IDictionary)field.GetValue(null)!).Count;
    }

    private static HashSet<MethodBase> OwnedPatchedMethods()
    {
        return Harmony.GetAllPatchedMethods()
            .Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Program.Owner) == true)
            .ToHashSet();
    }

    private static void VerifyEvaluateParity(Fixture fx)
    {
        (Pawn pawn, PawnDrawParms parms, string label, int tick)[] scenarios =
        {
            (fx.Pawns[0], fx.South[0], Fixture.EarLabel, 1000),
            (fx.Pawns[1], fx.South[1], Fixture.EarLabel, 1000),
            (fx.Pawns[0], fx.East[0], Fixture.RightEarLabel, 1040),
            (fx.Pawns[2], fx.East[2], Fixture.TailLabel, 1004),
            (fx.Pawns[2], fx.South[2], Fixture.TailLabel, 1004),
            (fx.Pawns[3], fx.South[3], Fixture.WingLabel, 123456),
            (fx.OtherPawn, fx.OtherSouth, Fixture.WingLabel, 4321),
            (fx.Pawns[0], fx.South[0], "UnregisteredLabel", 1000),
        };

        foreach ((Pawn pawn, PawnDrawParms parms, string label, int tick) in scenarios)
        {
            BodyAddonTransform patched = AnimatorSystem.Evaluate(pawn, parms, label, tick);
            BodyAddonTransform oracle = Oracle(pawn, parms, label, tick);
            Check(RawTransform.From(patched).Equals(RawTransform.From(oracle)),
                $"Evaluate mismatch for {pawn.def.defName}:{label} at tick {tick}: {RawTransform.From(patched)} vs {RawTransform.From(oracle)}");
        }

        // rightFlip must still flip the angle, which ValueFlip only does for a vertically facing pawn.
        BodyAddonTransform flipped = AnimatorSystem.Evaluate(fx.Pawns[0], fx.South[0], Fixture.RightEarLabel, 1040);
        BodyAddonTransform unflipped = AnimatorSystem.Evaluate(fx.Pawns[0], fx.South[0], Fixture.EarLabel, 1040);
        Check(!RawTransform.From(flipped).Equals(RawTransform.From(unflipped)),
            "rightFlip stacks now produce the same transform as non-flipped stacks");

        Console.WriteLine("       8 scenarios (registered, rightFlip, east/south, unregistered label) bit identical");
    }

    /// <summary>
    /// Independent oracle: evaluates the animator stack directly, bypassing both the cache and
    /// AnimatorSystem. Unregistered race/label pairs must yield <see cref="BodyAddonTransform.Identity" />.
    /// </summary>
    private static BodyAddonTransform Oracle(Pawn pawn, PawnDrawParms parms, string label, int tick)
    {
        AnimatorStack? stack = null;

        if (pawn.def.defName == Fixture.Race)
        {
            stack = label switch
            {
                Fixture.EarLabel => Fixture.BuildEarStack(false),
                Fixture.RightEarLabel => Fixture.BuildEarStack(true),
                Fixture.TailLabel => Fixture.BuildTailStack(),
                _ => null,
            };
        }
        else if (pawn.def.defName == Fixture.OtherRace)
        {
            stack = label == Fixture.WingLabel ? Fixture.BuildWingStack() : null;
        }
        else if (pawn.def.defName == Fixture.PreExistingRace && label == Fixture.EarLabel)
        {
            stack = Fixture.BuildEarStack(false);
        }

        return stack is null
            ? BodyAddonTransform.Identity
            : stack.Evaluate(pawn, parms, tick, pawn.thingIDNumber);
    }

    private static void VerifyCacheParity(Fixture fx)
    {
        (Pawn pawn, PawnDrawParms parms, int addonId, string label, int tick)[] scenarios =
        {
            (fx.Pawns[0], fx.South[0], AddonEar, Fixture.EarLabel, 1000),
            (fx.Pawns[0], fx.East[0], AddonRightEar, Fixture.RightEarLabel, 1004),
            (fx.Pawns[4], fx.East[4], AddonTail, Fixture.TailLabel, 2000),
            (fx.Pawns[4], fx.South[4], AddonTail, Fixture.TailLabel, 2000),
            (fx.OtherPawn, fx.OtherEast, AddonWing, Fixture.WingLabel, 777),
            (fx.Pawns[6], fx.South[6], 55, "UnregisteredLabel", 5000),
            (fx.Pawns[7], fx.South[7], 56, "Wing", 5000),
        };

        CleanAll();

        foreach ((Pawn pawn, PawnDrawParms parms, int addonId, string label, int tick) in scenarios)
        {
            RawTransform cached = RawTransform.From(AnimatorCache.Get(pawn, parms, addonId, label, tick));
            RawTransform oracle = RawTransform.From(Oracle(pawn, parms, label, tick));
            Check(cached.Equals(oracle),
                $"cache mismatch for {pawn.def.defName}:{label} (addon {addonId}) at tick {tick}: {cached} vs {oracle}");
        }

        Check(RawTransform.From(AnimatorCache.Get(fx.Pawns[6], fx.South[6], 55, "UnregisteredLabel", 5000))
                .Equals(RawTransform.From(BodyAddonTransform.Identity)),
            "unregistered label is not Identity");

        Console.WriteLine("       7 scenarios bit identical to the animator stack, unregistered label is Identity");
    }

    private static long MeasureEvaluateGreen(Fixture fx)
    {
        Pawn[] pawns = fx.Pawns;
        PawnDrawParms[] parms = fx.South;
        int count = pawns.Length;
        float sink = 0f;

        for (int i = 0; i < 1000; i++)
        {
            sink += AnimatorSystem.Evaluate(pawns[i % count], parms[i % count], Fixture.EarLabel, 1000 + i * 4).Angle;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < AllocIterations; i++)
        {
            int index = i % count;
            sink += AnimatorSystem.Evaluate(pawns[index], parms[index], Fixture.EarLabel, 1000 + i * 4).Angle;
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink);
        return bytes;
    }

    private static long MeasureGetGreen(Fixture fx)
    {
        Pawn[] pawns = fx.Pawns;
        PawnDrawParms[] parms = fx.South;
        int count = pawns.Length;
        int[] addons = { AddonEar, AddonRightEar, AddonTail, AddonWing };
        string[] labels = { Fixture.EarLabel, Fixture.RightEarLabel, Fixture.TailLabel, Fixture.WingLabel };
        float sink = 0f;

        // Warm every (pawn, addon) entry so the measured loop runs in steady state, then keep advancing
        // the tick by exactly one refresh interval so every measured call takes the refresh path.
        for (int i = 0; i < 1000; i++)
        {
            int index = i % count;
            int slot = i % addons.Length;
            sink += AnimatorCache.Get(pawns[index], parms[index], addons[slot], labels[slot], 1000 + i * 4).Angle;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < AllocIterations; i++)
        {
            int index = i % count;
            int slot = i % addons.Length;
            sink += AnimatorCache.Get(pawns[index], parms[index], addons[slot], labels[slot], 1000 + i * 4).Angle;
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink);
        return bytes;
    }

    private static void VerifyColdTickQuirkPatched(Fixture fx)
    {
        CleanAll();
        RawTransform cold = RawTransform.From(AnimatorCache.Get(fx.Pawns[0], fx.South[0], 900, Fixture.EarLabel, 2));
        Check(cold.Equals(default(RawTransform)),
            "the upstream cold-entry quirk (ticks 0..3 return the default transform) was not preserved: " + cold);
        RawTransform warm = RawTransform.From(AnimatorCache.Get(fx.Pawns[0], fx.South[0], 900, Fixture.EarLabel, 4));
        Check(!warm.Equals(default(RawTransform)), "tick 4 did not refresh the cold entry");
        Console.WriteLine("       cold-entry quirk at ticks 0..3 preserved bit for bit");
    }

    private static void VerifyZeroRefreshTickPatched(Fixture fx)
    {
        int original = HARAnimateSettings.CacheRefreshTick;
        HARAnimateSettings.CacheRefreshTick = 0;

        bool threw = false;
        BodyAddonTransform value = default;
        try
        {
            value = AnimatorCache.Get(fx.Pawns[0], fx.South[0], AddonEar, Fixture.EarLabel, 5000);
        }
        catch (Exception)
        {
            threw = true;
        }
        finally
        {
            HARAnimateSettings.CacheRefreshTick = original;
        }

        Check(!threw, "patched AnimatorCache.Get still throws when CacheRefreshTick is 0");
        Check(RawTransform.From(value).Equals(RawTransform.From(
                fx.EarStack.Evaluate(fx.Pawns[0], fx.South[0], 5000, fx.Pawns[0].thingIDNumber))),
            "the clamped refresh interval produced a wrong transform");
        Console.WriteLine("       CacheRefreshTick = 0 is clamped to 1 instead of crashing");
    }

    private sealed class StressResult
    {
        internal int Mismatches;
        internal int Exceptions;
    }

    /// <summary>
    /// 16 threads hammer <see cref="AnimatorCache.Get" /> for 4 shared entries while every 4th thread also
    /// calls <c>Cleanup</c>. Ticks are chosen so that one slot always maps to one tick, which makes the
    /// expected transform for a slot deterministic even when several threads race on the same entry.
    /// In GREEN any mismatch means a torn 24-byte BodyAddonTransform read.
    /// </summary>
    private static StressResult Stress(Fixture fx, bool assertNothing)
    {
        int[] ticks = new int[StressSlotCount];
        RawTransform[][] expected = new RawTransform[StressKeyCount][];

        (Pawn pawn, PawnDrawParms parms, int addon, string label)[] keys =
        {
            (fx.Pawns[0], fx.South[0], AddonEar, Fixture.EarLabel),
            (fx.Pawns[1], fx.East[1], AddonRightEar, Fixture.RightEarLabel),
            (fx.Pawns[2], fx.South[2], AddonTail, Fixture.TailLabel),
            (fx.OtherPawn, fx.OtherEast, AddonWing, Fixture.WingLabel),
        };

        for (int slot = 0; slot < StressSlotCount; slot++)
        {
            ticks[slot] = StressBaseTick + slot * Math.Max(1, HARAnimateSettings.CacheRefreshTick);
        }

        for (int key = 0; key < StressKeyCount; key++)
        {
            expected[key] = new RawTransform[StressSlotCount];
            for (int slot = 0; slot < StressSlotCount; slot++)
            {
                expected[key][slot] = RawTransform.From(Oracle(keys[key].pawn, keys[key].parms, keys[key].label, ticks[slot]));
            }
        }

        int mismatch = 0;
        int exceptions = 0;
        int iterations = assertNothing ? StressIterationsPerThread / 5 : StressIterationsPerThread;

        Parallel.For(0, StressThreads, thread =>
        {
            try
            {
                Random random = new(thread * 7919 + 13);
                for (int i = 0; i < iterations; i++)
                {
                    int key = random.Next(StressKeyCount);
                    int slot = random.Next(StressSlotCount);

                    BodyAddonTransform value = AnimatorCache.Get(
                        keys[key].pawn,
                        keys[key].parms,
                        keys[key].addon,
                        keys[key].label,
                        ticks[slot]);

                    if (!RawTransform.From(value).Equals(expected[key][slot]))
                    {
                        Interlocked.Increment(ref mismatch);
                    }

                    if ((thread & 3) == 0 && (i & 511) == 0)
                    {
                        Cleanup(ticks[StressSlotCount - 1] + 1, 0);
                    }
                }
            }
            catch (Exception)
            {
                Interlocked.Increment(ref exceptions);
            }
        });

        return new StressResult { Mismatches = Volatile.Read(ref mismatch), Exceptions = Volatile.Read(ref exceptions) };
    }
}
