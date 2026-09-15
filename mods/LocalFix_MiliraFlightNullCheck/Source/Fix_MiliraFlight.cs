using HarmonyLib;
using Milira;
using RimWorld;
using Verse;
using Verse.AI;

namespace LocalFix_MiliraFlightNullCheck
{
    /// <summary>
    /// Fixes NRE in Milira.MilianPatch_Pawn_FlightTracker_Notify_JobStarted.Prefix
    /// where CompFlightControl.CanFly is accessed without null-checking TryGetComp result.
    /// Runs BEFORE Milira's prefix via HarmonyPriority.HigherThanNormal.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_FlightTracker), "Notify_JobStarted")]
    [HarmonyPriority(Priority.HigherThanNormal)]
    public static class Fix_MiliraFlightNullCheck
    {
        [HarmonyPrefix]
        public static bool Prefix(Job job, Pawn_FlightTracker __instance, Pawn ___pawn)
        {
            // Only intercept Milira body pawns
            if (___pawn == null || ___pawn.RaceProps == null || ___pawn.RaceProps.body == null ||
                ___pawn.RaceProps.body.defName != "Milira_Body")
                return true;

            CompFlightControl cf = ThingCompUtility.TryGetComp<CompFlightControl>(___pawn);

            // Original Milira logic, but with null check on cf
            if (__instance.CanEverFly && cf != null && cf.CanFly &&
                (___pawn.Drafted || (___pawn.mindState != null && ___pawn.mindState.enemyTarget != null)))
            {
                __instance.StartFlying();
                job.flying = true;
                Hediff hediff = HediffMaker.MakeHediff(MiliraDefOf.Milira_InFlight, ___pawn, null);
                ___pawn.health.AddHediff(hediff, null, null, null);
                return false;
            }

            // Safe fallback: either no comp or CanFly is false - force land
            job.flying = false;
            if (__instance.Flying)
                __instance.ForceLand();
            return false;
        }
    }
}
