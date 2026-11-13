using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using DMSSR.Hediffs;
using DMSSR.Patches;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using WhiteGiver.DMSSRStatPathPerfFix;

namespace DMSSRStatPathPerfFix.Validation;

internal static class Tests
{
    private static readonly Func<Pawn_PathFollower, IntVec3, float> PathCost =
        (Func<Pawn_PathFollower, IntVec3, float>)AccessTools
            .Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(IntVec3) })!
            .CreateDelegate(typeof(Func<Pawn_PathFollower, IntVec3, float>));

    private static readonly Func<StatWorker, StatRequest, bool, float> StatValue =
        (Func<StatWorker, StatRequest, bool, float>)AccessTools
            .Method(typeof(StatWorker), "GetValueUnfinalized", new[] { typeof(StatRequest), typeof(bool) })!
            .CreateDelegate(typeof(Func<StatWorker, StatRequest, bool, float>));

    private static readonly MethodInfo TryGetCompFast =
        AccessTools.Method(
            typeof(Patch_MechCustomization_ApplyStatPreFinalize),
            "TryGetCompFast",
            new[] { typeof(Pawn), typeof(HediffComp_MechCustomization).MakeByRefType() })!;

    private static readonly MethodInfo UpstreamStatPostfix =
        AccessTools.Method(typeof(Patch_MechCustomization_ApplyStatPreFinalize), "Postfix")!;

    private static int checks;

    private static Fixture fixture = null!;

    private static readonly IntVec3 Start = new(2, 0, 2);
    private static readonly IntVec3 Target = new(3, 0, 2);

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

    // ------------------------------------------------------------------ RED

    private static void RunRed()
    {
        Section("RED - upstream DMSSR behaviour before the fix");
        Fixture fx = Fixture.Create();
        fixture = fx;
        Probe.Install();
        UpstreamPatches.Apply();

        Check(!AnyOwnerPatches(), "RED loaded a patch owned by the performance mod.");
        Check(HasPatch(UpstreamPatches.InstanceMethod, prefix: false), "DMSSR's instance CostToMoveIntoCell postfix is missing.");
        Check(HasPatch(UpstreamPatches.StaticMethod, prefix: false), "DMSSR's static CostToMoveIntoCell postfix is missing.");
        Check(HasPatch(UpstreamPatches.StatMethod, prefix: false), "DMSSR's StatWorker postfix is missing.");
        Console.WriteLine("       upstream patches: both CostToMoveIntoCell overloads + StatWorker.GetValueUnfinalized.");

        MethodInfo delegates = AccessTools.Method(typeof(PathCostPatches), "InstanceOverloadDelegatesToStatic")!;
        Check((bool)delegates.Invoke(null, null)!, "vanilla CoostToMoveIntoCell(IntVec3) does not delegate to the static overload.");
        Console.WriteLine("       vanilla CostToMoveIntoCell(IntVec3) is a pure delegation to CostToMoveIntoCell(Pawn, IntVec3).");

        // ---- bottleneck 1: the clamp runs twice per step and scans every hediff each time
        Pawn plain = fx.NewPawn(fx.HumanDef, Start);
        fx.AddNoiseHediffs(plain, 30);

        Probe.Reset();
        float plainCost = PathCost(plain.pather, Target);
        Check(Probe.ClampCalls == 2, $"upstream ran ClampPathCostForAbility {Probe.ClampCalls} times per pather.CostToMoveIntoCell(c), expected 2.");
        Check(Probe.HasIgnoreTerrainPenaltyCalls == 2, $"upstream ran the hediff scan {Probe.HasIgnoreTerrainPenaltyCalls} times per path step, expected 2.");
        Check(plain.health.hediffSet.hediffs.Count == 30, "the fixture pawn does not have 30 hediffs.");
        Check(plainCost.Equals(Fixture.RawCellCost), $"upstream path cost {plainCost} != vanilla raw cost {Fixture.RawCellCost} for a pawn without the flag.");
        Console.WriteLine($"       30-hediff pawn: 2 clamps x 30 hediffs = 60 hediff visits per path step (was {plainCost}).");

        // ---- the double subtraction: only visible when vanilla cost > moveTicks + terrain
        Pawn flagged = fx.NewPawn(fx.HumanDef, Start);
        fx.AddNoiseHediffs(flagged, 29);
        fx.AddIgnoreCompHediff(flagged);

        Probe.Reset();
        float flaggedCost = PathCost(flagged.pather, Target);
        Check(Probe.HasIgnoreTerrainPenaltyCalls == 2, "upstream did not scan the flagged pawn twice.");

        float single = Clamp(flagged, Fixture.RawCellCost);
        float doubled = Clamp(flagged, single);
        Check(single.Equals(160f), $"single clamp should be 160, got {single}.");
        Check(doubled.Equals(120f), $"double clamp should be 120, got {doubled}.");
        Check(flaggedCost.Equals(doubled), $"upstream path cost {flaggedCost} != double-clamped {doubled}.");
        Check(!doubled.Equals(single), "the double subtraction is not observable in this fixture.");
        Console.WriteLine($"       flagged pawn: upstream returns {flaggedCost} (terrain penalty subtracted twice); one clamp gives {single}.");
        Console.WriteLine($"       => this is also a behaviour bug: 160 ticks intended, {flaggedCost} ticks delivered.");

        // ---- bottleneck 2: every stat query on every mechanoid walks the hediff list first
        (Pawn mech, HediffComp_MechCustomization comp) = NewMechWithCustomization(fx, out Pawn mechControl);

        Probe.Reset();
        float armour = Stat(mech, "ArmorRating_Sharp");
        Check(Probe.MechCustomizationLookupCalls == 1, $"upstream ran {Probe.MechCustomizationLookupCalls} mech-customization hediff lookups for an unrelated stat, expected 1.");
        Check(Probe.MechCustomizationLookupScanLength == 30, $"upstream's lookup scanned {Probe.MechCustomizationLookupScanLength} hediffs, expected 30.");
        Check(armour.Equals(0.5f), $"ArmorRating_Sharp was modified: {armour}.");

        Probe.Reset();
        float market = Stat(mech, "MarketValue");
        Check(Probe.MechCustomizationLookupCalls == 1, "MarketValue did not pay the hediff scan.");
        Check(market.Equals(1000f), $"MarketValue was modified: {market}.");

        Probe.Reset();
        float move = Stat(mech, "MoveSpeed");
        Check(Probe.MechCustomizationLookupCalls == 1, "the relevant stat did not do the upstream lookup.");
        Check(move.Equals(2f), $"MoveSpeed multiplier not applied by upstream: {move}.");
        Console.WriteLine("       unrelated stat query: 1 x 30-hediff scan; relevant stat: 1 x 30-hediff scan + multiplier.");

        Probe.Remove();
        ReportPerfMatrix(plain, mech);
        ReportHediffScaling(fx);
        _ = comp;
        _ = mechControl;
    }

    // ---------------------------------------------------------------- GREEN

    private static void RunGreen(Type modType)
    {
        Section("GREEN - applying DMSSRStatPathPerfFix");
        Fixture fx = Fixture.Create();
        fixture = fx;
        Probe.Install();
        UpstreamPatches.Apply();

        object mod = Activator.CreateInstance(modType, new object?[] { null })!;
        Check(mod != null, "Mod constructor returned null.");

        Console.WriteLine(
            $"       mod state: singleClamp={PathCostPatches.SingleClampInstalled}"
            + $" cacheOnly={PathCostPatches.CacheOnlyFallbackInstalled}"
            + $" delegation={PathCostPatches.DelegationVerified}"
            + $" statGate={MechStatPatch.GateInstalled}"
            + $" compCache={MechStatPatch.CompCacheInstalled}"
            + $" dirtyCache={HediffCachePatches.Installed}");
        foreach (MethodBase patched in OwnedMethods())
        {
            Patches? info = Harmony.GetPatchInfo(patched);
            Console.WriteLine(
                $"       patched {Describe(patched)}: prefixes=[{string.Join(", ", info!.Prefixes.Select(p => p.PatchMethod.Name + "@" + p.owner))}]"
                + $" postfixes=[{string.Join(", ", info.Postfixes.Select(p => p.PatchMethod.Name + "@" + p.owner))}]");
        }

        // ---- patch wiring
        Check(PathCostPatches.DelegationVerified, "the instance CostToMoveIntoCell overload no longer delegates to the static one.");
        Check(PathCostPatches.SingleClampInstalled, "the single-clamp path was not installed.");
        Check(!PathCostPatches.CacheOnlyFallbackInstalled, "the mod fell back to cache-only mode.");
        Check(!HasPatch(UpstreamPatches.InstanceMethod, prefix: false), "DMSSR's duplicate instance postfix is still installed.");
        Check(HasPatch(UpstreamPatches.StaticMethod, prefix: false), "DMSSR's static postfix was removed by accident.");
        Check(HasPatch(UpstreamPatches.StatMethod, prefix: false), "DMSSR's StatWorker postfix should still be installed (it is gated, not replaced).");
        Check(HasOwnerPatch(AccessTools.Method(typeof(AbilityPathCostUtility), "ClampPathCostForAbility")!, prefix: true), "the clamp prefix is missing.");
        Check(HasOwnerPatch(AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache), Type.EmptyTypes)!, prefix: false), "the DirtyCache postfix is missing.");
        Check(HasOwnerPatch(UpstreamPatches.StatMethod.Postfix, prefix: true), "the stat gate prefix is missing.");
        Check(MechStatPatch.GateInstalled, "the stat gate was not installed.");
        Check(MechStatPatch.CompCacheInstalled, "the mech customization lookup cache was not installed.");
        Check(HediffCachePatches.Installed, "the hediff cache invalidation hook was not installed.");

        HashSet<MethodBase> owned = OwnedMethods();
        Check(owned.Count == 4, $"the mod patched {owned.Count} methods instead of 4: {string.Join(", ", owned.Select(Describe))}");
        Console.WriteLine("       patch wiring: 4 targets (clamp prefix, stat gate, mech lookup cache, DirtyCache postfix).");

        // ---- the stat set is read from DMSSR itself, not duplicated
        MechStatSetMayAffectProbe(fx);

        // ---- bottleneck 1: one clamp, zero upstream scans, cached flag
        Pawn plain = fx.NewPawn(fx.HumanDef, Start);
        fx.AddNoiseHediffs(plain, 30);

        HediffFlagCache.ResetForValidation();
        Probe.Reset();
        float plainCost = PathCost(plain.pather, Target);
        Check(Probe.ClampCalls == 1, $"the clamp ran {Probe.ClampCalls} times per path step, expected exactly 1.");
        Check(Probe.HasIgnoreTerrainPenaltyCalls == 0, "upstream's hediff scan still ran on the fixed path.");
        Check(HediffFlagCache.ScanCount == 1, $"the cache scanned {HediffFlagCache.ScanCount} times for one pawn, expected 1.");
        Check(plainCost.Equals(Fixture.RawCellCost), $"path cost changed for an unflagged pawn: {plainCost} != {Fixture.RawCellCost}.");
        Console.WriteLine($"       unflagged 30-hediff pawn: 1 clamp, 0 hediff visits, cost {plainCost} (identical to upstream).");

        Probe.Reset();
        for (int i = 0; i < 10_000; i++)
        {
            _ = PathCost(plain.pather, Target);
        }

        Check(HediffFlagCache.ScanCount == 1, $"the pawn cache re-scanned {HediffFlagCache.ScanCount} times over 10k path steps.");
        Check(Probe.HasIgnoreTerrainPenaltyCalls == 0, "upstream's scan ran during the 10k-step loop.");
        Check(Probe.ClampCalls == 10_000, $"the clamp ran {Probe.ClampCalls} times over 10k steps, expected 10k.");
        Console.WriteLine("       10,000 further path steps: 0 additional hediff scans.");

        // ---- flagged pawn: single subtraction, exactly the intended value
        Pawn flagged = fx.NewPawn(fx.HumanDef, Start);
        fx.AddNoiseHediffs(flagged, 29);
        fx.AddIgnoreCompHediff(flagged);
        float flaggedCost = PathCost(flagged.pather, Target);
        Check(flaggedCost.Equals(160f), $"flagged pawn path cost should be the single-clamp 160, got {flaggedCost}.");
        Console.WriteLine($"       flagged pawn: {flaggedCost} ticks (single clamp; upstream delivered 120).");

        // extension-based flag
        Pawn ext = fx.NewPawn(fx.HumanDef, Start);
        fx.AddNoiseHediffs(ext, 29);
        fx.AddIgnoreExtensionHediff(ext);
        Check(PathCost(ext.pather, Target).Equals(160f), "the DefModExtension flag is not honoured.");

        // a plain Hediff whose def lists the comp must NOT count (upstream cannot see it either)
        Pawn plainHediff = fx.NewPawn(fx.HumanDef, Start);
        fx.AddNoiseHediffs(plainHediff, 29);
        fx.AddPlainIgnoreCompHediff(plainHediff);
        Check(!HediffFlagCache.HasIgnoreTerrainMovePenalty(plainHediff), "a plain Hediff was treated as if it carried the comp.");
        Check(PathCost(plainHediff.pather, Target).Equals(Fixture.RawCellCost), "a plain Hediff changed the path cost.");
        Console.WriteLine("       extension flag and comp flag both honoured; plain Hediff with compClass-only def correctly ignored.");

        // ---- cache invalidation on hediff add/remove
        Pawn dynamic = fx.NewPawn(fx.HumanDef, Start);
        fx.AddNoiseHediffs(dynamic, 5);
        Check(PathCost(dynamic.pather, Target).Equals(Fixture.RawCellCost), "dynamic pawn starts flagged.");

        HediffWithComps added = fx.AddIgnoreCompHediff(dynamic);
        float expectedFlagged = Clamp(dynamic, Fixture.RawCellCost);
        float actualFlagged = PathCost(dynamic.pather, Target);
        Check(actualFlagged.Equals(expectedFlagged), $"adding the hediff did not invalidate the cache: {actualFlagged} != {expectedFlagged}.");
        Check(!actualFlagged.Equals(Fixture.RawCellCost), "the clamp did not engage after adding the hediff.");

        fx.RemoveHediff(dynamic, added);
        Check(PathCost(dynamic.pather, Target).Equals(Fixture.RawCellCost), "removing the hediff did not invalidate the cache.");

        // a raw list mutation (no DirtyCache) must still be caught by the count check
        HediffWithComps sneaky = new() { def = fx.IgnoreCompDef, pawn = dynamic, comps = new List<HediffComp> { new HediffComp_IgnoreTerrainMovePenalty() } };
        dynamic.health.hediffSet.hediffs.Add(sneaky);
        Check(HediffFlagCache.HasIgnoreTerrainMovePenalty(dynamic), "a hediff added without DirtyCache was not observed.");
        dynamic.health.hediffSet.hediffs.Remove(sneaky);
        Console.WriteLine("       invalidation: DirtyCache add/remove and direct list mutation all observed.");

        // ---- bottleneck 2: O(1) skip for unrelated stats, cached lookup for relevant ones
        (Pawn mech, HediffComp_MechCustomization comp) = NewMechWithCustomization(fx, out Pawn mechControl);

        HediffFlagCache.ResetForValidation();
        Probe.Reset();
        float armour = Stat(mech, "ArmorRating_Sharp");
        Check(Probe.MechCustomizationLookupCalls == 0, $"an unrelated stat still ran {Probe.MechCustomizationLookupCalls} hediff lookups.");
        Check(HediffFlagCache.ScanCount == 0, $"an unrelated stat triggered {HediffFlagCache.ScanCount} hediff scans.");
        Check(armour.Equals(0.5f), $"ArmorRating_Sharp was modified: {armour}.");

        Probe.Reset();
        float market = Stat(mech, "MarketValue");
        Check(Probe.MechCustomizationLookupCalls == 0, "MarketValue still paid the hediff lookup.");
        Check(HediffFlagCache.ScanCount == 0, "MarketValue triggered a hediff scan.");
        Check(market.Equals(1000f), $"MarketValue was modified: {market}.");

        Probe.Reset();
        float move = Stat(mech, "MoveSpeed");
        Check(Probe.MechCustomizationLookupCalls == 0, $"the relevant stat still used upstream's linear scan ({Probe.MechCustomizationLookupCalls} lookups).");
        Check(HediffFlagCache.ScanCount == 1, $"the cache filled {HediffFlagCache.ScanCount} times for the first relevant-stat query, expected 1.");
        Check(move.Equals(2f), $"MoveSpeed multiplier not applied: {move}.");

        Probe.Reset();
        for (int i = 0; i < 10_000; i++)
        {
            _ = Stat(mech, "MoveSpeed");
        }

        Check(Probe.MechCustomizationLookupCalls == 0, $"{Probe.MechCustomizationLookupCalls} hediff lookups during 10k cached relevant-stat queries.");
        Check(HediffFlagCache.ScanCount == 1, $"{HediffFlagCache.ScanCount} hediff scans during 10k relevant-stat queries.");
        Console.WriteLine("       unrelated stat: 0 lookups, 0 scans; first relevant query: 1 scan; next 10,000: 0 scans.");

        // ---- equivalence matrix against DMSSR's own postfix
        Section("GREEN - 100% equivalence matrix");
        Pawn mechNoComp = fx.NewPawn(fx.MechDef, Start);
        fx.AddNoiseHediffs(mechNoComp, 30);
        Pawn humanWithComp = fx.NewPawn(fx.HumanDef, Start);
        fx.AddNoiseHediffs(humanWithComp, 29);
        fx.AddMechCustomization(humanWithComp);

        int compared = 0;
        foreach (string statName in Fixture.StatMap.Select(entry => entry.Stat).Concat(Fixture.UnrelatedStats))
        {
            float rawMechControl = Stat(mechControl, statName);
            float rawNoComp = Stat(mechNoComp, statName);
            Check(rawMechControl.Equals(rawNoComp), $"{statName}: the control mech and the no-comp mech disagree before postfixes.");

            float expectedWithComp = UpstreamReference(fx.Worker(statName), StatRequest.For(mech), rawMechControl);
            float actualWithComp = Stat(mech, statName);
            Check(actualWithComp.Equals(expectedWithComp), $"{statName} (mech + customization): {actualWithComp} != upstream {expectedWithComp}.");

            float expectedNoComp = UpstreamReference(fx.Worker(statName), StatRequest.For(mechNoComp), rawMechControl);
            float actualNoComp = Stat(mechNoComp, statName);
            Check(actualNoComp.Equals(expectedNoComp), $"{statName} (mech, no customization): {actualNoComp} != upstream {expectedNoComp}.");
            Check(actualNoComp.Equals(rawMechControl), $"{statName} (mech, no customization) was modified without a comp.");

            float expectedHuman = UpstreamReference(fx.Worker(statName), StatRequest.For(humanWithComp), Stat(mechControl, statName));
            float actualHuman = Stat(humanWithComp, statName);
            Check(actualHuman.Equals(expectedHuman), $"{statName} (non-mech + customization): {actualHuman} != upstream {expectedHuman}.");

            compared += 3;
        }

        Console.WriteLine($"       {compared} value comparisons over {Fixture.StatMap.Length + Fixture.UnrelatedStats.Length} stats x 3 pawn shapes: all bit-identical to DMSSR's own postfix.");

        // the customization multiplier really is distinct per stat (guards against a gate that skips too much)
        foreach ((string statName, string compField) in Fixture.StatMap)
        {
            float multiplier = (float)typeof(HediffComp_MechCustomization).GetField(compField)!.GetValue(comp)!;
            float value = Stat(mech, statName);
            Check(value.Equals(multiplier), $"{statName} should be raw(1) x {compField}({multiplier}), got {value}.");
        }

        Console.WriteLine("       all ten customization stats receive their own multiplier; unrelated stats receive none.");

        // ---- lookup cache agrees with the vanilla scan, including invalidation
        Pawn lookupPawn = fx.NewPawn(fx.MechDef, Start);
        fx.AddNoiseHediffs(lookupPawn, 10);
        Check(TryGetComp(lookupPawn) == null, "the cached lookup found a comp on a pawn without one.");
        HediffComp_MechCustomization addedComp = fx.AddMechCustomization(lookupPawn);
        Check(ReferenceEquals(TryGetComp(lookupPawn), addedComp), "the cached lookup did not return the real comp.");
        fx.RemoveHediff(lookupPawn, addedComp.parent);
        Check(TryGetComp(lookupPawn) == null, "the cached lookup still returns a removed comp.");
        Console.WriteLine("       mech customization lookup: null -> comp -> null across DirtyCache invalidations.");

        // ---- other pawn types and edge cases are untouched
        Pawn animal = fx.NewPawn(fx.HumanDef, Start);
        fx.AddNoiseHediffs(animal, 3);
        Check(PathCost(animal.pather, Target).Equals(Fixture.RawCellCost), "a pawn with a small hediff list changed cost.");
        Check(Stat(animal, "MoveSpeed").Equals(1f), "a non-mech stat was modified.");

        Probe.Remove();
        ReportPerfMatrix(plain, mech);
        ReportHediffScaling(fx);
    }

    // ------------------------------------------------------------- helpers

    private static (Pawn Pawn, HediffComp_MechCustomization Comp) NewMechWithCustomization(Fixture fx, out Pawn control)
    {
        Pawn mech = fx.NewPawn(fx.MechDef, Start);
        fx.AddNoiseHediffs(mech, 29);
        HediffComp_MechCustomization comp = fx.AddMechCustomization(mech);
        comp.moveSpeedMult = 2f;
        comp.shootAccMult = 1.5f;
        comp.meleeDodgeMult = 0.5f;
        comp.aimTimeMult = 0.9f;
        comp.meleeCooldownMult = 1.1f;
        comp.meleeDamageMult = 1.25f;
        comp.energyUseMult = 0.75f;
        comp.wastepacksMult = 0.5f;
        comp.bandwidthMult = 3f;

        control = fx.NewPawn(fx.MechDef, Start);
        fx.AddNoiseHediffs(control, 30);
        return (mech, comp);
    }

    private static HediffComp_MechCustomization? TryGetComp(Pawn pawn)
    {
        Hediff? hediff = pawn.health.hediffSet.GetFirstHediffOfDef(DMS_HediffDefOf.DMS_MechCustomization);
        return hediff == null ? null : HediffUtility.TryGetComp<HediffComp_MechCustomization>(hediff);
    }

    private static float Stat(Pawn pawn, string statName)
    {
        StatWorker worker = fixture.Worker(statName);
        return StatValue(worker, StatRequest.For(pawn), false);
    }

    private static float UpstreamReference(StatWorker worker, StatRequest req, float rawValue)
    {
        object?[] args = { worker, req, false, rawValue };
        UpstreamStatPostfix.Invoke(null, args);
        return (float)args[3]!;
    }

    private static float Clamp(Pawn pawn, float cost)
    {
        AbilityPathCostUtility.ClampPathCostForAbility(pawn, Tests.Target, ref cost);
        return cost;
    }

    /// <summary>
    /// Same-process A/B/C: vanilla, DMSSR only, DMSSR + this fix. The three states are measured in
    /// interleaved rounds (A,B,C,A,B,C,A,B,C) and the best round per state is reported, so machine
    /// drift (this runs next to a live RimWorld) cannot favour one state.
    /// </summary>
    private static void ReportPerfMatrix(Pawn plain, Pawn mech)
    {
        const int iterations = 500_000;
        string[] names = { "vanilla", "upstream", "fixed" };
        double[] pathBest = { double.MaxValue, double.MaxValue, double.MaxValue };
        double[] statBest = { double.MaxValue, double.MaxValue, double.MaxValue };
        Harmony modHarmony = new(Program.Owner);
        StatWorker worker = fixture.Worker("ArmorRating_Sharp");
        StatRequest request = StatRequest.For(mech);

        for (int round = 0; round < 3; round++)
        {
            for (int state = 0; state < names.Length; state++)
            {
                SetPerfState(state, modHarmony);
                for (int i = 0; i < 2000; i++)
                {
                    _ = PathCost(plain.pather, Target);
                    _ = StatValue(worker, request, false);
                }

                pathBest[state] = Math.Min(pathBest[state], MeasurePath(plain, iterations));
                statBest[state] = Math.Min(statBest[state], MeasureStat(worker, request, iterations));
            }
        }

        SetPerfState(2, modHarmony);
        for (int i = 0; i < names.Length; i++)
        {
            Console.WriteLine(
                $"       [perf] {names[i],-8} path step {pathBest[i] / iterations * 1000.0:F3} us"
                + $" | unrelated stat {statBest[i] / iterations * 1000.0:F3} us");
        }
    }

    /// <summary>Same measurement on a hediff-heavy pawn: the upstream scan is O(hediffs), the fix is not.</summary>
    private static void ReportHediffScaling(Fixture fx)
    {
        const int iterations = 200_000;
        Pawn heavy = fx.NewPawn(fx.HumanDef, Start);
        for (int i = 0; i < 300; i++)
        {
            // Added directly (no DirtyCache) because the validation DirtyCache prefix has already been
            // removed at this point; the cache's hediff-count guard picks the new list up.
            heavy.health.hediffSet.hediffs.Add(new HediffWithComps
            {
                def = fx.NoiseDef,
                pawn = heavy,
                comps = new List<HediffComp>(),
            });
        }
        Harmony modHarmony = new(Program.Owner);
        double upstream = double.MaxValue;
        double fixedCost = double.MaxValue;

        for (int round = 0; round < 3; round++)
        {
            SetPerfState(1, modHarmony);
            for (int i = 0; i < 500; i++)
            {
                _ = PathCost(heavy.pather, Target);
            }

            upstream = Math.Min(upstream, MeasurePath(heavy, iterations));

            SetPerfState(2, modHarmony);
            for (int i = 0; i < 500; i++)
            {
                _ = PathCost(heavy.pather, Target);
            }

            fixedCost = Math.Min(fixedCost, MeasurePath(heavy, iterations));
        }

        SetPerfState(2, modHarmony);
        Console.WriteLine(
            $"       [perf] 300-hediff pawn: upstream {upstream / iterations * 1000.0:F3} us/step"
            + $" vs fixed {fixedCost / iterations * 1000.0:F3} us/step ({upstream / Math.Max(fixedCost, 0.001):F1}x).");
    }

    private static void SetPerfState(int state, Harmony modHarmony)
    {
        modHarmony.UnpatchAll(Program.Owner);
        if (state == 0)
        {
            UpstreamPatches.Remove();
            return;
        }

        UpstreamPatches.Apply();
        if (state == 1)
        {
            return;
        }

        HediffCachePatches.Apply(modHarmony);
        PathCostPatches.Apply(modHarmony);
        MechStatPatch.Apply(modHarmony);
    }

    private static double MeasurePath(Pawn pawn, int iterations)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        float sink = 0f;
        for (int i = 0; i < iterations; i++)
        {
            sink += PathCost(pawn.pather, Target);
        }

        stopwatch.Stop();
        GC.KeepAlive(sink);
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static double MeasureStat(StatWorker worker, StatRequest request, int iterations)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        float sink = 0f;
        for (int i = 0; i < iterations; i++)
        {
            sink += StatValue(worker, request, false);
        }

        stopwatch.Stop();
        GC.KeepAlive(sink);
        return stopwatch.Elapsed.TotalMilliseconds;
    }

    private static void MechStatSetMayAffectProbe(Fixture fx)
    {
        // The set is resolved lazily on the first stat query, once the game's DefOfs are bound.
        _ = MechStatSet.MayAffect(fx.Stats["MoveSpeed"]);
        Check(MechStatSet.Ready, "the customization stat set was not resolved.");
        Check(MechStatSet.FromUpstream, "the customization stat set was not read from DMSSR.");
        int expected = Fixture.StatMap.Length;
        Check(MechStatSet.Count == expected, $"DMSSR exposes {MechStatSet.Count} customization stats, the fixture expects {expected}.");
        foreach ((string statName, string _) in Fixture.StatMap)
        {
            Check(MechStatSet.MayAffect(fx.Stats[statName]), $"{statName} is missing from the customization stat set.");
        }

        foreach (string statName in Fixture.UnrelatedStats)
        {
            Check(!MechStatSet.MayAffect(fx.Stats[statName]), $"{statName} should not be in the customization stat set.");
        }

        _ = fx;
    }

    private static bool HasPatch(UpstreamPatches.PatchTarget target, bool prefix)
    {
        return HasPatch(target.Method, target.Postfix, prefix);
    }

    private static bool HasPatch(MethodBase method, MethodInfo patchMethod, bool prefix)
    {
        Patches? info = Harmony.GetPatchInfo(method);
        if (info == null)
        {
            return false;
        }

        IEnumerable<Patch> patches = prefix ? info.Prefixes : info.Postfixes;
        return patches.Any(patch => patch.PatchMethod == patchMethod);
    }

    private static bool HasOwnerPatch(MethodBase method, bool prefix, MethodInfo? patchMethod = null)
    {
        Patches? info = Harmony.GetPatchInfo(method);
        if (info == null)
        {
            return false;
        }

        IEnumerable<Patch> patches = prefix ? info.Prefixes : info.Postfixes;
        foreach (Patch patch in patches)
        {
            if (patch.owner != Program.Owner)
            {
                continue;
            }

            if (patchMethod == null || patch.PatchMethod == patchMethod)
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<MethodBase> OwnedMethods()
    {
        return Harmony.GetAllPatchedMethods()
            .Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Program.Owner) == true)
            .ToHashSet();
    }

    private static bool AnyOwnerPatches()
    {
        return OwnedMethods().Count > 0;
    }

    private static string Describe(MethodBase method)
    {
        return method.DeclaringType?.Name + "." + method.Name;
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
