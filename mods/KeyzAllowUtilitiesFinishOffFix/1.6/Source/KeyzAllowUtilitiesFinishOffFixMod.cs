using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Meidocho.KeyzAllowUtilitiesFinishOffFix;

public sealed class KeyzAllowUtilitiesFinishOffFixMod : Mod
{
    private const string HarmonyId = "meidocho.keyzallowutilities.finishofffix";
    private const string WorkGiverTypeName = "KeyzAllowUtilities.WorkGiver_FinishOff";
    private const string IsValidTargetMethodName = "IsValidTarget";

    public KeyzAllowUtilitiesFinishOffFixMod(ModContentPack content) : base(content)
    {
        Type workGiverType = AccessTools.TypeByName(WorkGiverTypeName);
        MethodInfo isValidTarget = workGiverType == null
            ? null
            : AccessTools.Method(
                workGiverType,
                IsValidTargetMethodName,
                new[] { typeof(Pawn), typeof(Pawn) });

        MethodInfo prefix = AccessTools.Method(
            typeof(KeyzAllowUtilitiesFinishOffFixMod),
            nameof(RejectUnmappedTarget));

        if (isValidTarget == null || prefix == null)
        {
            Log.Error(
                $"[Keyz Finish Off Fix] Could not resolve {WorkGiverTypeName}.IsValidTarget; patch not applied.");
            return;
        }

        new Harmony(HarmonyId).Patch(isValidTarget, prefix: new HarmonyMethod(prefix));
        Log.Message("[Keyz Finish Off Fix] Ignoring Finish Off targets that have already left the map.");
    }

    public static bool RejectUnmappedTarget(Pawn target, ref bool __result)
    {
        if (target != null && target.Map != null)
        {
            return true;
        }

        __result = false;
        return false;
    }
}
