using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DMSSR.AI;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// When the cephalopoda throw AI finds nearby enemies but no valid ally/enemy pair, both candidate
/// locals stay null and the original method dereferences <c>((Thing)null).Position</c>. Insert a
/// deterministic null check before the result pair is built and return a null job instead.
/// </summary>
internal static class CephalopodaThrowFix
{
    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        MethodInfo target = AccessTools.Method(
            typeof(JobGiver_AICastCephalopodaGraspOnBestEnemyValue), "TryGiveJob")
            ?? throw new MissingMethodException("DMSSR.AI.JobGiver_AICastCephalopodaGraspOnBestEnemyValue.TryGiveJob");
        MethodInfo transpiler = AccessTools.Method(typeof(CephalopodaThrowFix), nameof(Transpiler))
            ?? throw new MissingMethodException("CephalopodaThrowFix.Transpiler");

        harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
        Installed = true;
    }

    private static Job? GetJobOrNull(Ability? ability, Pawn? ally, Pawn? enemy)
    {
        if (ability == null || ally == null || enemy == null)
        {
            return null;
        }
        return ability.GetJob(new LocalTargetInfo(ally), new LocalTargetInfo(enemy.Position));
    }

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        List<CodeInstruction> result = new(instructions);
        MethodInfo implicitThing = AccessTools.Method(
            typeof(LocalTargetInfo), "op_Implicit", new[] { typeof(Thing) })
            ?? throw new MissingMethodException("Verse.LocalTargetInfo.op_Implicit(Thing)");

        int start = -1;
        for (int i = 0; i + 1 < result.Count; i++)
        {
            if (result[i].opcode == OpCodes.Ldloc_2
                && result[i + 1].opcode == OpCodes.Call
                && Equals(result[i + 1].operand, implicitThing))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            throw new InvalidOperationException(
                $"{original.DeclaringType?.FullName}.{original.Name}: expected the ally/enemy result pair; recheck upstream before using DMSSRUnifiedFix.");
        }

        // Replace the whole tail with one deterministic helper call. Building LocalTargetInfo
        // from the null enemy happens before the job is created in the original IL, so a guard
        // must run before both conversions.
        result.RemoveRange(start, result.Count - start);
        result.Add(new CodeInstruction(OpCodes.Ldloc_0));
        result.Add(new CodeInstruction(OpCodes.Ldloc_2));
        result.Add(new CodeInstruction(OpCodes.Ldloc_3));
        result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CephalopodaThrowFix), nameof(GetJobOrNull))));
        result.Add(new CodeInstruction(OpCodes.Ret));
        return result;
    }
}
