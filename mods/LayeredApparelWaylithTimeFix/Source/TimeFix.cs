using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace WhiteGiver.LayeredApparelWaylithTimeFix;

public sealed class TimeFix : Mod
{
    public TimeFix(ModContentPack content) : base(content)
    {
        var target = AccessTools.Method("LayeredApparel.CompLayeredApparel:UpdateVacuumStateCore");
        if (target == null)
            throw new MissingMethodException("LayeredApparel.CompLayeredApparel.UpdateVacuumStateCore");
        new Harmony("whitegiver.layeredapparel.waylithtimefix").Patch(target,
            transpiler: new HarmonyMethod(typeof(TimeFix), nameof(ReplaceClock)));
        Log.Message("[Layered Apparel Waylith Time Fix] Vacuum appearance clock patched.");
    }

    public static IEnumerable<CodeInstruction> ReplaceClock(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo method
                && method.DeclaringType?.FullName == "UnityEngine.Time"
                && method.Name == "get_unscaledTimeAsDouble")
                instruction.operand = AccessTools.PropertyGetter(typeof(UnityEngine.Time),
                    nameof(UnityEngine.Time.realtimeSinceStartupAsDouble));
            yield return instruction;
        }
    }
}
