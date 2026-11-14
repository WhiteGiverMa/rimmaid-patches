using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using DMSSR.Hediffs;
using DMSSR.Patches;
using HarmonyLib;
using RimWorld;
using Verse;
using WhiteGiver.DMSSRStatPathPerfFix;

namespace DMSSRStatPathPerfFix.Validation;

internal static class HotspotTests
{
    private static int checks;
    internal static int CheckCount => checks;
    private static readonly Type[] Helpers =
    {
        typeof(Patch_MechCustomization_ApplyCapacity), typeof(Patch_MechCustomization_BodySize),
        typeof(Patch_MechCustomization_MechEnergyMax), typeof(Patch_MechCustomization_MechEnergyThresholdMin),
        typeof(Patch_MechCustomization_MechEnergyThresholdMax),
    };

    internal static void Run(Fixture fx, bool fixedMode)
    {
        Console.WriteLine($"== {(fixedMode ? "GREEN" : "RED")} 1.1.0 health hotspots ==");
        Pawn mech = fx.NewPawn(fx.MechDef, new IntVec3(2, 0, 2));
        fx.AddNoiseHediffs(mech, 29);
        HediffComp_MechCustomization comp = fx.AddMechCustomization(mech);
        comp.consciousnessMult = 1.5f;
        comp.manipulationMult = 2f;
        comp.moveSpeedMult = 0.8f;
        comp.bodySizeOffset = 0.4f;
        comp.supply = 2.5f;
        comp.protocolSkillBonusLevels = 4;
        HediffFlagCache.ResetForValidation();
        Probe.Reset();
        foreach (Type type in Helpers)
        {
            object?[] args = { mech, null };
            Check((bool)AccessTools.Method(type, "TryGetCompFast").Invoke(null, args)!, type.Name + " lookup found no comp");
            Check(ReferenceEquals(args[1], comp), type.Name + " returned the wrong instance");
        }
        Check(Probe.MechCustomizationLookupCalls == (fixedMode ? 0 : Helpers.Length), "duplicated mech helper scan count");

        foreach (string name in fx.Capacities.Keys)
        {
            Probe.Reset();
            object?[] args = { mech.health.hediffSet, fx.Capacities[name], 0.8f };
            AccessTools.Method(typeof(Patch_MechCustomization_ApplyCapacity), "Postfix").Invoke(null, args);
            float multiplier = name switch { "Consciousness" => 1.5f, "Manipulation" => 2f, "Moving" => 0.8f, _ => 1f };
            Check(((float)args[2]!).Equals(0.8f * multiplier), "capacity multiplier " + name);
            Check(Probe.MechCustomizationLookupCalls == (fixedMode ? 0 : 1), "capacity scan " + name);
        }
        foreach (Type type in new[] { typeof(Patch_MechCustomization_SkillBonus_GetLevel), typeof(Patch_MechCustomization_SkillBonus_GetLevelForUI) })
        {
            foreach (int level in new[] { 0, 12, 19, 20 })
            {
                Probe.Reset();
                object?[] args = { mech, level };
                AccessTools.Method(type, "ApplyBonus").Invoke(null, args);
                Check((int)args[1]! == Math.Clamp(level + 4, 0, 20), type.Name + " skill bonus");
                Check(Probe.MechCustomizationLookupCalls == (fixedMode ? 0 : 1), type.Name + " scan");
            }
        }
        Pawn human = fx.NewPawn(fx.HumanDef, new IntVec3(2, 0, 2));
        fx.AddNoiseHediffs(human, 30);
        HediffFlagCache.ResetForValidation();
        Probe.Reset();
        object?[] schizo = { human.health.capacities, fx.Capacities["Breathing"], 0.2f };
        AccessTools.Method(typeof(Patch_Schizophrenia_ConsciousnessFloor), "Postfix").Invoke(null, schizo);
        Check(((float)schizo[2]!).Equals(0.2f), "unrelated schizophrenia capacity changed");
        Check(Probe.SchizophreniaLookupCalls == (fixedMode ? 0 : 1), "schizophrenia irrelevant-capacity scan");
        if (fixedMode) Check(HediffFlagCache.ScanCount == 0, "irrelevant capacity touched snapshot");
        HediffWithComps linked = new() { def = fx.SchizophreniaDef, pawn = human, comps = new List<HediffComp>() };
        human.health.hediffSet.hediffs.Add(linked);
        human.health.hediffSet.DirtyCache();
        foreach (float value in new[] { 0.1f, 0.5f, 0.9f })
        {
            Probe.Reset();
            object?[] args = { human.health.capacities, fx.Capacities["Consciousness"], value };
            AccessTools.Method(typeof(Patch_Schizophrenia_ConsciousnessFloor), "Postfix").Invoke(null, args);
            Check(((float)args[2]!).Equals(Math.Max(value, 0.5f)), "consciousness floor");
            Check(Probe.SchizophreniaLookupCalls == (fixedMode ? 0 : 1), "linked consciousness scan");
        }
        // The real prefix/postfix pair must balance its thread-static depth even on the empty path.
        MethodInfo prefix = AccessTools.Method(typeof(DMSSR.Patch_PawnCapacity_Calc), "Prefix");
        MethodInfo postfix = AccessTools.Method(typeof(DMSSR.Patch_PawnCapacity_Calc), "Postfix");
        FieldInfo depth = AccessTools.Field(typeof(DMSSR.Patch_PawnCapacity_Calc), "_capCallDepth");
        if (fixedMode) _ = HediffFlagCache.GetCapacityProviders(human);
        Probe.Reset();
        for (int i = 0; i < 20; i++)
        {
            prefix.Invoke(null, null);
            object?[] args = { human.health.hediffSet, fx.Capacities["Breathing"], 0.7f };
            postfix.Invoke(null, args);
            Check((int)depth.GetValue(null)! == 0 && ((float)args[2]!).Equals(0.7f), "provider-free depth/result");
        }
        Check(Probe.ProviderDefChecks == (fixedMode ? 0 : 620), "provider-free path still enters per-def lock");
        ProviderAndLinkCases(fx, fixedMode, human, mech, comp);
        if (fixedMode) SnapshotCases(fx);
        Console.WriteLine($"PASS health hotspots: {checks} assertions, helper/capacity/skill/schizophrenia/provider-free work counts.");
    }

    private static void ProviderAndLinkCases(Fixture fx, bool fixedMode, Pawn human, Pawn mech, HediffComp_MechCustomization comp)
    {
        var qa = new Harmony(Program.ValidationOwner);
        // Real vanilla CalculateCapacityLevel, not a substitute, with leaf PawnCapacityWorker=1.
        if (!fixedMode)
        {
            foreach (Type type in new[] { typeof(DMSSR.Patch_PawnCapacity_Calc), typeof(Patch_MechCustomization_ApplyCapacity), typeof(Patch_Schizophrenia_ConsciousnessFloor) })
                qa.CreateClassProcessor(type).Patch();
        }
        Pawn providerPawn = fx.NewPawn(fx.HumanDef, new IntVec3(2, 0, 2));
        var first = AddProvider(providerPawn, 1.5f, 0.1f, "ValidationProviderA");
        var second = AddProvider(providerPawn, 0.8f, -0.2f, "ValidationProviderB");
        providerPawn.health.hediffSet.DirtyCache();
        PawnCapacityDef capacity = fx.Capacities["Breathing"];
        float value = PawnCapacityUtility.CalculateCapacityLevel(providerPawn.health.hediffSet, capacity);
        Check(value.Equals((1f * (1.5f * 0.8f)) + (0.1f - 0.2f)), "ordered provider aggregation on real capacity entry");
        first.Mul = 1.1f;
        first.Add = 0.7f;
        value = PawnCapacityUtility.CalculateCapacityLevel(providerPawn.health.hediffSet, capacity);
        Check(value.Equals((1f * (1.1f * 0.8f)) + (0.7f - 0.2f)), "provider values froze in snapshot");
        first.Nested = () => PawnCapacityUtility.CalculateCapacityLevel(providerPawn.health.hediffSet, fx.Capacities["Sight"]);
        value = PawnCapacityUtility.CalculateCapacityLevel(providerPawn.health.hediffSet, capacity);
        Check(value.Equals((1f * (1.1f * 0.8f)) + (0.7f - 0.2f)), "provider reentrancy changed outer value");
        Check(first.NestedValue.Equals(1f), "nested capacity applied provider recursively");
        FieldInfo depth = AccessTools.Field(typeof(DMSSR.Patch_PawnCapacity_Calc), "_capCallDepth");
        Check((int)depth.GetValue(null)! == 0, "nested provider depth leaked");
        first.Nested = null;
        first.Throw = true;
        bool threw = false;
        try { _ = PawnCapacityUtility.CalculateCapacityLevel(providerPawn.health.hediffSet, capacity); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && (int)depth.GetValue(null)! == 0, "provider exception did not balance depth");
        first.Throw = false;
        fx.RemoveHediff(providerPawn, second.parent);
        Check(PawnCapacityUtility.CalculateCapacityLevel(providerPawn.health.hediffSet, capacity).Equals(1f * first.Mul + first.Add), "removed provider retained");
        if (fixedMode)
        {
            // Immutable snapshots are shared; provider values are read per invocation, and depth
            // remains thread-local. One shared pawn exercises actual parallel caller interleaving.
            var errors = new ConcurrentQueue<string>();
            Parallel.For(0, 16, _ =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    float actual = PawnCapacityUtility.CalculateCapacityLevel(providerPawn.health.hediffSet, capacity);
                    if (!actual.Equals(first.Mul + first.Add) || (int)depth.GetValue(null)! != 0) errors.Enqueue("parallel provider");
                }
            });
            Check(errors.IsEmpty, "parallel provider or ThreadStatic depth failed");
        }

        // Source values come from the real cached PawnCapacitiesHandler.GetLevel; the real link
        // comp resolves the source. Mutual links exercise DMSSR's original recursion flag.
        Pawn source = fx.NewPawn(fx.HumanDef, new IntVec3(3, 0, 2));
        foreach (string name in new[] { "Sight", "Hearing" })
        {
            PawnCapacityDef cap = fx.Capacities[name];
            cap.workerClass = typeof(PawnCapacityWorker);
            Check(source.health.capacities.GetLevel(cap).Equals(1f), "real source capacity fixture");
        }
        HediffWithComps linked = (HediffWithComps)human.health.hediffSet.hediffs.Last();
        HediffComp_SchizophreniaLink link = new() { parent = linked };
        linked.comps!.Add(link);
        link.SetSource(source, 8f, false, 60);
        human.health.hediffSet.DirtyCache();
        foreach (string name in new[] { "Sight", "Hearing" })
        {
            object?[] args = { human.health.capacities, fx.Capacities[name], 0.2f };
            AccessTools.Method(typeof(Patch_Schizophrenia_ConsciousnessFloor), "Postfix").Invoke(null, args);
            Check(((float)args[2]!).Equals(1f), "linked source " + name);
        }
        HediffWithComps reverse = new() { def = fx.SchizophreniaDef, pawn = source, comps = new() };
        HediffComp_SchizophreniaLink reverseComp = new() { parent = reverse };
        reverse.comps.Add(reverseComp);
        reverseComp.SetSource(human, 8f, false, 60);
        source.health.hediffSet.hediffs.Add(reverse);
        source.health.hediffSet.DirtyCache();
        Check(human.health.capacities.GetLevel(fx.Capacities["Sight"]).Equals(1f), "mutual link recursed");
        Check(!(bool)AccessTools.Field(typeof(Patch_Schizophrenia_ConsciousnessFloor), "resolvingLinkedCapacity").GetValue(null)!, "link flag leaked");

        // Every live customization consumer keeps its original arithmetic.
        if (!fixedMode) qa.CreateClassProcessor(typeof(Patch_MechCustomization_MechEnergyMax)).Patch();
        fx.MechDef.race.maxMechEnergy = 100;
        Need_MechEnergy energy = new(mech);
        Check(Energy(energy).Equals(250f), "real mech energy getter");
        object?[] body = { mech, 1f };
        AccessTools.Method(typeof(Patch_MechCustomization_BodySize), "Postfix").Invoke(null, body);
        Check(((float)body[1]!).Equals(1.4f), "body size offset");
        foreach (float supply in new[] { 0.01f, 0.1f, 1f, 2.5f, 12f })
        {
            comp.supply = supply;
            float factor = Math.Clamp(supply, 0.1f, 10f);
            object?[] min = { mech, 20 };
            AccessTools.Method(typeof(Patch_MechCustomization_MechEnergyThresholdMin), "Postfix").Invoke(null, min);
            Check((int)min[1]! == (int)Math.Floor(20f * factor), "minimum recharge threshold");
            object?[] max = { mech, 90f };
            AccessTools.Method(typeof(Patch_MechCustomization_MechEnergyThresholdMax), "Postfix").Invoke(null, max);
            Check(((float)max[1]!).Equals(90f * factor), "maximum recharge threshold");
            Check(Energy(energy).Equals(Math.Max(1f, 100f * factor)), "live supply clamp");
        }
        comp.supply = 2.5f;
        mech.needs = (Pawn_NeedsTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_NeedsTracker));
        mech.needs.energy = energy;
        object?[] boundedMin = { mech, 200 };
        AccessTools.Method(typeof(Patch_MechCustomization_MechEnergyThresholdMin), "Postfix").Invoke(null, boundedMin);
        Check((int)boundedMin[1]! == 250, "minimum threshold energy bound");
        object?[] boundedMax = { mech, 200f };
        AccessTools.Method(typeof(Patch_MechCustomization_MechEnergyThresholdMax), "Postfix").Invoke(null, boundedMax);
        Check(((float)boundedMax[1]!).Equals(250f), "maximum threshold energy bound");
        SkillRecord skill = new(mech, new SkillDef { defName = "ValidationSkill" });
        AccessTools.Field(typeof(SkillRecord), "levelInt").SetValue(skill, 12);
        AccessTools.Field(typeof(SkillRecord), "cachedTotallyDisabled").SetValue(skill, BoolUnknown.False);
        AccessTools.Field(typeof(SkillRecord), "cachedPermanentlyDisabled").SetValue(skill, BoolUnknown.False);
        if (!fixedMode)
        {
            qa.CreateClassProcessor(typeof(Patch_MechCustomization_SkillBonus_GetLevel)).Patch();
            qa.CreateClassProcessor(typeof(Patch_MechCustomization_SkillBonus_GetLevelForUI)).Patch();
        }
        Check(Skill(skill, false) == 16 && Skill(skill, true) == 16, "real skill entrypoints");
        AccessTools.Field(typeof(SkillRecord), "cachedTotallyDisabled").SetValue(skill, BoolUnknown.True);
        Check(Skill(skill, false) == 0 && Skill(skill, true) == 16, "disabled skill distinction");
        if (!fixedMode)
        {
            foreach (Type type in new[] { typeof(DMSSR.Patch_PawnCapacity_Calc), typeof(Patch_MechCustomization_ApplyCapacity),
                typeof(Patch_Schizophrenia_ConsciousnessFloor), typeof(Patch_MechCustomization_MechEnergyMax),
                typeof(Patch_MechCustomization_SkillBonus_GetLevel), typeof(Patch_MechCustomization_SkillBonus_GetLevelForUI) })
            {
                // Remove only harness-installed upstream methods; retain Probe's counters.
                MethodInfo patch = AccessTools.Method(type, "Postfix");
                foreach (MethodBase method in Harmony.GetAllPatchedMethods().ToArray())
                    if (Harmony.GetPatchInfo(method)?.Postfixes.Any(p => p.PatchMethod == patch) == true) qa.Unpatch(method, patch);
                if (type == typeof(DMSSR.Patch_PawnCapacity_Calc))
                    qa.Unpatch(AccessTools.Method(typeof(PawnCapacityUtility), "CalculateCapacityLevel"), AccessTools.Method(type, "Prefix"));
            }
        }
    }

    private static TestProvider AddProvider(Pawn pawn, float mul, float add, string defName)
    {
        HediffWithComps hediff = new() { def = new HediffDef { defName = defName, hediffClass = typeof(HediffWithComps) }, pawn = pawn, comps = new() };
        TestProvider provider = new() { parent = hediff, Mul = mul, Add = add };
        hediff.comps.Add(provider);
        pawn.health.hediffSet.hediffs.Add(hediff);
        return provider;
    }

    private static void SnapshotCases(Fixture fx)
    {
        Pawn pawn = fx.NewPawn(fx.MechDef, new IntVec3(2, 0, 2));
        fx.AddNoiseHediffs(pawn, 30);
        HediffFlagCache.ResetForValidation();
        Check(HediffFlagCache.GetMechCustomization(pawn) == null, "negative customization");
        HediffWithComps first = new() { def = fx.MechCustomizationDef, pawn = pawn, comps = new() };
        pawn.health.hediffSet.hediffs.Add(first);
        HediffComp_MechCustomization second = fx.AddMechCustomization(pawn);
        Check(HediffFlagCache.GetMechCustomization(pawn) == null, "first matching hediff without comp must win");
        fx.RemoveHediff(pawn, first);
        Check(ReferenceEquals(HediffFlagCache.GetMechCustomization(pawn), second), "first hediff removal not observed");
        HediffSet previous = pawn.health.hediffSet;
        HediffSet replacement = new(pawn);
        replacement.hediffs.AddRange(previous.hediffs.Where(h => h != second.parent));
        replacement.hediffs.Add(new HediffWithComps { pawn = pawn, def = fx.NoiseDef, comps = new() });
        pawn.health.hediffSet = replacement;
        Check(HediffFlagCache.GetMechCustomization(pawn) == null, "same-count HediffSet reference replacement ignored");
        replacement.hediffs[0] = second.parent;
        replacement.DirtyCache();
        Check(ReferenceEquals(HediffFlagCache.GetMechCustomization(pawn), second), "same-count DirtyCache replacement ignored");
        HediffWithComps linked = new() { pawn = pawn, def = fx.SchizophreniaDef, comps = new() };
        replacement.hediffs.Add(linked); // no DirtyCache: count fallback
        Check(ReferenceEquals(HediffFlagCache.GetSchizophreniaLinked(pawn), linked), "linked count invalidation");
        replacement.hediffs.Remove(linked);
        Check(HediffFlagCache.GetSchizophreniaLinked(pawn) == null, "linked count removal");
        _ = HediffFlagCache.GetCapacityProviders(pawn);
        Probe.Reset();
        int scans = HediffFlagCache.ScanCount;
        var errors = new ConcurrentQueue<string>();
        Parallel.For(0, 16, _ =>
        {
            for (int i = 0; i < 10_000; i++)
            {
                if (!ReferenceEquals(HediffFlagCache.GetMechCustomization(pawn), second)
                    || HediffFlagCache.GetSchizophreniaLinked(pawn) != null
                    || HediffFlagCache.GetCapacityProviders(pawn).Length != 0) errors.Enqueue("snapshot");
            }
        });
        Check(errors.IsEmpty && HediffFlagCache.ScanCount == scans, "parallel immutable snapshot reuse");
        Check(Probe.ProviderDefChecks == 0, "parallel warm snapshots entered provider locks");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float Energy(Need_MechEnergy energy) => energy.MaxLevel;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Skill(SkillRecord skill, bool ui) => ui ? skill.GetLevelForUI(false) : skill.GetLevel(false);

    private sealed class TestProvider : HediffComp, DMSSR.IDynamicCapacityProvider
    {
        internal float Mul;
        internal float Add;
        internal Func<float>? Nested;
        internal float NestedValue;
        internal bool Throw;
        public void GetCapacityAdjustments(PawnCapacityDef cap, out float mulFactor, out float addOffset)
        {
            if (Throw) throw new InvalidOperationException("QA provider failure");
            if (Nested != null) NestedValue = Nested();
            mulFactor = Mul;
            addOffset = Add;
        }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
    }
}
