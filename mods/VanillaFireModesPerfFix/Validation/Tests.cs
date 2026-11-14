using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using VFM_VanillaFireModes;
using VFM_VanillaFireModes.Settings;
using VFM_VanillaFireModes.Settings.CustomWeaponProfile;
using VFM_VanillaFireModes.Utilities;
using Verse;
using WhiteGiver.VanillaFireModesPerfFix;

namespace VanillaFireModesPerfFix.Validation;

internal static class Tests
{
    private const string ProbeHarmonyId = "whitegiver.vfm.perffix.validation.probes";
    private const int IdleVerbs = 1200;
    private const int MeleeVerbs = 900;

    private static readonly MethodInfo WarmupMethod = AccessTools.Method(typeof(Verb), "WarmupComplete")!;
    private static readonly MethodInfo TickMethod = AccessTools.Method(typeof(Verb), "VerbTick")!;
    private static readonly MethodInfo GetterMethod = AccessTools.PropertyGetter(typeof(Verb), "BurstShotCount")!;

    private static int checks;
    private static int failures;

    private static int modelChecks;
    private static int modelViolations;

    private static int vfmTickCalls;
    private static int vfmGetterCalls;
    private static int vfmLockCalls;

    internal static void Run(string mode)
    {
        Fixture.Setup();
        Fixture.InstallBurstCapture();

        switch (mode)
        {
            case "red":
                Red();
                break;
            case "green":
                Green();
                break;
            case "verify":
                Verify();
                break;
            default:
                throw new ArgumentException("Unknown validation mode: " + mode);
        }

        Finish();
    }

    private static void Finish()
    {
        Console.WriteLine();
        Console.WriteLine($"{(failures == 0 ? "OK" : "FAILED")}: {checks - failures}/{checks} checks passed.");
        if (failures != 0)
        {
            throw new InvalidOperationException($"{failures} validation check(s) failed.");
        }
    }

    // ==================================================================== RED

    private static void Red()
    {
        Info("=== RED: Vanilla Fire Modes 1.8.0 alone (real DLL, real 1.7 game assembly) ===");
        InstallVfmPatches();
        RequireVfmPatches("RED");

        List<Sample> transcript = Transcript();
        WriteTranscript("red", transcript);
        Measure("RED");

        Info("=== RED probe accounting (1,200 idle verbs) ===");
        InstrumentVfmPatches();
        ProbeCounts probes = CountVfmProbes();
        Info($"VFM LockCount={probes.Lock}, BurstShotCountPostFix={probes.Getter}, Postfix_Cleanup probes={probes.Tick}, this mod's probes={probes.Own}");
        Require(probes.Tick > 0, "RED: every non-bursting VerbTick enters VFM's Postfix_Cleanup and probes the table");
        Require(probes.Getter > 0, "RED: every BurstShotCount read enters VFM's postfix and probes the table");
    }

    // ==================================================================== GREEN

    private static void Green()
    {
        Info("=== GREEN: Vanilla Fire Modes + VanillaFireModesPerfFix ===");
        InstallVfmPatches();
        RequireVfmPatches("GREEN precondition");

        List<string> before = PatchSnapshot();
        int installed = VanillaFireModesPerfFixMod.ApplyPatches();
        ReportInstall(installed);
        Require(installed == 3, "installer replaced three Vanilla Fire Modes patch methods");
        AssertReplacementPatchGraph(before);

        List<Sample> transcript = Transcript();
        WriteTranscript("green", transcript);
        Measure("GREEN");

        InstrumentVfmPatches();
        ProbeCounts probes = CountVfmProbes();
        Info($"after install: VFM LockCount={probes.Lock}, BurstShotCountPostFix={probes.Getter}, Postfix_Cleanup={probes.Tick}, this mod's probes={probes.Own}");
        Require(probes.Tick == 0 && probes.Getter == 0 && probes.Lock == 0, "GREEN runs none of Vanilla Fire Modes' burst patches");
        Require(probes.Own == 0, "GREEN: 1,200 idle verbs perform zero burst-table probes");

        AssertCleanupSemantics();
        AssertSecondInstallIsIdempotent();
        AssertInstallPhases();
    }

    // ==================================================================== VERIFY

    private static void Verify()
    {
        Info("=== VERIFY: RED (Vanilla Fire Modes) vs GREEN (VanillaFireModesPerfFix) ===");
        InstallVfmPatches();
        RequireVfmPatches("RED");

        List<Sample> redTranscript = Transcript();
        WriteTranscript("red", redTranscript);
        Perf red = Measure("RED");

        InstrumentVfmPatches();
        ProbeCounts redProbes = CountVfmProbes();
        Info($"RED probes: LockCount={redProbes.Lock}, getter={redProbes.Getter}, tick={redProbes.Tick}, this mod={redProbes.Own}");

        List<string> before = PatchSnapshot();
        int installed = VanillaFireModesPerfFixMod.ApplyPatches();
        ReportInstall(installed);
        Require(installed == 3, "installer replaced three Vanilla Fire Modes patch methods");
        AssertReplacementPatchGraph(before);

        List<Sample> greenTranscript = Transcript();
        WriteTranscript("green", greenTranscript);
        Perf green = Measure("GREEN");

        InstrumentVfmPatches();
        ProbeCounts greenProbes = CountVfmProbes();
        Info($"GREEN probes: LockCount={greenProbes.Lock}, getter={greenProbes.Getter}, tick={greenProbes.Tick}, this mod={greenProbes.Own}");

        Info("=== parity ===");
        CompareTranscripts(redTranscript, greenTranscript);
        ComparePerf(red, green);
        Require(redProbes.Tick > 0, "RED performs one burst-table probe per non-bursting VerbTick");
        Require(greenProbes.Tick == 0 && greenProbes.Getter == 0 && greenProbes.Lock == 0, "GREEN runs none of Vanilla Fire Modes' burst patches");
        Require(greenProbes.Own == 0, "GREEN: idle VerbTick and BurstShotCount traffic performs zero table probes");

        AssertCleanupSemantics();
        AssertSecondInstallIsIdempotent();
        AssertInstallPhases();
        AssertAbsentVfmIsNoOp();
    }

    // ==================================================================== install / patch graph

    private static void InstallVfmPatches()
    {
        Harmony harmony = new(Program.VfmOwner);
        harmony.UnpatchAll(Program.VfmOwner);
        harmony.PatchAll(typeof(VanillaFireModes).Assembly);
    }

    private static void RequireVfmPatches(string label)
    {
        Require(HasPatch(WarmupMethod, Program.VfmOwner, HarmonyPatchType.Prefix), $"{label}: VFM prefix on Verb.WarmupComplete");
        Require(HasPatch(GetterMethod, Program.VfmOwner, HarmonyPatchType.Postfix), $"{label}: VFM postfix on Verb.get_BurstShotCount");
        Require(HasPatch(TickMethod, Program.VfmOwner, HarmonyPatchType.Postfix), $"{label}: VFM postfix on Verb.VerbTick");
    }

    private static bool HasPatch(MethodBase method, string owner, HarmonyPatchType type)
    {
        Patches? info = Harmony.GetPatchInfo(method);
        if (info == null)
        {
            return false;
        }

        IEnumerable<Patch> patches = type switch
        {
            HarmonyPatchType.Prefix => info.Prefixes,
            HarmonyPatchType.Postfix => info.Postfixes,
            HarmonyPatchType.Finalizer => info.Finalizers,
            _ => info.Transpilers,
        };

        return patches.Any(patch => patch.owner == owner);
    }

    private static void ReportInstall(int installed)
    {
        Info($"installer returned {installed}; steps:");
        foreach (string step in VanillaFireModesPerfFixMod.InstalledPatches)
        {
            Info("  - " + step);
        }

        foreach (LogMessage message in Log.Messages)
        {
            if (message.text.Contains("VFM Perf Fix", StringComparison.Ordinal))
            {
                Info("  game log: " + message.text);
            }
        }
    }

    private static void AssertReplacementPatchGraph(List<string> before)
    {
        List<string> after = PatchSnapshot();
        List<string> removed = before.Except(after).ToList();
        List<string> added = after.Except(before).ToList();

        Info("patch graph removed:");
        foreach (string line in removed)
        {
            Info("  - " + line);
        }

        Info("patch graph added:");
        foreach (string line in added)
        {
            Info("  + " + line);
        }

        Require(removed.Count == 3, "exactly three patch methods disappear from the graph");
        Require(removed.All(line => line.Contains("|" + Program.VfmOwner + "|", StringComparison.Ordinal)), "every removed patch belongs to Vanilla Fire Modes");
        Require(removed.Any(line => line.Contains("::WarmupComplete|prefix", StringComparison.Ordinal)), "the removed set contains VFM's WarmupComplete prefix");
        Require(removed.Any(line => line.Contains("::get_BurstShotCount|postfix", StringComparison.Ordinal)), "the removed set contains VFM's BurstShotCount postfix");
        Require(removed.Any(line => line.Contains("::VerbTick|postfix", StringComparison.Ordinal)), "the removed set contains VFM's VerbTick cleanup postfix");

        Require(added.Count == 3, "exactly three patch methods are added");
        Require(added.All(line => line.Contains("|" + Program.ModOwner + "|", StringComparison.Ordinal)), "every added patch belongs to this mod");
        Require(added.Any(line => line.Contains("::WarmupComplete|prefix", StringComparison.Ordinal)), "the new WarmupComplete prefix is live");
        Require(added.Any(line => line.Contains("::WarmupComplete|finalizer", StringComparison.Ordinal)), "the new WarmupComplete finalizer is live");
        Require(added.Any(line => line.Contains("::get_BurstShotCount|postfix", StringComparison.Ordinal)), "the new BurstShotCount postfix is live");

        Patches? tickInfo = Harmony.GetPatchInfo(TickMethod);
        Require(tickInfo == null || tickInfo.Postfixes.Count == 0, "Verb.VerbTick carries no postfix any more, so the per-tick table probe is gone");
        Require(tickInfo == null || !tickInfo.Owners.Contains(Program.VfmOwner), "Verb.VerbTick has no Vanilla Fire Modes patch left");
    }

    private static void AssertSecondInstallIsIdempotent()
    {
        List<string> before = PatchSnapshot();
        int again = VanillaFireModesPerfFixMod.ApplyPatches();
        List<string> after = PatchSnapshot();
        Require(again == 3, "a second install reports the same three replacement patch methods");
        Require(before.SequenceEqual(after), "a second install adds no duplicate patch entry");
    }

    /// <summary>
    /// Both real load phases: VFM patches in its <see cref="Mod"/> constructor, so this mod must install there too,
    /// and <c>[StaticConstructorOnStartup]</c> must repair the install if VFM ever patched after us.
    /// </summary>
    private static void AssertInstallPhases()
    {
        VanillaFireModesPerfFixMod.RemovePatches();
        InstallVfmPatches();
        _ = new VanillaFireModesPerfFixMod(null!);
        Require(HasPatch(WarmupMethod, Program.ModOwner, HarmonyPatchType.Prefix), "the Mod constructor installs the replacement prefix");
        Require(!HasPatch(WarmupMethod, Program.VfmOwner, HarmonyPatchType.Prefix), "the Mod constructor removes VFM's prefix");

        VanillaFireModesPerfFixMod.RemovePatches();
        InstallVfmPatches();
        RuntimeHelpers.RunClassConstructor(typeof(VanillaFireModesPerfFixStartup).TypeHandle);        Require(HasPatch(WarmupMethod, Program.ModOwner, HarmonyPatchType.Prefix), "StaticConstructorOnStartup repairs the install after a late VFM patch");
        Require(!HasPatch(WarmupMethod, Program.VfmOwner, HarmonyPatchType.Prefix), "StaticConstructorOnStartup removes the late VFM prefix");
        Patches? getter = Harmony.GetPatchInfo(GetterMethod);
        Require(getter != null && getter.Postfixes.Count(p => p.owner is Program.ModOwner or Program.VfmOwner) == 1,
            "exactly one burst-length authority is attached to the getter after both phases");
    }

    private static void AssertAbsentVfmIsNoOp()
    {
        VanillaFireModesPerfFixMod.RemovePatches();
        int installed = VanillaFireModesPerfFixMod.ApplyPatches();
        Require(installed == 0, "with no Vanilla Fire Modes patches to replace the installer changes nothing");
        Require(!HasPatch(WarmupMethod, Program.ModOwner, HarmonyPatchType.Prefix) && !HasPatch(GetterMethod, Program.ModOwner, HarmonyPatchType.Postfix),
            "the no-op install leaves no replacement patch behind");
        Require(!HasPatch(WarmupMethod, Program.VfmOwner, HarmonyPatchType.Prefix), "the no-op install does not resurrect Vanilla Fire Modes' patches");
        VanillaFireModesPerfFixMod.RemovePatches();
    }

    // ==================================================================== transcript

    private readonly record struct Sample(string Case, string Point, int Count, string State, int Shots, int Vanilla, int Expected);

    private static List<Sample> Transcript()
    {
        List<Sample> samples = new();
        modelChecks = 0;
        modelViolations = 0;

        foreach (int burst in new[] { 1, 3, 8 })
        {
            foreach (VFM_FireMode mode in new[] { VFM_FireMode.Default, VFM_FireMode.Precision, VFM_FireMode.Burst, VFM_FireMode.Suppression })
            {
                foreach (BurstShotOption option in new[] { BurstShotOption.Linear, BurstShotOption.Additive, BurstShotOption.Tent, BurstShotOption.Adaptive })
                {
                    Fixture.ConfigureBurstOptions(option);
                    string name = $"burst={burst}|mode={mode}|option={option}";
                    ThingWithComps weapon = Fixture.NewRangedWeapon("ValidationMatrix_" + burst + "_" + mode + "_" + option, burst);
                    Pawn pawn = Fixture.NewPawn("ValidationMatrixPawn_" + burst + "_" + mode + "_" + option, mode);
                    int expected = FireModeDB.GetBurstCount(mode, burst, weapon.def.defName);
                    if (burst > 1 && expected == burst)
                    {
                        throw new InvalidOperationException($"{name}: the fixture option must change the burst length (expected {expected}).");
                    }

                    Lifecycle(samples, name, Fixture.NewRangedVerb(pawn, weapon), pawn, mode, NextMode(mode), burst, expected);
                }
            }
        }

        Fixture.ConfigureBurstOptions(BurstShotOption.Additive);

        // VFM exclusions: one-use verbs, non-pawn casters, melee verbs.
        Pawn excludedPawn = Fixture.NewPawn("ValidationOneUsePawn", VFM_FireMode.Burst);
        ThingWithComps oneUseWeapon = Fixture.NewRangedWeapon("ValidationOneUseRifle", 3);
        Lifecycle(samples, "one-use|burst=3|mode=Burst|option=Additive", Fixture.NewOneUseVerb(excludedPawn, oneUseWeapon),
            excludedPawn, VFM_FireMode.Burst, VFM_FireMode.Suppression, 3, 3);

        Pawn turretWeaponPawn = Fixture.NewPawn("ValidationTurretWeaponPawn", VFM_FireMode.Burst);
        ThingWithComps turretWeapon = Fixture.NewRangedWeapon("ValidationTurretRifle", 3);
        Verb_Shoot turretVerb = new()
        {
            verbProps = turretWeapon.def.Verbs[0],
            verbTracker = turretWeapon.GetComp<CompEquippable>().verbTracker,
            caster = Fixture.NewBuildingCaster("ValidationTurret"),
        };
        Lifecycle(samples, "building caster|burst=3|mode=Burst|option=Additive", turretVerb, turretWeaponPawn, VFM_FireMode.Burst, VFM_FireMode.Suppression, 3, 3);

        Pawn meleePawn = Fixture.NewPawn("ValidationMeleePawn", VFM_FireMode.Burst);
        ThingWithComps meleeWeapon = Fixture.NewMeleeWeapon("ValidationMeleeBeak");
        Lifecycle(samples, "melee verb|burst=1|mode=Burst|option=Additive", Fixture.NewMeleeVerb(meleePawn, meleeWeapon),
            meleePawn, VFM_FireMode.Burst, VFM_FireMode.Suppression, 1, 1);

        // A pawn that does not carry VFM's comp still gets the Default-mode treatment.
        Pawn noCompPawn = Fixture.NewPawn("ValidationNoCompPawn", VFM_FireMode.Default, withFireModeComp: false);
        ThingWithComps noCompWeapon = Fixture.NewRangedWeapon("ValidationNoCompRifle", 3);
        Lifecycle(samples, "pawn without VFM comp|burst=3|mode=Default|option=Additive", Fixture.NewRangedVerb(noCompPawn, noCompWeapon),
            noCompPawn, VFM_FireMode.Default, VFM_FireMode.Default, 3, FireModeDB.GetBurstCount(VFM_FireMode.Default, 3, noCompWeapon.def.defName));

        // NPC fire modes switched off: every value must stay vanilla.
        VanillaFireModes.settings.enableFireModeForNPC = false;
        Pawn npcOffPawn = Fixture.NewPawn("ValidationNpcOffPawn", VFM_FireMode.Suppression);
        ThingWithComps npcOffWeapon = Fixture.NewRangedWeapon("ValidationNpcOffRifle", 3);
        Lifecycle(samples, "npc fire modes disabled|burst=3|mode=Suppression|option=Additive", Fixture.NewRangedVerb(npcOffPawn, npcOffWeapon),
            npcOffPawn, VFM_FireMode.Suppression, VFM_FireMode.Burst, 3, 3);
        VanillaFireModes.settings.enableFireModeForNPC = true;

        // Per-weapon profile overrides take the profile's explicit burst counts.
        ThingWithComps profiled = Fixture.NewRangedWeapon("ValidationProfiledRifle", 5);
        VanillaFireModes.settings.CustomWeaponProfiles[profiled.def.defName] = new VFM_WeaponProfile(
            profiled.def.defName,
            new VFM_FireModeProfile(1f, 1f, 1f, 9),
            new VFM_FireModeProfile(1f, 1f, 1f, 7),
            new VFM_FireModeProfile(1f, 1f, 1f, 3),
            new VFM_FireModeProfile(1f, 1f, 1f, 12));
        foreach (VFM_FireMode mode in new[] { VFM_FireMode.Default, VFM_FireMode.Precision, VFM_FireMode.Burst, VFM_FireMode.Suppression })
        {
            Pawn pawn = Fixture.NewPawn("ValidationProfilePawn" + mode, mode);
            Lifecycle(samples, $"profile|burst=5|mode={mode}", Fixture.NewRangedVerb(pawn, profiled), pawn, mode, NextMode(mode), 5,
                FireModeDB.GetBurstCount(mode, 5, profiled.def.defName));
        }

        VanillaFireModes.settings.CustomWeaponProfiles.Clear();

        Info($"transcript model: {modelChecks - modelViolations}/{modelChecks} samples match the expected VFM values");
        Require(modelViolations == 0, "every sampled point matches the modelled burst length before RED/GREEN are compared");
        return samples;
    }

    private static void Lifecycle(
        List<Sample> samples,
        string name,
        Verb verb,
        Pawn pawn,
        VFM_FireMode first,
        VFM_FireMode second,
        int vanilla,
        int expected)
    {
        verb.Reset();
        Fixture.SetMode(pawn, first);
        Add(samples, name, "pre", verb, null, vanilla, expected);

        verb.WarmupComplete();
        Add(samples, name, "warmed", verb, Fixture.CapturedBurstShotsLeft, vanilla, expected);

        Fixture.SetMode(pawn, second);
        Add(samples, name, "mode-changed-mid-burst", verb, null, vanilla, expected);

        verb.Reset();
        Add(samples, name, "burst-ended-before-tick", verb, null, vanilla, expected);

        verb.VerbTick();
        Add(samples, name, "after-tick", verb, null, vanilla, expected);
    }

    private static VFM_FireMode NextMode(VFM_FireMode mode) => mode switch
    {
        VFM_FireMode.Default => VFM_FireMode.Suppression,
        VFM_FireMode.Precision => VFM_FireMode.Burst,
        VFM_FireMode.Burst => VFM_FireMode.Precision,
        _ => VFM_FireMode.Default,
    };

    private static void Add(List<Sample> samples, string name, string point, Verb verb, int? shots, int vanilla, int expected)
    {
        Sample sample = new(name, point, verb.BurstShotCount, verb.state.ToString(), shots ?? Fixture.ShotsLeft(verb), vanilla, expected);
        samples.Add(sample);

        bool ok = point switch
        {
            "pre" or "after-tick" => sample.Count == vanilla,
            "warmed" => sample.Count == expected && sample.Shots == expected,
            "mode-changed-mid-burst" => sample.Count == expected,
            "burst-ended-before-tick" => sample.Count == expected || sample.Count == vanilla,
            _ => true,
        };

        modelChecks++;
        if (ok)
        {
            return;
        }

        modelViolations++;
        if (modelViolations <= 5)
        {
            Info("  model mismatch: " + Format(sample));
        }
    }

    private static void WriteTranscript(string phase, List<Sample> samples)
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, phase + "-transcript.txt");
        System.IO.File.WriteAllLines(path, samples.Select(Format));
        Info($"{phase}: {samples.Count} samples written to {path}");
    }

    private static string Format(Sample sample)
    {
        return $"{sample.Case}|{sample.Point}|count={sample.Count}|state={sample.State}|shots={sample.Shots}|vanilla={sample.Vanilla}|expected={sample.Expected}";
    }

    private static void CompareTranscripts(List<Sample> red, List<Sample> green)
    {
        Require(red.Count == green.Count, $"both phases sample the same points ({red.Count} vs {green.Count})");

        int mismatches = 0;
        int documentedWindow = 0;
        int staleInGreen = 0;
        int sampleCount = Math.Min(red.Count, green.Count);
        for (int i = 0; i < sampleCount; i++)
        {
            Sample r = red[i];
            Sample g = green[i];
            if (r.Case != g.Case || r.Point != g.Point || r.State != g.State || r.Shots != g.Shots || r.Vanilla != g.Vanilla || r.Expected != g.Expected)
            {
                mismatches++;
                if (mismatches <= 5)
                {
                    Info($"  mismatch: {Format(r)}  !=  {Format(g)}");
                }

                continue;
            }

            if (r.Count == g.Count)
            {
                continue;
            }

            if (g.Point == "burst-ended-before-tick")
            {
                // Documented difference: VFM keeps the frozen value alive until that verb's next VerbTick,
                // this mod stops applying it the moment the verb is no longer bursting.
                documentedWindow++;
                if (g.Count != g.Vanilla)
                {
                    staleInGreen++;
                }

                continue;
            }

            mismatches++;
            if (mismatches <= 5)
            {
                Info($"  mismatch: {Format(r)}  !=  {Format(g)}");
            }
        }

        Info($"compared {sampleCount} samples: {mismatches} mismatches, {documentedWindow} post-burst samples where this mod returns the vanilla value earlier than VFM");
        Require(mismatches == 0, "GREEN reproduces every RED burst length bit for bit while the burst is live");
        Require(staleInGreen == 0, "GREEN never returns a cached burst length after the burst ended");
        Require(documentedWindow > 0, "the post-burst window is exercised (VFM's <=1 tick stale value)");
    }

    // ==================================================================== performance

    private readonly record struct Perf(
        string Phase,
        double TickMs,
        long TickBytes,
        int TickCalls,
        double GetterMs,
        long GetterBytes,
        int GetterCalls);

    private const int Passes = 250;

    private static Perf Measure(string phase)
    {
        List<Verb> verbs = Fixture.NewIdleColony(MeleeVerbs, IdleVerbs - MeleeVerbs);
        Fixture.PrimeBursts(verbs);

        for (int i = 0; i < verbs.Count; i++)
        {
            verbs[i].VerbTick();
            _ = verbs[i].BurstShotCount;
        }

        double tickMs = double.MaxValue;
        long tickBytes = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch watch = Stopwatch.StartNew();
            for (int t = 0; t < Passes; t++)
            {
                for (int i = 0; i < verbs.Count; i++)
                {
                    verbs[i].VerbTick();
                }
            }

            watch.Stop();
            tickBytes = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            tickMs = Math.Min(tickMs, watch.Elapsed.TotalMilliseconds);
        }

        double getterMs = double.MaxValue;
        long getterBytes = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            Stopwatch watch = Stopwatch.StartNew();
            for (int t = 0; t < Passes; t++)
            {
                for (int i = 0; i < verbs.Count; i++)
                {
                    _ = verbs[i].BurstShotCount;
                }
            }

            watch.Stop();
            getterBytes = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
            getterMs = Math.Min(getterMs, watch.Elapsed.TotalMilliseconds);
        }

        // The last of several passes runs after every tiering/JIT transition, so its allocation count is the
        // steady-state one; the timing is the best of the 4 measured passes.
        tickBytes = SteadyAllocation(verbs, tick: true);
        getterBytes = SteadyAllocation(verbs, tick: false);

        Perf perf = new(phase, tickMs, tickBytes, Passes * verbs.Count, getterMs, getterBytes, Passes * verbs.Count);
        Report(perf);
        return perf;
    }

    /// <summary>Allocation count of the last of several identical passes, i.e. after tiering has settled.</summary>
    private static long SteadyAllocation(List<Verb> verbs, bool tick)
    {
        long bytes = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            long allocBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < Passes; t++)
            {
                for (int i = 0; i < verbs.Count; i++)
                {
                    if (tick)
                    {
                        verbs[i].VerbTick();
                    }
                    else
                    {
                        _ = verbs[i].BurstShotCount;
                    }
                }
            }

            bytes = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        }

        return bytes;
    }

    private static void Report(Perf perf)
    {
        double tickNs = perf.TickMs * 1_000_000.0 / perf.TickCalls;
        double getterNs = perf.GetterMs * 1_000_000.0 / perf.GetterCalls;
        Info($"{perf.Phase} idle colony ({IdleVerbs} verbs, {MeleeVerbs} of them melee):");
        Info($"  VerbTick        {perf.TickCalls,9:N0} calls {perf.TickMs,8:0.###} ms  {tickNs,6:0.##} ns/call  {perf.TickBytes,6:N0} B");
        Info($"  BurstShotCount  {perf.GetterCalls,9:N0} reads {perf.GetterMs,8:0.###} ms  {getterNs,6:0.##} ns/call  {perf.GetterBytes,6:N0} B");
        Info($"  modelled 10,000 VerbTick calls per frame: {tickNs * 10_000 / 1_000_000.0:0.####} ms/frame");
    }

    private static void ComparePerf(Perf red, Perf green)
    {
        double redTickNs = red.TickMs * 1_000_000.0 / red.TickCalls;
        double greenTickNs = green.TickMs * 1_000_000.0 / green.TickCalls;
        double redGetterNs = red.GetterMs * 1_000_000.0 / red.GetterCalls;
        double greenGetterNs = green.GetterMs * 1_000_000.0 / green.GetterCalls;

        Info($"VerbTick: {redTickNs:0.##} -> {greenTickNs:0.##} ns/call ({redTickNs / greenTickNs:0.#}x); " +
             $"BurstShotCount: {redGetterNs:0.##} -> {greenGetterNs:0.##} ns/call ({redGetterNs / Math.Max(greenGetterNs, 0.0001):0.#}x)");

        Require(green.TickMs * 2 < red.TickMs, "GREEN: the idle VerbTick loop takes less than half of VFM's time");
        Require(green.TickBytes == 0, "GREEN: the idle VerbTick loop allocates nothing");
        Require(green.GetterBytes == 0, "GREEN: the idle BurstShotCount loop allocates nothing");
        Require(red.TickBytes == 0, "RED baseline allocates nothing on the tick path either (the cost is table probing)");
        Require(redGetterNs > greenGetterNs, "GREEN: BurstShotCount reads are cheaper than VFM's unconditional table probe");
    }

    private static void AssertCleanupSemantics()
    {
        Fixture.ConfigureBurstOptions(BurstShotOption.Additive);
        VanillaFireModes.settings.defaultBurstAdditiveBonus = 2;
        VanillaFireModes.settings.precisionBurstAdditiveBonus = 4;

        Pawn pawn = Fixture.NewPawn("ValidationCleanupPawn", VFM_FireMode.Burst);
        ThingWithComps weapon = Fixture.NewRangedWeapon("ValidationCleanupRifle", 3);
        Verb verb = Fixture.NewRangedVerb(pawn, weapon);

        verb.Reset();
        int vanilla = verb.BurstShotCount;
        verb.WarmupComplete();
        int locked = Fixture.CapturedBurstShotsLeft;
        Require(vanilla == 3, "cleanup fixture base burst is 3");
        Require(locked == 5, "cleanup fixture locks the additive burst of 5 (Burst mode +2)");
        Require(verb.BurstShotCount == 5, "the frozen burst is visible while the burst runs");

        verb.Reset();
        Require(verb.BurstShotCount == vanilla, "the frozen burst disappears the moment the verb is no longer bursting");

        Fixture.SetMode(pawn, VFM_FireMode.Precision);
        Require(verb.BurstShotCount == vanilla, "a mode change after the burst cannot resurrect the frozen value");

        verb.WarmupComplete();
        int precision = Fixture.CapturedBurstShotsLeft;
        Require(verb.BurstShotCount == precision, "the next burst locks the new fire mode");
        Require(precision == 7 && precision != locked, "Precision mode (+4) produced a different burst length than Burst mode (+2)");
        verb.Reset();
        verb.VerbTick();
        Require(verb.BurstShotCount == vanilla, "VerbTick after the burst still reports the vanilla burst length");
    }

    // ==================================================================== probe accounting

    private readonly record struct ProbeCounts(int Lock, int Getter, int Tick, int Own);

    private static void InstrumentVfmPatches()
    {
        Type vfm = AccessTools.TypeByName("VFM_VanillaFireModes.Patches.Patch_BurstShotCount")
            ?? throw new InvalidOperationException("VFM patch type missing.");
        Harmony harmony = new(ProbeHarmonyId);
        harmony.UnpatchAll(ProbeHarmonyId);
        harmony.Patch(AccessTools.Method(vfm, "LockCount", new[] { typeof(Verb) })!, prefix: new HarmonyMethod(typeof(Tests), nameof(CountLock)));
        harmony.Patch(AccessTools.Method(vfm, "BurstShotCountPostFix")!, prefix: new HarmonyMethod(typeof(Tests), nameof(CountGetter)));
        harmony.Patch(AccessTools.Method(vfm, "Postfix_Cleanup", new[] { typeof(Verb) })!, prefix: new HarmonyMethod(typeof(Tests), nameof(CountTick)));
    }

    private static bool CountLock()
    {
        vfmLockCalls++;
        return true;
    }

    private static bool CountGetter()
    {
        vfmGetterCalls++;
        return true;
    }

    private static bool CountTick(Verb __0)
    {
        if (__0.state != VerbState.Bursting)
        {
            vfmTickCalls++;
        }

        return true;
    }

    private static ProbeCounts CountVfmProbes()
    {
        List<Verb> verbs = Fixture.NewIdleColony(MeleeVerbs, IdleVerbs - MeleeVerbs);
        for (int i = 0; i < verbs.Count; i++)
        {
            verbs[i].VerbTick();
            _ = verbs[i].BurstShotCount;
        }

        vfmTickCalls = 0;
        vfmGetterCalls = 0;
        vfmLockCalls = 0;
        BurstCountCache.ResetProbes();

        for (int t = 0; t < 100; t++)
        {
            for (int i = 0; i < verbs.Count; i++)
            {
                verbs[i].VerbTick();
            }
        }

        int tickProbes = BurstCountCache.Probes;
        BurstCountCache.ResetProbes();

        for (int t = 0; t < 20; t++)
        {
            for (int i = 0; i < verbs.Count; i++)
            {
                _ = verbs[i].BurstShotCount;
            }
        }

        return new ProbeCounts(vfmLockCalls, vfmGetterCalls, vfmTickCalls, tickProbes + BurstCountCache.Probes);
    }

    // ==================================================================== patch snapshot

    private static List<string> PatchSnapshot()
    {
        List<string> lines = new();
        foreach (MethodBase method in Harmony.GetAllPatchedMethods())
        {
            Patches? info = Harmony.GetPatchInfo(method);
            if (info == null)
            {
                continue;
            }

            Add(lines, method, "prefix", info.Prefixes);
            Add(lines, method, "postfix", info.Postfixes);
            Add(lines, method, "transpiler", info.Transpilers);
            Add(lines, method, "finalizer", info.Finalizers);
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static void Add(List<string> lines, MethodBase method, string kind, IEnumerable<Patch> patches)
    {
        string target = $"{method.DeclaringType?.FullName}::{method.Name}";
        foreach (Patch patch in patches)
        {
            if (patch.owner is not (Program.VfmOwner or Program.ModOwner))
            {
                continue;
            }

            lines.Add($"{target}|{kind}|{patch.owner}|{patch.PatchMethod.DeclaringType?.Name}.{patch.PatchMethod.Name}|priority={patch.priority}");
        }
    }

    // ==================================================================== helpers

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
