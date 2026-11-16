using System;
using System.Reflection;
using DMSSR.Gene;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// The right-click "enter stasis pod" option checks <c>CanStartEntryFor</c> and then queues the job,
/// but it never calls <c>TryQueueSubject</c>. <c>JobDriver_EnterGeneFittingPod</c> requires
/// <c>PodComp.IsQueuedSubject(pawn)</c> in its global fail condition, so the freshly ordered job
/// ends as Incompletable. Queue the subject before the driver's reservations are evaluated.
/// </summary>
internal static class GeneFittingPodFix
{
    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        MethodInfo reservations = AccessTools.Method(
            typeof(JobDriver_EnterGeneFittingPod), nameof(JobDriver_EnterGeneFittingPod.TryMakePreToilReservations))
            ?? throw new MissingMethodException("DMSSR.Gene.JobDriver_EnterGeneFittingPod.TryMakePreToilReservations");

        harmony.Patch(reservations, prefix: new HarmonyMethod(
            typeof(GeneFittingPodFix), nameof(QueueSubjectPrefix)));
        Installed = true;
    }

    private static void QueueSubjectPrefix(JobDriver_EnterGeneFittingPod __instance)
    {
        Pawn? pawn = __instance.pawn;
        Building_DMSGeneFittingPod? pod = __instance.job?.targetA.Thing as Building_DMSGeneFittingPod;
        Comp_DMSGeneFittingPod? comp = pod?.GetComp<Comp_DMSGeneFittingPod>();
        if (pawn == null || comp == null || comp.IsQueuedSubject(pawn))
        {
            return;
        }

        if (comp.CanStartEntryFor(pawn, out _))
        {
            comp.TryQueueSubject(pawn, false);
        }
    }
}
