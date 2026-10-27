using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace WhiteGiver.AutoAbilityNullTargetFix;

public sealed class AutoAbilityNullTargetFixMod : Mod
{
    public AutoAbilityNullTargetFixMod(ModContentPack content) : base(content)
    {
        MyValidateNullTargetFix.Apply();
    }
}

/// <summary>
/// Auto Ability's friendly-fire AOE check reads current2.Pawn.IsColonist directly.
/// Area targets without a Pawn (cells, non-pawn things) make that read throw, so
/// every automatic cast of such an ability logs a red error and is skipped.
/// </summary>
public static class MyValidateNullTargetFix
{
    public const string HarmonyId = "whitegiver.autoability.nulltargetfix";
    private const string LogPrefix = "[AutoAbility Null Target Fix]";
    private static MethodInfo? getPawn;

    public static void Apply()
    {
        try
        {
            Type? utils = AccessTools.TypeByName("AutoAbility.AutoAbility_Utils");
            if (utils == null)
            {
                Log.Message($"{LogPrefix} Auto Ability is not loaded; patch skipped.");
                return;
            }

            MethodInfo? target = AccessTools.GetDeclaredMethods(utils).FirstOrDefault(method =>
                method.Name == "MyValidate" && method.IsStatic && method.ReturnType == typeof(bool) &&
                method.GetParameters().Length == 4);
            if (target == null)
            {
                Log.Error($"{LogPrefix} Expected AutoAbility_Utils.MyValidate signature is missing; patch not applied.");
                return;
            }

            getPawn = AccessTools.PropertyGetter(typeof(LocalTargetInfo), nameof(LocalTargetInfo.Pawn));
            if (getPawn == null)
            {
                Log.Error($"{LogPrefix} LocalTargetInfo.Pawn getter is missing; patch not applied.");
                return;
            }

            int unsafeChecks = 0;
            List<CodeInstruction> original = PatchProcessor.GetOriginalInstructions(target).ToList();
            for (int index = 0; index + 1 < original.Count; index++)
            {
                if (!original[index].Calls(getPawn)) continue;
                if (original[index + 1].operand is MethodInfo getter && getter.DeclaringType == typeof(Pawn) &&
                    getter.Name is "get_IsColonist" or "get_IsColonyMech" or "get_IsColonyAnimal")
                {
                    unsafeChecks++;
                }
            }
            if (unsafeChecks != 3)
            {
                Log.Error($"{LogPrefix} Expected three unsafe target.Pawn reads in MyValidate, found {unsafeChecks}; patch not applied.");
                return;
            }

            new Harmony(HarmonyId).Patch(target,
                transpiler: new HarmonyMethod(typeof(MyValidateNullTargetFix), nameof(Transpiler)));
            Log.Message($"{LogPrefix} Patched MyValidate (three null-safe target checks).");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Could not apply null-target compatibility patch: {exception}");
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo pawnGetter = getPawn ?? throw new InvalidOperationException($"{LogPrefix} Pawn getter is not resolved.");
        List<CodeInstruction> codes = instructions.ToList();
        int replaced = 0;
        for (int index = 0; index + 1 < codes.Count; index++)
        {
            if (!codes[index].Calls(pawnGetter)) continue;
            if (codes[index + 1].operand is not MethodInfo getter || getter.DeclaringType != typeof(Pawn)) continue;
            MethodInfo? replacement = getter.Name switch
            {
                "get_IsColonist" => AccessTools.Method(typeof(MyValidateNullTargetFix), nameof(IsColonistSafe)),
                "get_IsColonyMech" => AccessTools.Method(typeof(MyValidateNullTargetFix), nameof(IsColonyMechSafe)),
                "get_IsColonyAnimal" => AccessTools.Method(typeof(MyValidateNullTargetFix), nameof(IsColonyAnimalSafe)),
                _ => null
            };
            if (replacement == null) continue;
            codes[index + 1].opcode = OpCodes.Call;
            codes[index + 1].operand = replacement;
            replaced++;
        }

        if (replaced != 3)
        {
            throw new InvalidOperationException(
                $"{LogPrefix} Expected three unsafe pawn checks in MyValidate, replaced {replaced}.");
        }
        return codes;
    }

    public static bool IsColonistSafe(Pawn? pawn) => pawn != null && pawn.IsColonist;

    public static bool IsColonyMechSafe(Pawn? pawn) => pawn != null && pawn.IsColonyMech;

    public static bool IsColonyAnimalSafe(Pawn? pawn) => pawn != null && pawn.IsColonyAnimal;
}
