using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using RimWorld;
using Verse;

internal static class Program
{
    private const string Owner = "whitegiver.autoability.nulltargetfix";
    private static string gameRoot = "";
    private static string harmonyPath = "";
    private static string abilityPath = "";
    private static string patchPath = "";

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--help")
        {
            Console.WriteLine("Usage: Validation red|green|absent GAME_ROOT HARMONY_DLL AUTOABILITY_DLL PATCH_DLL");
            return 0;
        }
        if (args.Length != 5 || args[0] is not ("red" or "green" or "absent"))
        {
            Console.Error.WriteLine("Expected a validation mode and four assembly paths; use --help.");
            return 1;
        }
        gameRoot = Path.GetFullPath(args[1]);
        harmonyPath = Path.GetFullPath(args[2]);
        abilityPath = Path.GetFullPath(args[3]);
        patchPath = Path.GetFullPath(args[4]);
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { gameRoot, Path.GetDirectoryName(harmonyPath)!, Path.GetDirectoryName(abilityPath)! })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        try
        {
            Run(args[0]);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(string mode)
    {
        Assembly patch = AssemblyLoadContext.Default.LoadFromAssemblyPath(patchPath);
        Type mod = patch.GetType("WhiteGiver.AutoAbilityNullTargetFix.AutoAbilityNullTargetFixMod", true)!;
        if (mode == "absent")
        {
            Activator.CreateInstance(mod, new object?[] { null });
            Check(!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method)?.Owners.Contains(Owner) == true),
                "Absent Auto Ability registered a patch.");
            Console.WriteLine("PASS: actual Mod constructor skips absent Auto Ability without registering patches.");
            return;
        }

        Assembly abilityAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(abilityPath);
        Type utils = abilityAssembly.GetType("AutoAbility.AutoAbility_Utils", true)!;
        MethodInfo validate = FindMyValidate(utils);
        MethodInfo? pawnGetter = AccessTools.PropertyGetter(typeof(LocalTargetInfo), nameof(LocalTargetInfo.Pawn));
        Check(pawnGetter != null, "LocalTargetInfo.Pawn getter is missing.");
        int unsafeChecks = 0;
        List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(validate).ToList();
        for (int index = 0; index + 1 < original.Count; index++)
        {
            if (!original[index].Calls(pawnGetter)) continue;
            if (original[index + 1].operand is MethodInfo getter && getter.DeclaringType == typeof(Pawn) &&
                getter.Name is "get_IsColonist" or "get_IsColonyMech" or "get_IsColonyAnimal")
            {
                unsafeChecks++;
            }
        }
        Check(unsafeChecks == 3, $"Expected three unsafe target.Pawn reads in MyValidate, found {unsafeChecks}.");

        if (mode == "red")
        {
            bool reproduced = false;
            try { InvokeValidate(validate, abilityAssembly); }
            catch (TargetInvocationException exception) when (exception.InnerException is NullReferenceException) { reproduced = true; }
            Check(reproduced, "Original MyValidate did not throw for a non-pawn affected target.");
            Console.WriteLine("RED: actual MyValidate threw NullReferenceException on a non-pawn affected target.");
            return;
        }

        Activator.CreateInstance(mod, new object?[] { null });
        Check(Harmony.GetPatchInfo(validate)?.Transpilers.Count(info => info.owner == Owner) == 1,
            "Actual Mod constructor did not register one transpiler.");
        Type fix = patch.GetType("WhiteGiver.AutoAbilityNullTargetFix.MyValidateNullTargetFix", true)!;
        List<CodeInstruction> current = PatchProcessor.GetCurrentInstructions(validate);
        Check(current.Count(instruction => instruction.operand is MethodInfo method && method.DeclaringType == fix) == 3,
            "Actual patched MyValidate does not call the three null-safe helpers.");
        Check(!current.Any(instruction => instruction.operand is MethodInfo method &&
                method.DeclaringType == typeof(Pawn) &&
                method.Name is "get_IsColonist" or "get_IsColonyMech" or "get_IsColonyAnimal"),
            "Actual patched MyValidate still contains an unsafe pawn read.");
        object? result = InvokeValidate(validate, abilityAssembly);
        Check(result is true, "Patched MyValidate did not accept a non-pawn target.");
        MethodInfo colonist = fix.GetMethod("IsColonistSafe")!;
        Check((bool)colonist.Invoke(null, new object?[] { null })! == false, "Null target helper is not safe.");
        Pawn pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
        Check((bool)colonist.Invoke(null, new object?[] { pawn })! == false, "Helper changed its result for a pawn without a faction.");
        Console.WriteLine("GREEN: actual patched MyValidate returned true; three unsafe pawn reads replaced; null helper safe.");
    }

    private static MethodInfo FindMyValidate(Type utils)
    {
        MethodInfo? target = AccessTools.GetDeclaredMethods(utils).FirstOrDefault(method =>
            method.Name == "MyValidate" && method.IsStatic && method.ReturnType == typeof(bool) &&
            method.GetParameters().Length == 4);
        if (target == null) throw new InvalidOperationException("AutoAbility_Utils.MyValidate is missing.");
        return target;
    }

    private static object? InvokeValidate(MethodInfo validate, Assembly abilityAssembly)
    {
        Type settingType = abilityAssembly.GetType("AutoAbility.AutoCastSetting", true)!;
        object setting = Activator.CreateInstance(settingType)!;
        Type autoType = abilityAssembly.GetType("AutoAbility.AutoAbility_Utils+AutoType", true)!;
        settingType.GetField("autoType")!.SetValue(setting, Enum.Parse(autoType, "UseAfterCooldown"));
        settingType.GetField("autoPreventAlly")!.SetValue(setting, true);
        settingType.GetField("stopIfHasWindow")!.SetValue(setting, false);
        settingType.GetField("onlyApplyToNoHediff")!.SetValue(setting, false);
        settingType.GetField("notUseToIneffective")!.SetValue(setting, false);
        settingType.GetField("onlyApplyToAllowedArea")!.SetValue(setting, false);
        settingType.GetField("minUseDis")!.SetValue(setting, 0);
        settingType.GetField("maxUseDis")!.SetValue(setting, 500);

        var ability = new ValidationAbility
        {
            pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn)),
            def = new AbilityDef
            {
                defName = "ValidationAoeAbility",
                canUseAoeToGetTargets = false,
                statBases = new List<StatModifier> { new() { stat = null!, value = 5f } }
            }
        };
        AccessTools.Field(typeof(Ability), "verbTracker").SetValue(ability, new VerbTracker(ability));
        Check(ability.def.HasAreaOfEffect, "Fixture AbilityDef has no area of effect.");

        var targetThing = new Thing
        {
            def = new ThingDef { defName = "ValidationTarget", thingClass = typeof(Thing), useHitPoints = false }
        };
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(targetThing, new IntVec3(0, 0, 0));
        var target = new LocalTargetInfo(targetThing);
        Check(target.Pawn == null, "Fixture target unexpectedly has a Pawn.");

        return validate.Invoke(null, new object?[] { ability, target, null, setting });
    }

    private sealed class ValidationAbility : Ability
    {
        public override AcceptanceReport CanCast => AcceptanceReport.WasAccepted;

        public override bool CanApplyOn(LocalTargetInfo target) => true;

        public override bool GizmoDisabled(out string reason)
        {
            reason = string.Empty;
            return false;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
