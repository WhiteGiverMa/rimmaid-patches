using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DMSSR.Abilities;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// The ability passed <c>0f</c> as the base tend quality and clamped the configured 1.3 maximum to
/// 1.0, so every field tend landed between 0% and 25%. Vanilla <see cref="HediffComp_TendDuration.CompTended"/>
/// already supports maxQuality above 1.0; pass the configured maximum as both the base quality and
/// the cap, which keeps the authored 1.3 ceiling and produces 105%-130% tends.
/// </summary>
internal static class EmergencyTendFix
{
    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        MethodInfo target = AccessTools.Method(
            typeof(CompAbilityEffect_EmergencyTend),
            nameof(CompAbilityEffect_EmergencyTend.Apply),
            new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo) })
            ?? throw new MissingMethodException("DMSSR.Abilities.CompAbilityEffect_EmergencyTend.Apply");
        MethodInfo transpiler = AccessTools.Method(typeof(EmergencyTendFix), nameof(Transpiler))
            ?? throw new MissingMethodException("EmergencyTendFix.Transpiler");

        harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
        Installed = true;
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        List<CodeInstruction> result = new(instructions);
        MethodInfo clamp = AccessTools.Method(typeof(Mathf), nameof(Mathf.Clamp01))
            ?? throw new MissingMethodException("UnityEngine.Mathf.Clamp01");
        MethodInfo identity = AccessTools.Method(typeof(EmergencyTendFix), nameof(Unclamped))
            ?? throw new MissingMethodException("EmergencyTendFix.Unclamped");
        MethodInfo tended = AccessTools.Method(typeof(Hediff), nameof(Hediff.Tended))
            ?? throw new MissingMethodException("Verse.Hediff.Tended");
        MethodInfo tendedFromMax = AccessTools.Method(typeof(EmergencyTendFix), nameof(TendedFromMax))
            ?? throw new MissingMethodException("EmergencyTendFix.TendedFromMax");

        int clampCount = 0;
        int tendedCount = 0;
        foreach (CodeInstruction instruction in result)
        {
            if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, clamp))
            {
                instruction.operand = identity;
                clampCount++;
            }
            else if (instruction.opcode == OpCodes.Callvirt && Equals(instruction.operand, tended))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = tendedFromMax;
                tendedCount++;
            }
        }

        if (clampCount != 1 || tendedCount != 1)
        {
            throw new InvalidOperationException(
                $"{original.DeclaringType?.FullName}.{original.Name}: expected one Clamp01 and one Hediff.Tended call, found {clampCount}/{tendedCount}. Recheck upstream before using DMSSRUnifiedFix.");
        }

        return result;
    }

    private static float Unclamped(float value) => value;

    private static void TendedFromMax(Hediff hediff, float quality, float maxQuality, int batchPosition)
    {
        hediff.Tended(maxQuality, maxQuality, batchPosition);
    }
}
