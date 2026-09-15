using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CEBreachingFix;

public static class BreachingFixPatches
{
    [HarmonyPatch(typeof(Verb_LaunchProjectileCE), nameof(Verb_LaunchProjectileCE.CanHitTargetFrom), typeof(IntVec3), typeof(LocalTargetInfo))]
    public static class Patch_CanHitTargetFrom_BreachingFix
    {
        static void Postfix(Verb_LaunchProjectileCE __instance, IntVec3 root, LocalTargetInfo targ, ref bool __result)
        {
            if (!__result && BreachingFix.TryAllowShotWithoutLoS(__instance, root, targ, out _, out _))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_CanHitTargetFrom_Report_BreachingFix
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Verb_LaunchProjectileCE),
                nameof(Verb_LaunchProjectileCE.CanHitTargetFrom),
                new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(string).MakeByRefType() });
        }

        static void Postfix(Verb_LaunchProjectileCE __instance, IntVec3 root, LocalTargetInfo targ, ref string report, ref bool __result)
        {
            if (!__result && BreachingFix.TryAllowShotWithoutLoS(__instance, root, targ, out _, out _))
            {
                report = "";
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_TryFindCEShootLineFromTo_BreachingFix
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Verb_LaunchProjectileCE),
                nameof(Verb_LaunchProjectileCE.TryFindCEShootLineFromTo),
                new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(ShootLine).MakeByRefType(), typeof(Vector3).MakeByRefType() });
        }

        static void Postfix(Verb_LaunchProjectileCE __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, ref Vector3 targetPos, ref bool __result)
        {
            if (!__result && BreachingFix.TryAllowShotWithoutLoS(__instance, root, targ, out var line, out var pos))
            {
                resultingLine = line;
                targetPos = pos;
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    public static class Patch_JobGiver_AIBreaching_RepeatGuard
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(JobGiver_AIBreaching), "TryGiveJob");
        }

        static void Postfix(Pawn pawn, ref Job __result)
        {
            if (BreachingFix.ShouldReplaceInvalidBreachingJob(pawn, __result))
            {
                __result = JobMaker.MakeJob(JobDefOf.Wait, 60);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_PawnJobTracker_StartJob_BreachingGuard
    {
        static void Prefix(Pawn ___pawn, ref Job newJob, ThinkNode jobGiver)
        {
            if (BreachingFix.ShouldThrottleBreachingStartJob(___pawn, newJob, jobGiver))
            {
                newJob = JobMaker.MakeJob(JobDefOf.Wait, 60);
            }
        }
    }

    [HarmonyPatch(typeof(Toils_Combat), nameof(Toils_Combat.GotoCastPosition))]
    public static class Patch_GotoCastPosition_BreachingFix
    {
        static void Postfix(TargetIndex targetInd, TargetIndex castPositionInd, Toil __result)
        {
            var originalInit = __result.initAction;
            __result.initAction = () =>
            {
                if (BreachingFix.TryStartKnownCastPosition(__result.actor, targetInd, castPositionInd))
                {
                    return;
                }

                originalInit?.Invoke();
            };
        }
    }
}
