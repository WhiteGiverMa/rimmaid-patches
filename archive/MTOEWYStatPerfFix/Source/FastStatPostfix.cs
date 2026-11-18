using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.MTOEWYStatPerfFix;

/// <summary>
/// Replacement for MTOEWY's <c>IdealExclusiveStatPatch.Postfix</c> on
/// <see cref="StatExtension.GetStatValue(Thing, StatDef, bool, int)"/>.
/// <para>
/// Every stage is gated before it can do work:
/// 1. not a Pawn -&gt; return;
/// 2. StatDef is not one of the 14 stats MTOEWY can touch -&gt; one dictionary lookup and return;
/// 3. stage-specific cheap gates (story == null, empty world-effect list, no gear);
/// 4. the game component is cached instead of rescanning <c>Game.components</c>.
/// </para>
/// Results are bit-identical to upstream: the world-effect stage, burst window,
/// reward lookup and original-recovery flag are still executed by MTOEWY itself.
/// </summary>
internal static class FastStatPostfix
{
    internal static void Postfix(Thing __0, StatDef __1, ref float __result)
    {
        if (!(__0 is Pawn pawn))
        {
            return;
        }

        if (__1 == null)
        {
            return;
        }

        if (!StatDispatch.TryGet(__1, out StatId id))
        {
            return;
        }

        switch (id)
        {
            case StatId.GlobalLearningFactor:
            case StatId.SocialImpact:
            case StatId.ImmunityGainSpeed:
            case StatId.RestFallRateFactor:
            case StatId.GeneralLaborSpeed:
            case StatId.ResearchSpeed:
            case StatId.MedicalTendQuality:
            case StatId.CarryingCapacity:
            case StatId.MentalBreakThreshold:
                UpstreamLogic.ApplyBackstory(pawn, id, ref __result);
                break;

            case StatId.MoveSpeed:
                UpstreamLogic.ApplyTraits(pawn, id, ref __result);
                if (ReferenceEquals(__1, StatDefOf.MoveSpeed))
                {
                    // Upstream compares the world-effect stage by Def reference, not defName.
                    UpstreamLogic.ApplyWorld(pawn, ref __result);
                }

                UpstreamLogic.ApplyEquipment(pawn, id, ref __result);
                break;

            case StatId.ConstructionSpeed:
            case StatId.JoyFallRateFactor:
                UpstreamLogic.ApplyTraits(pawn, id, ref __result);
                break;

            case StatId.AimingDelayFactor:
            case StatId.MeleeDodgeChance:
                UpstreamLogic.ApplyEquipment(pawn, id, ref __result);
                break;
        }
    }
}
