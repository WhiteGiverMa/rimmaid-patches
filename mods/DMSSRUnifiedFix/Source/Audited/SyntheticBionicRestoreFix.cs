using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// DMSSR explicitly allows a synthetic bionic to be installed on a missing limb whose parent is
/// intact (<c>SyntheticBionicUtility.CanInstallRecipeOnPart</c>), but the driver adds the AddedPart
/// hediff without restoring the missing record first. Vanilla <see cref="HediffSet.AddDirect"/>
/// rejects that add and the item is still consumed. Restore exactly like
/// <c>Recipe_InstallArtificialBodyPart</c> does before attaching the new part.
/// </summary>
internal static class SyntheticBionicRestoreFix
{
    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        MethodInfo addHediff = AccessTools.Method(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.AddHediff),
            new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })
            ?? throw new MissingMethodException("Verse.Pawn_HealthTracker.AddHediff(Hediff,...)");

        harmony.Patch(addHediff, prefix: new HarmonyMethod(
            typeof(SyntheticBionicRestoreFix), nameof(RestoreMissingPartPrefix)));
        Installed = true;
    }

    private static void RestoreMissingPartPrefix(Pawn_HealthTracker __instance, Hediff hediff, BodyPartRecord part)
    {
        if (hediff is not Hediff_AddedPart)
        {
            return;
        }

        BodyPartRecord? target = part ?? hediff.Part;
        if (target == null)
        {
            return;
        }

        HediffSet set = __instance.hediffSet;
        if (set == null || !set.PartIsMissing(target))
        {
            return;
        }

        __instance.RestorePart(target);
    }
}
