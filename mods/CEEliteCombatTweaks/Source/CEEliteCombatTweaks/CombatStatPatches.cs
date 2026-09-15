using System.Collections.Generic;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CEEliteCombatTweaks;

public static class CombatStatPatches
{
    [HarmonyPatch(typeof(VerbProperties), nameof(VerbProperties.AdjustedCooldown), typeof(Verb), typeof(Pawn))]
    public static class Patch_CyclicRateFloor
    {
        [HarmonyPriority(Priority.Low)]
        static void Postfix(Verb ownerVerb, ref float __result)
        {
            if (ownerVerb is not Verb_LaunchProjectileCE || ownerVerb.verbProps is null)
                return;

            float cyclicRateFloor = ownerVerb.TicksBetweenBurstShots.TicksToSeconds();
            if (__result < cyclicRateFloor)
                __result = cyclicRateFloor;
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.GetValueUnfinalized))]
    public static class Patch_RangedCooldownUnfinalizedValue
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var getBaseValueFor = AccessTools.Method(typeof(StatWorker), nameof(StatWorker.GetBaseValueFor));
            var applyOffset = AccessTools.Method(typeof(Patch_RangedCooldownUnfinalizedValue), nameof(ApplyEliteCooldownOffset));

            for (int i = 0; i < codes.Count - 1; i++)
            {
                if (!codes[i].Calls(getBaseValueFor) || codes[i + 1].opcode != OpCodes.Stloc_0)
                    continue;

                codes.InsertRange(i + 2, new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Ldloc_0),
                    new CodeInstruction(OpCodes.Call, applyOffset),
                    new CodeInstruction(OpCodes.Stloc_0)
                });
                return codes;
            }

            Log.Warning("[CE Elite Combat Tweaks] Could not patch RangedCooldownFactor offset stage; StatWorker.GetValueUnfinalized layout changed.");
            return codes;
        }

        public static float ApplyEliteCooldownOffset(StatWorker worker, StatRequest request, float value)
        {
            if (worker.stat != StatDefOf.RangedCooldownFactor || request.Thing is not Pawn pawn)
                return value;

            return value - CombatStatCurves.EliteCooldownReduction(pawn);
        }
    }

}
