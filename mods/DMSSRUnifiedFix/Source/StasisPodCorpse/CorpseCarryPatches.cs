using System;
using System.Collections.Generic;
using System.Reflection;
using DMSSR.Content;
using HarmonyLib;
using Verse.AI;

namespace WhiteGiver.DMSSRUnifiedFix.StasisPodCorpse;

internal static class CorpseCarryPatches
{
    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        Type driver = typeof(JobDriver_CarryCorpseToStasisPod);
        MethodInfo reservations = AccessTools.DeclaredMethod(driver, nameof(JobDriver.TryMakePreToilReservations), new[] { typeof(bool) })
            ?? throw new MissingMethodException(driver.FullName, nameof(JobDriver.TryMakePreToilReservations));
        MethodInfo toils = AccessTools.DeclaredMethod(driver, "MakeNewToils", Type.EmptyTypes)
            ?? throw new MissingMethodException(driver.FullName, "MakeNewToils");
        if (reservations.ReturnType != typeof(bool) || toils.ReturnType != typeof(IEnumerable<Toil>))
            throw new InvalidOperationException("Corpse carry driver signature changed.");

        HarmonyMethod normalize = new(typeof(CorpseCarryPatches), nameof(NormalizeCount));
        harmony.Patch(reservations, prefix: normalize);
        // Prefix the iterator FACTORY: the count is corrected immediately, even on save reloads
        // that rebuild toils without making reservations again. All upstream toils stay intact.
        harmony.Patch(toils, prefix: normalize);
        Installed = true;
    }

    private static void NormalizeCount(JobDriver_CarryCorpseToStasisPod __instance)
    {
        Job? job = __instance.job;
        if (job != null && job.count <= 0) job.count = 1;
    }
}
