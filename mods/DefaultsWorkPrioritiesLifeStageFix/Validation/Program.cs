using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;
using RimWorld;
using Verse;

internal static class Program
{
    private const string Owner = "whitegiver.defaults.workprioritieslifestagefix";
    private static string gameRoot = "";
    private static string harmonyPath = "";
    private static string defaultsPath = "";
    private static string patchPath = "";
    private static readonly string BoundDirectory = Path.Combine(AppContext.BaseDirectory, "bound");
    private static readonly List<string> Errors = new();
    private static int policyCalls;
    private static bool failPolicyTranspile;
    private static List<WorkTypeDef> Assigned => Qa.Assigned;
    private static WorkTypeDef workA { get => Qa.workA; set => Qa.workA = value; }
    private static WorkTypeDef workB { get => Qa.workB; set => Qa.workB = value; }
    private static LifeStageDef baby { get => Qa.baby; set => Qa.baby = value; }
    private static LifeStageDef customChild { get => Qa.customChild; set => Qa.customChild = value; }
    private static LifeStageDef vanillaChild { get => Qa.vanillaChild; set => Qa.vanillaChild = value; }
    private static LifeStageDef adult { get => Qa.adult; set => Qa.adult = value; }
    private static LifeStageDef fullyFormed { get => Qa.fullyFormed; set => Qa.fullyFormed = value; }

    private sealed class Qa
    {
        public static readonly List<WorkTypeDef> Assigned = new();
        public static WorkTypeDef workA = null!;
        public static WorkTypeDef workB = null!;
        public static LifeStageDef baby = null!;
        public static LifeStageDef customChild = null!;
        public static LifeStageDef vanillaChild = null!;
        public static LifeStageDef adult = null!;
        public static LifeStageDef fullyFormed = null!;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation bind|red|green|rollback|absent GAME_ROOT HARMONY_DLL DEFAULTS_DLL PATCH_DLL");
            return 0;
        }
        if (args.Length != 5 || args[0] is not ("bind" or "red" or "green" or "rollback" or "absent"))
        {
            Console.Error.WriteLine("Expected a validation mode and four assembly paths; use --help.");
            return 1;
        }
        gameRoot = Path.GetFullPath(args[1]);
        harmonyPath = Path.GetFullPath(args[2]);
        defaultsPath = Path.GetFullPath(args[3]);
        patchPath = Path.GetFullPath(args[4]);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { BoundDirectory, gameRoot, Path.GetDirectoryName(harmonyPath)! })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            if (args[0] == "bind") Bind();
            else Run(args[0]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Bind()
    {
        Directory.CreateDirectory(BoundDirectory);
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(gameRoot);
        resolver.AddSearchDirectory(Path.GetDirectoryName(harmonyPath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(defaultsPath)!);
        using (var defaults = AssemblyDefinition.ReadAssembly(defaultsPath, new ReaderParameters { AssemblyResolver = resolver }))
        {
            int changed = 0;
            foreach (AssemblyNameReference reference in defaults.MainModule.AssemblyReferences)
            {
                if (!reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)) continue;
                reference.Name = "UnityStubs";
                reference.Version = new Version(1, 0, 0, 0);
                changed++;
            }
            defaults.Write(Path.Combine(BoundDirectory, "Defaults.dll"));
            Console.WriteLine($"QA-only Defaults copy: {changed} Unity references rebound; original DLL unchanged.");
        }
        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(gameRoot, "Assembly-CSharp.dll"),
            new ReaderParameters { AssemblyResolver = resolver });
        MethodDefinition configInitializer = game.MainModule.GetType("Verse.ModsConfig").Methods.Single(method => method.Name == ".cctor");
        configInitializer.Body.Instructions.Clear();
        configInitializer.Body.ExceptionHandlers.Clear();
        configInitializer.Body.Variables.Clear();
        configInitializer.Body.GetILProcessor().Emit(Mono.Cecil.Cil.OpCodes.Ret);
        game.Write(Path.Combine(BoundDirectory, "Assembly-CSharp.dll"));
        Console.WriteLine("QA-only game copy: ModsConfig initialization disabled; no user-config IO.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string mode)
    {
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
        Type mod = patch.GetType("WhiteGiver.DefaultsWorkPrioritiesLifeStageFix.DefaultsLifeStageFixMod", true)!;
        if (mode == "absent")
        {
            Activator.CreateInstance(mod, new object?[] { null });
            Check(!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true),
                "Absent Defaults registered a patch.");
            Console.WriteLine("PASS: absent Defaults skips the entire patch group.");
            return;
        }

        Assembly upstream = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(BoundDirectory, "Defaults.dll"));
        Type workType = upstream.GetType("Defaults.WorkPriorities.Patch_Pawn", true)!;
        Type policyType = upstream.GetType("Defaults.Policies.Patch_Pawn_AgeTracker", true)!;
        MethodInfo workPostfix = AccessTools.DeclaredMethod(workType, "Postfix", new[] { typeof(Pawn) });
        MethodInfo policyPostfix = AccessTools.DeclaredMethod(policyType, "Postfix",
            new[] { typeof(Pawn_AgeTracker), typeof(Pawn), typeof(int) });
        MethodInfo originalFirst = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(Enumerable.First) && method.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(LifeStageAge));
        MethodInfo originalMinAge = AccessTools.DeclaredMethod(typeof(Pawn_AgeTracker), nameof(Pawn_AgeTracker.LifeStageMinAge),
            new[] { typeof(LifeStageDef) });
        if (mode == "rollback")
        {
            VerifyRollback(mod, workPostfix, policyPostfix);
            return;
        }
        InitializeDefs();

        if (mode == "red")
        {
            InstallQaStubs(upstream);
            RedWorkPriorities(workPostfix);
            RedPolicies(policyPostfix);
            Console.WriteLine("RED: actual stable Defaults reproduces both the no-child First exception and the custom-child policy error.");
            return;
        }

        Activator.CreateInstance(mod, new object?[] { null });
        Check(Harmony.GetPatchInfo(workPostfix)?.Transpilers.Count(info => info.owner == Owner) == 1,
            "Work-priority transpiler was not registered exactly once.");
        Check(Harmony.GetPatchInfo(policyPostfix)?.Transpilers.Count(info => info.owner == Owner) == 1,
            "Policy transpiler was not registered exactly once.");
        Check(Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true)
                .ToHashSet().SetEquals(new[] { workPostfix, policyPostfix }),
            "Patch group touched a method outside the two intended targets.");
        Type fix = patch.GetType("WhiteGiver.DefaultsWorkPrioritiesLifeStageFix.LifeStageCompatibilityFix", true)!;
        MethodInfo firstReplacement = fix.GetMethod("FirstChildLifeStage")!;
        MethodInfo minAgeReplacement = fix.GetMethod("ChildMinAge")!;
        ValidateCurrentAndTranspiler(fix, "WorkPrioritiesTranspiler", workPostfix, originalFirst, firstReplacement);
        ValidateCurrentAndTranspiler(fix, "PoliciesTranspiler", policyPostfix, originalMinAge, minAgeReplacement);
        InstallQaStubs(upstream);
        GreenWorkPriorities(workPostfix);
        GreenPolicies(policyPostfix);
        ValidateAssemblyReferences();
        Console.WriteLine("GREEN: actual Defaults targets pass no-child, custom-child and vanilla-child behavior; two-call patch group verified.");
    }

    private static void RedWorkPriorities(MethodInfo postfix)
    {
        Pawn drone = CreatePawn(NoChildStages(humanlike: false), 0, humanlike: false);
        SetTemporaryWorkTypes(workA);
        bool reproduced = false;
        try { postfix.Invoke(null, new object[] { drone }); }
        catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException invalid
            && invalid.Message.Contains("Sequence contains no matching element", StringComparison.Ordinal))
        {
            reproduced = true;
        }
        Check(reproduced, "Stable work-priority First did not reproduce the no-child exception.");

        Assigned.Clear();
        Pawn vanilla = CreatePawn(VanillaStages(), 3, humanlike: true);
        SetTemporaryWorkTypes(workA);
        postfix.Invoke(null, new object[] { vanilla });
        Check(Assigned.ToHashSet().SetEquals(new[] { workA, workB }), "Stable vanilla-child baseline changed before patching.");
        Console.WriteLine("RED: no-child race throws Sequence contains no matching element; vanilla-child baseline remains functional.");
    }

    private static void RedPolicies(MethodInfo postfix)
    {
        Errors.Clear();
        policyCalls = 0;
        Pawn custom = CreatePawn(CustomChildStages(), 3, humanlike: true);
        postfix.Invoke(null, new object?[] { custom.ageTracker, custom, 3 });
        Check(Errors.Any(message => message.Contains("Life stage def", StringComparison.Ordinal)),
            "Stable policy lookup did not emit the expected missing-HumanlikeChild error.");
        Check(policyCalls == 0, "Stable custom-child policy unexpectedly applied at age three.");
        Console.WriteLine("RED: custom-child policy lookup logs the missing-stage error and misses the age-three policy assignment.");
    }

    private static void GreenWorkPriorities(MethodInfo postfix)
    {
        Assigned.Clear();
        Pawn drone = CreatePawn(NoChildStages(humanlike: false), 0, humanlike: false);
        SetTemporaryWorkTypes(workA);
        postfix.Invoke(null, new object[] { drone });
        Check(Assigned.SequenceEqual(new[] { workA }), "No-child race expanded or changed newly enabled work types.");

        Assigned.Clear();
        Pawn olderCustom = CreatePawn(CustomChildStages(), 10, humanlike: true);
        SetTemporaryWorkTypes(workA);
        postfix.Invoke(null, new object[] { olderCustom });
        Check(Assigned.SequenceEqual(new[] { workA }), "Custom-child age-ten path changed newly enabled work types.");

        foreach (List<LifeStageAge> stages in new[] { CustomChildStages(), VanillaStages() })
        {
            Assigned.Clear();
            Pawn child = CreatePawn(stages, 3, humanlike: true);
            SetTemporaryWorkTypes(workA);
            postfix.Invoke(null, new object[] { child });
            Check(Assigned.ToHashSet().SetEquals(new[] { workA, workB }),
                "Becoming a child did not preserve the all-work-types default assignment.");
        }
        Console.WriteLine("GREEN: work priorities preserve no-child, custom age-ten, custom age-three and vanilla age-three behavior.");
    }

    private static void GreenPolicies(MethodInfo postfix)
    {
        foreach (List<LifeStageAge> stages in new[] { CustomChildStages(), VanillaStages() })
        {
            Errors.Clear();
            policyCalls = 0;
            Pawn child = CreatePawn(stages, 3, humanlike: true);
            postfix.Invoke(null, new object?[] { child.ageTracker, child, 3 });
            Check(policyCalls == 1, "Age-three child policy was not applied exactly once.");
            Check(Errors.Count == 0, "Patched child policy emitted an unexpected error.");
        }

        Errors.Clear();
        policyCalls = 0;
        Pawn noChild = CreatePawn(NoChildStages(humanlike: true), 0, humanlike: true);
        postfix.Invoke(null, new object?[] { noChild.ageTracker, noChild, 0 });
        Check(policyCalls == 0 && Errors.Count == 0, "No-child race was treated as age zero or emitted an error.");
        Console.WriteLine("GREEN: policies apply at the real child age and safely skip races without a child stage.");
    }

    private static void VerifyRollback(Type mod, MethodInfo workPostfix, MethodInfo policyPostfix)
    {
        const string rollbackOwner = Owner + ".validation.rollback";
        Harmony harmony = new(rollbackOwner);
        Errors.Clear();
        harmony.Patch(AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(RecordError)));
        harmony.Patch(policyPostfix, transpiler: new HarmonyMethod(typeof(Program), nameof(FailPolicyTranspiler)));
        failPolicyTranspile = true;
        Activator.CreateInstance(mod, new object?[] { null });
        failPolicyTranspile = false;
        Check(Errors.Any(message => message.Contains("Could not apply", StringComparison.Ordinal)),
            "Forced second-patch failure did not reach the production catch path.");
        Check(!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true),
            "Rollback left a production patch registered after the second target failed.");
        Check(Harmony.GetPatchInfo(policyPostfix)?.Transpilers.Any(info => info.owner == rollbackOwner) == true,
            "Rollback removed another owner's transpiler.");
        Console.WriteLine("PASS: forced second-patch failure rolls back the first patch and preserves the other Harmony owner.");
    }

    private static IEnumerable<CodeInstruction> FailPolicyTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        if (failPolicyTranspile) throw new InvalidOperationException("QA forced second-patch failure.");
        return instructions;
    }

    private static void InstallQaStubs(Assembly upstream)
    {
        Harmony harmony = new(Owner + ".validation");
        harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.GetDisabledWorkTypes), new[] { typeof(bool) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(EmptyDisabledWorkTypes)));
        Type workUtility = upstream.GetType("Defaults.WorkPriorities.WorkPriorityUtility", true)!;
        harmony.Patch(AccessTools.Method(workUtility, "SetWorkPrioritiesToDefault", new[] { typeof(Pawn), typeof(WorkTypeDef) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(RecordWorkPriority)));
        Type pawnTypeUtility = upstream.GetType("Defaults.PawnTypeUtility", true)!;
        harmony.Patch(AccessTools.Method(pawnTypeUtility, "GetPawnType", new[] { typeof(Pawn) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(ReturnChildPawnType)));
        Type policyUtility = upstream.GetType("Defaults.Policies.PolicyUtility", true)!;
        MethodInfo setPolicies = policyUtility.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "SetAllDefaultPolicies" && method.GetParameters().Length == 2);
        harmony.Patch(setPolicies, prefix: new HarmonyMethod(typeof(Program), nameof(RecordPolicy)));
        harmony.Patch(AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(Program), nameof(RecordError)));
    }

    private static bool EmptyDisabledWorkTypes(ref List<WorkTypeDef> __result)
    {
        __result = new List<WorkTypeDef>();
        return false;
    }

    private static bool RecordWorkPriority(WorkTypeDef def)
    {
        Assigned.Add(def);
        return false;
    }

    private static bool ReturnChildPawnType(ref object __result, MethodInfo __originalMethod)
    {
        Type enumType = Nullable.GetUnderlyingType(__originalMethod.ReturnType) ?? __originalMethod.ReturnType;
        __result = Enum.Parse(enumType, "ChildColonist");
        return false;
    }

    private static bool RecordPolicy()
    {
        policyCalls++;
        return false;
    }

    private static bool RecordError(string text)
    {
        Errors.Add(text);
        return false;
    }

    private static void InitializeDefs()
    {
        workA = new WorkTypeDef { defName = "QA_WorkA" };
        workB = new WorkTypeDef { defName = "QA_WorkB" };
        DefDatabase<WorkTypeDef>.Add(workA);
        DefDatabase<WorkTypeDef>.Add(workB);
        baby = new LifeStageDef { defName = "QA_Baby", developmentalStage = DevelopmentalStage.Baby };
        customChild = new LifeStageDef { defName = "QA_CustomChild", developmentalStage = DevelopmentalStage.Child };
        vanillaChild = new LifeStageDef { defName = "HumanlikeChild", developmentalStage = DevelopmentalStage.Child };
        adult = new LifeStageDef { defName = "QA_Adult", developmentalStage = DevelopmentalStage.Adult };
        fullyFormed = new LifeStageDef { defName = "MechanoidFullyFormed", developmentalStage = DevelopmentalStage.Adult };
        LifeStageDefOf.HumanlikeChild = vanillaChild;
    }

    private static List<LifeStageAge> CustomChildStages() => new()
    {
        new LifeStageAge { def = baby, minAge = 0f },
        new LifeStageAge { def = customChild, minAge = 3f },
        new LifeStageAge { def = adult, minAge = 18f }
    };

    private static List<LifeStageAge> VanillaStages() => new()
    {
        new LifeStageAge { def = baby, minAge = 0f },
        new LifeStageAge { def = vanillaChild, minAge = 3f },
        new LifeStageAge { def = adult, minAge = 18f }
    };

    private static List<LifeStageAge> NoChildStages(bool humanlike) => new()
    {
        new LifeStageAge { def = humanlike ? adult : fullyFormed, minAge = humanlike ? 18f : 0f }
    };

    private static Pawn CreatePawn(List<LifeStageAge> stages, int ageYears, bool humanlike)
    {
        var pawn = new Pawn
        {
            def = new ThingDef
            {
                defName = "QA_Pawn",
                race = new RaceProperties
                {
                    intelligence = humanlike ? Intelligence.Humanlike : Intelligence.Animal,
                    lifeStageAges = stages
                }
            },
            Name = new NameSingle("QA")
        };
        pawn.ageTracker = new Pawn_AgeTracker(pawn);
        AccessTools.Field(typeof(Pawn_AgeTracker), "ageBiologicalTicksInt").SetValue(pawn.ageTracker, ageYears * 3600000L);
        return pawn;
    }

    private static void SetTemporaryWorkTypes(params WorkTypeDef[] defs) =>
        AccessTools.Field(typeof(Pawn_AgeTracker), "tmpEnabledWorkTypes").SetValue(null, defs.ToList());

    private static void ValidateCurrentAndTranspiler(Type fix, string transpilerName, MethodInfo target,
        MethodInfo original, MethodInfo replacement)
    {
        List<CodeInstruction> current = PatchProcessor.GetCurrentInstructions(target);
        Check(current.Count(instruction => instruction.Calls(replacement)) == 1 && !current.Any(instruction => instruction.Calls(original)),
            $"Patched {target.Name} does not contain exactly one replacement call.");
        MethodInfo transpiler = fix.GetMethod(transpilerName, BindingFlags.NonPublic | BindingFlags.Static)!;
        List<CodeInstruction> before = PatchProcessor.GetOriginalInstructions(target);
        int callIndex = before.FindIndex(instruction => instruction.Calls(original));
        Check(callIndex >= 0, $"Original {target.Name} call was not found.");
        var opcodes = before.Select(instruction => instruction.opcode).ToArray();
        var operands = before.Select(instruction => instruction.operand).ToArray();
        var labels = before.Select(instruction => instruction.labels.ToArray()).ToArray();
        var blocks = before.Select(instruction => instruction.blocks.ToArray()).ToArray();
        var after = ((IEnumerable<CodeInstruction>)transpiler.Invoke(null, new object[] { before })!).ToList();
        Check(after.Count == before.Count, $"Transpiler changed {target.Name} instruction count.");
        for (int index = 0; index < before.Count; index++)
        {
            Check(ReferenceEquals(before[index], after[index])
                && after[index].opcode == (index == callIndex ? OpCodes.Call : opcodes[index])
                && Equals(after[index].operand, index == callIndex ? replacement : operands[index])
                && after[index].labels.SequenceEqual(labels[index]) && after[index].blocks.SequenceEqual(blocks[index]),
                $"Transpiler changed unrelated {target.Name} instructions, labels or exception blocks.");
        }
        foreach (int count in new[] { 0, 2 })
        {
            bool rejected = false;
            try
            {
                transpiler.Invoke(null, new object[] { Enumerable.Range(0, count)
                    .Select(_ => new CodeInstruction(OpCodes.Call, original)).ToList() });
            }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException) { rejected = true; }
            Check(rejected, $"Unexpected call count {count} was not rejected for {target.Name}.");
        }
        Console.WriteLine($"GREEN: {target.DeclaringType?.Name}.{target.Name} changes one call only; 0/2-call drift rejected.");
    }

    private static void ValidateAssemblyReferences()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(patchPath);
        Check(!assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "Defaults"
            || reference.Name.StartsWith("UnityEngine", StringComparison.Ordinal)),
            "Patch has a compile-time Defaults or legacy Unity dependency.");
        Check(assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "Assembly-CSharp")
            && assembly.MainModule.AssemblyReferences.Any(reference => reference.Name == "0Harmony"),
            "Patch is missing its game or Harmony reference.");
        Console.WriteLine("GREEN: patch assembly has no Defaults or legacy Unity compile dependency.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
