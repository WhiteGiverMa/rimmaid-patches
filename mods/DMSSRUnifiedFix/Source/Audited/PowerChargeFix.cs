using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMSSR.Hediffs;
using DMSSR.Jobs;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// Two independent safety repairs for Skylark's power charge:
/// 1. the dash grabbed every pawn in its path, including friendlies and allied mechs;
/// 2. the dash's <c>Burst</c> hediff (and the post-dash buff on an aborted job) was only removed by
///    the final arrival toil, so a downed/despawned/forced-stop charge left it forever.
/// The finish action removes the dash state on every job condition, and removes the post-dash buff
/// only when the charge did not legitimately complete.
/// </summary>
internal static class PowerChargeFix
{
    private static readonly ConditionalWeakTable<JobDriver, object> Registered = new();

    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        if (DMS_HediffDefOf.DMS_Skylark_PowerCharge_Burst == null
            || DMS_HediffDefOf.DMS_Skylark_PowerCharge_Buff == null)
        {
            return;
        }

        MethodInfo shouldCollect = AccessTools.Method(typeof(JobDriver_PowerChargeDash), "ShouldCollectPawn")
            ?? throw new MissingMethodException("JobDriver_PowerChargeDash.ShouldCollectPawn");
        MethodInfo reservations = AccessTools.Method(
            typeof(JobDriver_PowerChargeDash), nameof(JobDriver_PowerChargeDash.TryMakePreToilReservations))
            ?? throw new MissingMethodException("JobDriver_PowerChargeDash.TryMakePreToilReservations");
        MethodInfo makeNewToils = AccessTools.Method(typeof(JobDriver_PowerChargeDash), "MakeNewToils")
            ?? throw new MissingMethodException("JobDriver_PowerChargeDash.MakeNewToils");

        harmony.Patch(shouldCollect, postfix: new HarmonyMethod(
            typeof(PowerChargeFix), nameof(ShouldCollectPawnPostfix)));
        harmony.Patch(reservations, prefix: new HarmonyMethod(
            typeof(PowerChargeFix), nameof(ReservationsPrefix)));
        harmony.Patch(makeNewToils, postfix: new HarmonyMethod(
            typeof(PowerChargeFix), nameof(MakeNewToilsPostfix)));
        Installed = true;
    }

    private static void ShouldCollectPawnPostfix(
        JobDriver_PowerChargeDash __instance, Pawn p, ref bool __result)
    {
        if (!__result)
        {
            return;
        }

        Pawn? caster = __instance.pawn;
        if (caster == null || GenHostility.HostileTo(p, caster) == false)
        {
            __result = false;
        }
    }

    private static void ReservationsPrefix(JobDriver_PowerChargeDash __instance)
    {
        EnsureFinishAction(__instance);
    }

    private static void MakeNewToilsPostfix(JobDriver_PowerChargeDash __instance)
    {
        EnsureFinishAction(__instance);
    }

    private static void EnsureFinishAction(JobDriver_PowerChargeDash driver)
    {
        if (driver?.pawn == null || Registered.TryGetValue(driver, out _))
        {
            return;
        }

        Registered.Add(driver, new object());
        driver.AddFinishAction(condition => RemoveChargeHediffs(driver.pawn, condition));
    }

    private static void RemoveChargeHediffs(Pawn? pawn, JobCondition condition)
    {
        if (pawn?.health?.hediffSet == null)
        {
            return;
        }

        HediffDef? burst = DMS_HediffDefOf.DMS_Skylark_PowerCharge_Burst;
        HediffDef? buff = DMS_HediffDefOf.DMS_Skylark_PowerCharge_Buff;
        List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
        for (int i = hediffs.Count - 1; i >= 0; i--)
        {
            Hediff hediff = hediffs[i];
            if (hediff?.def == burst || (condition != JobCondition.Succeeded && hediff?.def == buff))
            {
                pawn.health.RemoveHediff(hediff);
            }
        }
    }
}
