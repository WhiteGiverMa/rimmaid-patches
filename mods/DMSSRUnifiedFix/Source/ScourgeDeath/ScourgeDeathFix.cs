using WhiteGiver.DMSSRUnifiedFix;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DMSSR.Content;
using Fortified;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRUnifiedFix.ScourgeDeath;

internal static class DeathHediffRemoval
{
    internal static void Apply(Harmony harmony)
    {
        foreach (MethodBase target in TargetMethods())
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(DeathHediffRemoval), nameof(Transpiler)));
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Patch_ScourgeNextDeath), "Prefix");
        yield return AccessTools.DeclaredMethod(typeof(HumanlikeMech), nameof(HumanlikeMech.Kill));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        MethodInfo remove = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff));
        MethodInfo guarded = AccessTools.Method(typeof(DeathHediffRemoval), nameof(RemoveDuringDeath));
        List<CodeInstruction> result = new(instructions);
        int replacements = 0;
        foreach (CodeInstruction instruction in result)
        {
            if (!instruction.Calls(remove)) continue;
            instruction.opcode = OpCodes.Call;
            instruction.operand = guarded;
            replacements++;
        }
        if (replacements != 1)
            throw new InvalidOperationException($"{original.DeclaringType?.FullName}.{original.Name}: expected one death-time RemoveHediff call, found {replacements}. Recheck upstream before using DMSSRUnifiedFix.");
        return result;
    }

    private static void RemoveDuringDeath(Pawn_HealthTracker health, Hediff hediff)
    {
        // These two callers run before Pawn.Kill raises its own reentrancy flag.
        // Keep every removal callback, but do not start a second death from its state check.
        bool previous = health.isBeingKilled;
        health.isBeingKilled = true;
        try { health.RemoveHediff(hediff); }
        finally { health.isBeingKilled = previous; }
    }
}
