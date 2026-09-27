using System.Reflection;
using HarmonyLib;
using Milira;
using RimWorld;
using Verse;
using Verse.AI;

namespace LocalFix_MiliraFlightNullCheck
{
    /// <summary>
    /// Registers this assembly's Harmony patches when the game loads it.
    /// [StaticConstructorOnStartup] is run exactly once per process by
    /// Verse.StaticConstructorOnStartupUtility.CallAll().
    /// [HarmonyPriority] only orders already-registered patches; it does not register anything.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class Bootstrap
    {
        private const string HarmonyId = "local.meidocho.MiliraFlightNullCheckFix";

        static Bootstrap()
        {
            new Harmony(HarmonyId).PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    /// <summary>
    /// Guards the NullReferenceException in Milira.MilianPatch_Pawn_FlightTracker_Notify_JobStarted.Prefix,
    /// which dereferences CompFlightControl.CanFly without checking that TryGetComp returned null.
    /// Only the crashing case (Milira body pawn, no flight-control comp, CanEverFly true) is
    /// intercepted; every other call returns true so Milira's own prefix runs unchanged.
    /// HigherThanNormal priority puts this prefix before Milira's Normal-priority prefix even
    /// though Milira registers first.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_FlightTracker), "Notify_JobStarted")]
    [HarmonyPriority(Priority.HigherThanNormal)]
    public static class Fix_MiliraFlightNullCheck
    {
        [HarmonyPrefix]
        public static bool Prefix(Job job, Pawn_FlightTracker __instance, Pawn ___pawn)
        {
            // Only Milira body pawns are affected by the upstream crash.
            if (___pawn == null || ___pawn.RaceProps == null || ___pawn.RaceProps.body == null ||
                ___pawn.RaceProps.body.defName != "Milira_Body")
                return true;

            CompFlightControl cf = ThingCompUtility.TryGetComp<CompFlightControl>(___pawn);
            if (cf != null)
                return true;

            // Upstream dereferences the null comp only when CanEverFly is true; when it is false
            // upstream handles this case itself, so leave it alone.
            if (!__instance.CanEverFly)
                return true;

            // Same landing fallback upstream uses when its flight condition is false.
            job.flying = false;
            if (__instance.Flying)
                __instance.ForceLand();
            return false;
        }
    }
}
