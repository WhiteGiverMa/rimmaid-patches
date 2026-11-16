using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml;
using DMSSR.FloatMenus;
using DMSSR.Hediffs;
using DMSSR.Jobs;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using WhiteGiver.DMSSRNeuroglueFix;

namespace DMSSRNeuroglueFix.Validation;

internal static class Tests
{
    private static int assertions;

    internal static void Run(string mode, Assembly patchAssembly, string dmsRoot, string dmssrRoot, string modRoot)
    {
        if (mode == "red")
        {
            RunRed();
        }
        else
        {
            RunGreen(patchAssembly, dmsRoot, dmssrRoot, modRoot);
        }

        Console.WriteLine();
        Console.WriteLine($"PASS [{mode.ToUpperInvariant()}]: {assertions} assertions.");
    }

    private static void RunRed()
    {
        Console.WriteLine("=== RED: Upstream DMSSR behaviour before DMSSRNeuroglueFix ===");
        Fixture fx = Fixture.Create();

        Check(!Harmony.HasAnyPatches(DMSSRNeuroglueFixMod.HarmonyId), "RED must not have DMSSRNeuroglueFix patches installed.");

        // 1. Reproduce user's exact bug: mech at 62.98 fuel with 9 DegenerativeSyndrome parts uses 1 Neuroglue.
        Pawn skylark = fx.SpawnColonyMech(new IntVec3(3, 0, 3));
        Hediff_BiogelReservoir reservoir = fx.EnsureReservoirWithFuel(skylark, 62.98f);
        fx.AddDegenerativeToParts(skylark, 9);

        Check(CountHediffs(skylark, fx.DegenerativeDef) == 9, "Fixture skylark should start with 9 degenerative lesions.");

        ThingWithComps gel = fx.SpawnNeuroglue(new IntVec3(4, 0, 3), count: 5);
        Thing carried = fx.TakeOneToCarry(skylark, gel);

        Toil finalToil = Fixture.BuildFinalUseNeuroglueToil(skylark, skylark, carried, out _);
        finalToil.initAction();
        Fixture.TickHediffsOnce(skylark);

        // Upstream refills 25 biogel (62.98 -> 87.98), matching user's screenshot!
        Check(Math.Abs(reservoir.Fuel - 87.98f) < 0.05f, $"Expected upstream fuel ~87.98, got {reservoir.Fuel:F2}.");
        // BUG 1: NeuroglueHigh was skipped, so ALL 9 degenerative lesions remain!
        Check(!HasHediff(skylark, fx.NeuroglueHighDef), "Upstream unexpectedly applied NeuroglueHigh.");
        Check(CountHediffs(skylark, fx.DegenerativeDef) == 9, "Upstream unexpectedly cured degenerative lesions.");
        Console.WriteLine($"  [Reproduced] Fuel rose 62.98 -> {reservoir.Fuel:F2}, NeuroglueHigh=false, DegenerativeSyndrome stuck at 9/9.");

        // 2. Reproduce BUG 2: mech with 100 fuel (or no reservoir) + DegenerativeSyndrome is blocked by CanRefill.
        Pawn deathCheatMech = fx.SpawnColonyMech(new IntVec3(5, 0, 3));
        fx.AddDegenerativeToParts(deathCheatMech, 2);
        Check(!JobDriver_UseNeuroglue.CanRefill(deathCheatMech), "Upstream CanRefill should block mechs without reservoir.");

        fx.EnsureReservoirWithFuel(deathCheatMech, 100f);
        Check(!JobDriver_UseNeuroglue.CanRefill(deathCheatMech), "Upstream CanRefill should block mechs at 100/100 fuel even when diseased.");
        Console.WriteLine("  [Reproduced] CanRefill returns false at 100/100 fuel or without reservoir despite active DegenerativeSyndrome.");
    }

    private static void RunGreen(Assembly patchAssembly, string dmsRoot, string dmssrRoot, string modRoot)
    {
        Console.WriteLine("=== GREEN: Verifying DMSSRNeuroglueFix end-to-end ===");
        Fixture fx = Fixture.Create();

        DMSSRNeuroglueFixMod.ApplyPatches();
        NeuroglueDefs.ResetForTesting();
        MechBiogelCache.ResetForTesting();
        NeuroglueDefs.EnsureInitialized();

        Check(UseNeurogluePatches.CanRefillPatched, "CanRefill patch did not install.");
        Check(UseNeurogluePatches.MakeNewToilsPatched, "MakeNewToils patch did not install.");
        Check(UseNeurogluePatches.FloatMenuPatched, "FloatMenu fast-lookup patch did not install.");
        Check(MechBiogelCache.DirtyHookInstalled, "HediffSet.DirtyCache hook did not install.");
        Check(UseNeurogluePatches.UpstreamOutcomeFilterDetected, "Expected upstream IL filter detection to report true on current DMSSR.dll.");

        // 1. User's exact save scenario: Skylark at 87.98/100 fuel (> 75 threshold!) with 9 DegenerativeSyndrome parts,
        //    1 SyntheticRejection, and LV.2 BionicShock.
        Pawn skylark = fx.SpawnColonyMech(new IntVec3(3, 0, 3));
        Hediff_BiogelReservoir reservoir = fx.EnsureReservoirWithFuel(skylark, 87.98f);
        fx.AddDegenerativeToParts(skylark, 9);
        skylark.health.AddHediff(fx.SyntheticRejectionDef);
        Hediff_Level shock = (Hediff_Level)HediffMaker.MakeHediff(fx.BionicShockDef, skylark, fx.Parts[0]);
        shock.level = 2;
        shock.Severity = 2f;
        skylark.health.AddHediff(shock);

        ThingWithComps shelfStack = fx.SpawnNeuroglue(new IntVec3(6, 0, 3), count: 5);

        // JobGiver_AutoUseNeuroglue must immediately issue a job even though Fuel is 87.98 (> 75), because lesions exist!
        JobGiver_AutoUseNeuroglue giver = new();
        MethodInfo tryGiveJob = typeof(JobGiver_AutoUseNeuroglue)
            .GetMethod("TryGiveJob", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Job? autoJob = (Job?)tryGiveJob.Invoke(giver, new object[] { skylark });
        Check(autoJob != null, "Auto JobGiver failed to issue job for diseased Skylark at 87.98 fuel.");
        Check(autoJob!.def == fx.UseNeuroglueJobDef, "Wrong JobDef issued.");
        Check(autoJob.targetA.Thing == skylark, "TargetA must be self.");
        Check(autoJob.targetB.Thing == shelfStack, $"TargetB must be the neuroglue stack, got '{autoJob.targetB.Thing}' vs expected '{shelfStack}'.");
        Check(autoJob.count == 1, $"Job count must be 1, got {autoJob.count}.");

        // Simulate StartCarryThing(1) + final Toil execution:
        Thing taken = fx.TakeOneToCarry(skylark, shelfStack);
        Toil finalToil = Fixture.BuildFinalUseNeuroglueToil(skylark, skylark, taken, out _);
        finalToil.initAction();

        Check(Math.Abs(reservoir.Fuel - 100f) < 0.01f, $"Fuel should clamp to 100, got {reservoir.Fuel:F2}.");
        Check(HasHediff(skylark, fx.NeuroglueHighDef), "NeuroglueHigh was not applied to Skylark.");
        Check(skylark.carryTracker.CarriedThing == null && taken.Destroyed, "Consumed neuroglue unit was not destroyed cleanly.");
        Check(shelfStack.stackCount == 4, $"Shelf stack should have 4 remaining, got {shelfStack.stackCount}.");
        Check(shock.level == 1, $"BionicShock should step down from LV.2 to LV.1, got LV.{shock.level}.");

        // Tick hediffs once: HediffComp_RemoveIfOtherHediff must wipe ALL 9 DegenerativeSyndrome + SyntheticRejection!
        Fixture.TickHediffsOnce(skylark);
        Check(CountHediffs(skylark, fx.DegenerativeDef) == 0, $"All 9 DegenerativeSyndrome lesions must be cured, remaining: {CountHediffs(skylark, fx.DegenerativeDef)}.");
        Check(!HasHediff(skylark, fx.SyntheticRejectionDef), "SyntheticRejection must be cured by NeuroglueHigh.");
        Console.WriteLine("  [Verified] Skylark at 87.98 fuel auto-used 1 Neuroglue -> Fuel=100, NeuroglueHigh=100%, 9/9 DegenerativeSyndrome cured, BionicShock LV.2->LV.1.");

        // Second dose should still trigger because LV.1 BionicShock remains; after LV.1->0 cure, third evaluation must return null!
        Job? secondDoseJob = (Job?)tryGiveJob.Invoke(giver, new object[] { skylark });
        Check(secondDoseJob != null, "Should take a second dose to finish curing LV.1 BionicShock.");
        Thing taken2 = fx.TakeOneToCarry(skylark, shelfStack);
        Fixture.BuildFinalUseNeuroglueToil(skylark, skylark, taken2, out _).initAction();
        Fixture.TickHediffsOnce(skylark);
        Check(!HasHediff(skylark, fx.BionicShockDef), "LV.1 BionicShock should be completely cured on second dose.");

        Job? thirdEval = (Job?)tryGiveJob.Invoke(giver, new object[] { skylark });
        Check(thirdEval == null, "Healthy Skylark at 100 fuel with NeuroglueHigh must NOT waste another neuroglue.");

        // 2. Mech without BiogelReservoir (e.g. DeathCheat proc) + DegenerativeSyndrome: CanRefill + AutoJob + Cure!
        Pawn regularMech = fx.SpawnColonyMech(new IntVec3(4, 0, 4));
        fx.AddDegenerativeToParts(regularMech, 3);
        Check(JobDriver_UseNeuroglue.CanRefill(regularMech), "CanRefill must allow diseased mech even without BiogelReservoir.");
        Job? regJob = (Job?)tryGiveJob.Invoke(giver, new object[] { regularMech });
        Check(regJob != null, "Auto JobGiver must cure diseased mech without BiogelReservoir.");
        Thing taken3 = fx.TakeOneToCarry(regularMech, shelfStack);
        Fixture.BuildFinalUseNeuroglueToil(regularMech, regularMech, taken3, out _).initAction();
        Fixture.TickHediffsOnce(regularMech);
        Check(CountHediffs(regularMech, fx.DegenerativeDef) == 0, "Regular mech's degenerative lesions were not cured.");
        Console.WriteLine("  [Verified] Mech without BiogelReservoir cured 3/3 DeathCheat degenerative lesions.");

        // 3. Preventive threshold boundary: 75.01 -> null, 75.00 -> refills!
        fx.EnsureReservoirWithFuel(skylark, 75.05f);
        Check(tryGiveJob.Invoke(giver, new object[] { skylark }) == null, "Should not auto-refill above 75.0 fuel when healthy.");
        fx.EnsureReservoirWithFuel(skylark, 75.00f);
        Check(tryGiveJob.Invoke(giver, new object[] { skylark }) != null, "Should auto-refill at <= 75.0 fuel.");

        // 4. Safety gates: Drafted, SelfShutdown, Active Combat, Forbidden, Unreachable + 600-tick backoff cooldown.
        typeof(Pawn_DraftController).GetField("draftedInt", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(skylark.drafter, true);
        Check(tryGiveJob.Invoke(giver, new object[] { skylark }) == null, "Drafted mech must never auto-break formation to fetch neuroglue.");
        typeof(Pawn_DraftController).GetField("draftedInt", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(skylark.drafter, false);

        fx.SetControlGroupMode(fx.WorkModeSelfShutdown);
        Check(tryGiveJob.Invoke(giver, new object[] { skylark }) == null, "SelfShutdown work mode must not wake mech.");
        fx.SetControlGroupMode(fx.WorkModeEscort);
        Check(tryGiveJob.Invoke(giver, new object[] { skylark }) != null, "Escort mode out of combat should allow auto-refill.");

        Pawn enemy = fx.SpawnColonist(new IntVec3(10, 0, 10));
        skylark.mindState.enemyTarget = enemy;
        Check(tryGiveJob.Invoke(giver, new object[] { skylark }) == null, "Mech with active enemyTarget must not leave combat.");
        skylark.mindState.enemyTarget = null;
        fx.SetControlGroupMode(fx.WorkModeWork);

        // Closest selection & unreachable/forbidden filtering + 600-tick cooldown:
        fx.DespawnThing(shelfStack);
        ThingWithComps forbiddenClose = fx.SpawnNeuroglue(new IntVec3(3, 0, 4), count: 2, forbidden: true);
        ThingWithComps unreachableClose = fx.SpawnNeuroglue(new IntVec3(15, 0, 15), count: 2, forbidden: false);
        ThingWithComps validFar = fx.SpawnNeuroglue(new IntVec3(12, 0, 12), count: 2, forbidden: false);

        Job? pickValid = (Job?)tryGiveJob.Invoke(giver, new object[] { skylark });
        Check(pickValid?.targetB.Thing == validFar, "Finder failed to skip forbidden/unreachable stacks and pick valid stack.");

        // Remove valid stack -> search fails once, sets 600-tick backoff, next 50 evaluations perform 0 ThingsOfDef queries!
        fx.DespawnThing(validFar);
        NeuroglueFinder.ResetCounters();
        Check(tryGiveJob.Invoke(giver, new object[] { skylark }) == null, "Should return null when only forbidden/unreachable stacks exist.");
        Check(NeuroglueFinder.ThingsOfDefQueries == 1, $"Expected 1 ThingsOfDef query before cooldown, got {NeuroglueFinder.ThingsOfDefQueries}.");

        for (int i = 0; i < 50; i++)
        {
            Check(tryGiveJob.Invoke(giver, new object[] { skylark }) == null, "Cooldown evaluation should return null.");
        }
        Check(NeuroglueFinder.ThingsOfDefQueries == 1, $"Cooldown leaked {NeuroglueFinder.ThingsOfDefQueries - 1} extra queries!");
        fx.DespawnThing(forbiddenClose);
        fx.DespawnThing(unreachableClose);
        Console.WriteLine("  [Verified] Drafted/SelfShutdown/Combat/Forbidden/Unreachable gates + 600-tick zero-query backoff.");

        // 5. FloatMenuOptionProvider_UseNeuroglue fast path verification:
        ThingWithComps menuGel = fx.SpawnNeuroglue(new IntVec3(5, 0, 5), count: 3);
        Pawn colonist = fx.SpawnColonist(new IntVec3(4, 0, 5));
        FloatMenuOptionProvider_UseNeuroglue provider = new();
        FloatMenuContext colonistCtx = MakeFloatContext(colonist, fx.Map);

        Check(
            UseNeurogluePatches.TryResolveFloatMenuTarget(skylark, colonistCtx, out Pawn? resolvedActor, out Pawn? resolvedTarget, out Thing? resolvedGel, out _),
            "Colonist right-clicking needy Skylark should resolve a valid UseNeuroglue target.");
        Check(resolvedActor == colonist && resolvedTarget == skylark && resolvedGel == menuGel, "Colonist feed target/gel mismatch.");
        Check(provider.GetOptionsFor(skylark, colonistCtx).Count() == 0, "GetOptionsFor(Pawn) overload must remain empty to prevent duplicate menu entries.");

        // Colonist right-clicking Neuroglue on ground must return empty from patched GetOptionsFor(Thing, FloatMenuContext) (leaves vanilla Ingest intact):
        Check(!provider.GetOptionsFor((Thing)menuGel, colonistCtx).Any(), "Colonist right-clicking Neuroglue must not get mech UseNeuroglue option.");

        // Skylark right-clicking itself or Neuroglue resolves cleanly:
        FloatMenuContext mechCtx = MakeFloatContext(skylark, fx.Map);
        Check(UseNeurogluePatches.TryResolveFloatMenuTarget(skylark, mechCtx, out _, out resolvedTarget, out resolvedGel, out _)
              && resolvedTarget == skylark && resolvedGel == menuGel,
            "Mech right-clicking self should resolve closest Neuroglue.");
        Check(UseNeurogluePatches.TryResolveFloatMenuTarget(menuGel, mechCtx, out _, out resolvedTarget, out resolvedGel, out _)
              && resolvedTarget == skylark && resolvedGel == menuGel,
            "Mech right-clicking Neuroglue should resolve self + clicked Neuroglue.");
        Console.WriteLine("  [Verified] FloatMenuOptionProvider fast lookup preserves self-use, colonist-feed, and human-ingest boundaries.");

        // 6. Verify XML ThinkTree XPath against real DMS & DMSSR Workshop XML files:
        VerifyThinkTreeXmlPatch(dmsRoot, dmssrRoot, modRoot, patchAssembly);

        // 7. Zero-GC & speed micro-benchmark on 30 ordinary mechs + healthy Skylark:
        VerifyZeroAllocationHotPath(fx, giver, tryGiveJob, skylark);
    }

    private static void VerifyThinkTreeXmlPatch(string dmsRoot, string dmssrRoot, string modRoot, Assembly patchAssembly)
    {
        string patchXmlPath = Path.Combine(modRoot, @"1.7\Patches\DMSSR_AutoUseNeuroglue_ThinkTree.xml");
        XmlDocument patchDoc = new();
        patchDoc.Load(patchXmlPath);
        string xpath = patchDoc.SelectSingleNode("//match/xpath")!.InnerText;
        string className = patchDoc.SelectSingleNode("//match/value/li/@Class")!.Value!;

        Type? resolvedType = patchAssembly.GetType(className, throwOnError: false);
        Check(resolvedType == typeof(JobGiver_AutoUseNeuroglue), $"ThinkTree li Class '{className}' did not resolve to JobGiver_AutoUseNeuroglue.");

        (string Label, string FilePath, int ExpectedMatches)[] targets =
        {
            ("DMS_HumanlikeMech (Skylark/Hirsch/Scourge/Facedancer/Sensor/Auntie)", Path.Combine(dmsRoot, @"1.7\Defs\Misc\ThinkTree_Lady.xml"), 1),
            ("DMS_Mechanoid", Path.Combine(dmsRoot, @"1.7\Defs\Misc\ThinkTree_Mech.xml"), 1),
            ("DMSSR_Cephalopoda", Path.Combine(dmssrRoot, @"1.7\Defs\Misc\DMSSR_ThinkTreeDefs.xml"), 1),
            ("DMSSR_Hydra", Path.Combine(dmssrRoot, @"1.7\Defs\Misc\DMSSR_HydraThinkTree.xml"), 1),
        };

        foreach ((string label, string filePath, int expected) in targets)
        {
            XmlDocument doc = new();
            doc.Load(filePath);
            XmlNodeList? nodes = doc.SelectNodes(xpath);
            int count = nodes?.Count ?? 0;
            Check(count == expected, $"XPath matched {count} nodes in {label}, expected {expected}.");
        }

        Console.WriteLine("  [Verified] ThinkTree XPath cleanly matches DMS_HumanlikeMech, DMS_Mechanoid, DMSSR_Cephalopoda, and DMSSR_Hydra.");
    }

    private static void VerifyZeroAllocationHotPath(Fixture fx, JobGiver_AutoUseNeuroglue giver, MethodInfo tryGiveJob, Pawn healthySkylark)
    {
        Func<JobGiver_AutoUseNeuroglue, Pawn, Job?> fastInvoke =
            (Func<JobGiver_AutoUseNeuroglue, Pawn, Job?>)tryGiveJob.CreateDelegate(typeof(Func<JobGiver_AutoUseNeuroglue, Pawn, Job?>));

        Pawn ordinaryMech = fx.SpawnColonyMech(new IntVec3(8, 0, 8));
        for (int i = 0; i < 25; i++)
        {
            Hediff noise = new() { def = new HediffDef { defName = "Noise_" + i, hediffClass = typeof(Hediff) }, pawn = ordinaryMech };
            ordinaryMech.health.hediffSet.hediffs.Add(noise);
        }
        ordinaryMech.health.hediffSet.DirtyCache();
        fx.EnsureReservoirWithFuel(healthySkylark, 95f);

        // Warmup JIT across full Tier-1 threshold (10,000+ calls in .NET 10) & populate cache once:
        for (int w = 0; w < 20_000; w++)
        {
            _ = fastInvoke(giver, ordinaryMech);
            _ = fastInvoke(giver, healthySkylark);
        }
        int rebuildsBefore = MechBiogelCache.RebuildCount;

        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
        {
            _ = fastInvoke(giver, ordinaryMech);
            _ = fastInvoke(giver, healthySkylark);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        Stopwatch sw = Stopwatch.StartNew();
        for (int i = 0; i < 100_000; i++)
        {
            _ = fastInvoke(giver, ordinaryMech);
            _ = fastInvoke(giver, healthySkylark);
        }
        sw.Stop();

        Check(MechBiogelCache.RebuildCount == rebuildsBefore, "Cache unexpectedly rebuilt during 200,000 steady-state evaluations.");
        Check(allocated == 0, $"Steady-state hot path allocated {allocated} bytes across 200,000 calls (expected 0 B)!");
        Console.WriteLine($"  [Perf] 200,000 ThinkTree evaluations (ordinary 25-hediff mech + healthy Skylark): {sw.Elapsed.TotalMilliseconds:F2} ms total ({sw.Elapsed.TotalNanoseconds / 200_000.0:F1} ns/call), {allocated} B GC, 0 cache rebuilds.");
    }

    private static FloatMenuContext MakeFloatContext(Pawn actor, Map map)
    {
        FloatMenuContext ctx = (FloatMenuContext)RuntimeHelpers.GetUninitializedObject(typeof(FloatMenuContext));
        ctx.allSelectedPawns = new List<Pawn> { actor };
        ctx.map = map;
        return ctx;
    }

    private static bool HasHediff(Pawn pawn, HediffDef def)
    {
        return pawn.health.hediffSet.GetFirstHediffOfDef(def) != null;
    }

    private static int CountHediffs(Pawn pawn, HediffDef def)
    {
        int count = 0;
        List<Hediff> list = pawn.health.hediffSet.hediffs;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].def == def)
            {
                count++;
            }
        }
        return count;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("ASSERTION FAILED: " + message);
        }
        assertions++;
    }
}
