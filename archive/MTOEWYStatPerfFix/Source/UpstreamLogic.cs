using System;
using System.Collections.Generic;
using MiliraTheOneEntwinedWithYou.Data;
using MiliraTheOneEntwinedWithYou.GameComponents;
using RimWorld;
using Verse;

namespace WhiteGiver.MTOEWYStatPerfFix;

/// <summary>
/// Straight ports of the four upstream helpers the stat postfix calls, with the
/// allocation-free gates that matter on RimWorld's hottest stat entry point:
/// <list type="bullet">
/// <item><c>IdealBackstoryBonuses.Adjust</c> / <c>Value</c></item>
/// <item><c>IdealExclusiveTraits.AdjustStat</c> / <c>Has</c> / <c>Enclosed</c></item>
/// <item><c>IdealWorldEffectService.MoveFactor</c> gate (delegates to upstream when active)</item>
/// <item><c>IdealEquipment.Stat</c> / <c>Holds</c> / <c>Wears</c></item>
/// </list>
/// Arithmetic, comparison order and constants mirror MiliraTheOneEntwinedWithYou 1.4.2.
/// </summary>
internal static class UpstreamLogic
{
    internal const string ChildhoodPlaceholder = "MTOEWY_IdealChildhoodPlaceholder";
    internal const string ChildhoodRemembered = "MTOEWY_IdealChildhoodRemembered";
    internal const string AdulthoodPlaceholder = "MTOEWY_IdealAdulthoodPlaceholder";

    internal const string TraitSkystriker = "MTOEWY_Trait_Skystriker";
    internal const string TraitDawnAegis = "MTOEWY_Trait_DawnAegis";
    internal const string TraitStarlightTinkerer = "MTOEWY_Trait_StarlightTinkerer";
    internal const string TraitInquisitiveMind = "MTOEWY_Trait_InquisitiveMind";

    internal const string ItemStarlightRailgun = "MTOEWY_StarlightRailgun";
    internal const string ItemStarlightLance = "MTOEWY_StarlightLance";
    internal const string ItemNightwingSuit = "MTOEWY_NightwingSuit";

    internal const string RewardMeleeMastery = "MTOEWY_Reward_MeleeMastery";
    internal const string RewardRangedMastery = "MTOEWY_Reward_RangedMastery";

    internal const string RepairJob = "Repair";

    internal static bool ChildhoodActive(Pawn pawn)
    {
        BackstoryDef? childhood = pawn.story?.Childhood;
        if (childhood == null)
        {
            return false;
        }

        string defName = childhood.defName;
        return defName == ChildhoodPlaceholder || defName == ChildhoodRemembered;
    }

    internal static bool AdulthoodActive(Pawn pawn)
    {
        BackstoryDef? adulthood = pawn.story?.Adulthood;
        return adulthood != null && adulthood.defName == AdulthoodPlaceholder;
    }

    internal static float BackstoryValue(StatId id, float value, bool child, bool adult)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return value;
        }

        if (child)
        {
            switch (id)
            {
                case StatId.GlobalLearningFactor:
                case StatId.SocialImpact:
                case StatId.ImmunityGainSpeed:
                    value *= 1.1f;
                    break;
                case StatId.RestFallRateFactor:
                    value *= 0.9f;
                    break;
            }
        }

        if (adult)
        {
            switch (id)
            {
                case StatId.GeneralLaborSpeed:
                case StatId.ResearchSpeed:
                    value *= 1.1f;
                    break;
                case StatId.MedicalTendQuality:
                    value += 0.1f;
                    break;
                case StatId.CarryingCapacity:
                    value += 10f;
                    break;
                case StatId.MentalBreakThreshold:
                    value = Math.Max(0f, value - 0.05f);
                    break;
            }
        }

        return value;
    }

    internal static void ApplyBackstory(Pawn pawn, StatId id, ref float result)
    {
        // Upstream: Array.IndexOf over 4+5 defName strings for every Pawn (chickens
        // included), then IdealExclusiveTraits.Owner. Non-humanlikes have no story and
        // can never satisfy either backstory placeholder, so the whole stage is a no-op.
        if (pawn.story == null)
        {
            return;
        }

        if (FastOwner.Owner(pawn) == null)
        {
            return;
        }

        result = BackstoryValue(id, result, ChildhoodActive(pawn), AdulthoodActive(pawn));
    }

    internal static void ApplyTraits(Pawn pawn, StatId id, ref float result)
    {
        float value = result;
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return;
        }

        GameComponent_IdealTracker? owner = FastOwner.Owner(pawn);
        if (owner == null)
        {
            return;
        }

        switch (id)
        {
            case StatId.MoveSpeed:
                if (Has(pawn, TraitSkystriker) && pawn.Spawned && !Enclosed(pawn))
                {
                    value += 0.3f;
                }

                if (Has(pawn, TraitDawnAegis) && UpstreamRefs.BurstActive!(owner.State.ExclusiveTraits, Now))
                {
                    value += 0.4f;
                }

                break;

            case StatId.ConstructionSpeed:
                if (pawn.CurJobDef?.defName == RepairJob && Has(pawn, TraitStarlightTinkerer))
                {
                    value *= 1.35f;
                }

                break;

            case StatId.JoyFallRateFactor:
                if (Has(pawn, TraitInquisitiveMind) && owner.State.ExclusiveTraits.NeedsStudy)
                {
                    value *= 1.15f;
                }

                break;
        }

        result = value;
    }

    private static bool Has(Pawn pawn, string traitDefName)
    {
        List<Trait>? traits = pawn.story?.traits?.allTraits;
        if (traits == null)
        {
            return false;
        }

        for (int i = 0; i < traits.Count; i++)
        {
            Trait trait = traits[i];
            TraitDef? def = trait.def;
            if (def != null && def.defName == traitDefName && !trait.Suppressed)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Enclosed(Pawn pawn)
    {
        if (!pawn.Spawned || !pawn.Position.Roofed(pawn.Map))
        {
            return false;
        }

        Room? room = pawn.GetRoom();
        return room != null && !room.PsychologicallyOutdoors;
    }

    internal static void ApplyWorld(Pawn pawn, ref float result)
    {
        // Upstream IdealWorldEffectService.MoveFactor resolves the component again, then
        // foreach's Effects. An empty effect list returns 1f, so the multiply is skipped:
        // one Count check instead of a second component scan.
        GameComponent_IdealTracker? tracker = TrackerCache.Current();
        if (tracker == null)
        {
            return;
        }

        IdealWorldEffectState? state = tracker.State?.WorldEffects;
        List<IdealWorldEffect>? effects = state?.Effects;
        if (effects == null || effects.Count == 0 || !pawn.Spawned)
        {
            return;
        }

        result *= UpstreamRefs.MoveFactor!(pawn);
    }

    internal static void ApplyEquipment(Pawn pawn, StatId id, ref float result)
    {
        ThingWithComps? primary = pawn.equipment?.Primary;
        Pawn_ApparelTracker? apparel = pawn.apparel;

        // Holds() and Wears() are both false, so IdealEquipment.Stat is a pure no-op.
        if (primary == null && (apparel == null || apparel.WornApparelCount == 0))
        {
            return;
        }

        bool aidier = FastOwner.Owner(pawn) != null;
        float value = result;

        if (id == StatId.MoveSpeed && Holds(primary, ItemStarlightRailgun) && !aidier)
        {
            value = Math.Max(0.1f, value - 0.3f);
        }

        if (id == StatId.AimingDelayFactor && Wears(apparel, ItemNightwingSuit))
        {
            value *= 0.8f;
        }

        if (aidier && Holds(primary, ItemStarlightLance) && UpstreamRefs.ActiveReward!(RewardMeleeMastery) != null)
        {
            if (id == StatId.MoveSpeed)
            {
                value *= 1.1f;
            }

            if (id == StatId.MeleeDodgeChance)
            {
                value += 0.1f;
            }
        }

        if (aidier && id == StatId.AimingDelayFactor && Holds(primary, ItemStarlightRailgun) && UpstreamRefs.ActiveReward!(RewardRangedMastery) != null)
        {
            value *= 0.8f;
        }

        result = value;
    }

    private static bool Holds(ThingWithComps? primary, string defName)
    {
        return primary?.def != null && primary.def.defName == defName;
    }

    private static bool Wears(Pawn_ApparelTracker? apparel, string defName)
    {
        if (apparel == null)
        {
            return false;
        }

        List<Apparel> worn = apparel.WornApparel;
        for (int i = 0; i < worn.Count; i++)
        {
            ThingDef? def = worn[i].def;
            if (def != null && def.defName == defName)
            {
                return true;
            }
        }

        return false;
    }

    internal static int Now
    {
        get
        {
            TickManager? ticks = Find.TickManager;
            return ticks?.TicksGame ?? 0;
        }
    }
}
