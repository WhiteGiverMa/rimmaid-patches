using System;
using System.Collections.Generic;
using System.Reflection;
using DMSSR.Hediffs;
using HarmonyLib;
using RimWorld;
using Verse;
using WhiteGiver.DMSSRUnifiedFix.StatPathPerf;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// The Scourge's Undying armor comp computes the intended armor value but no stat path ever reads
/// it. Add a <see cref="StatPart"/> to both armor stats (the idiomatic finalize hook, and O(1) after
/// the shared health snapshot), and keep the visual countdown in sync with the comp's real 20,000
/// tick duration instead of the XML's 10,000 tick <c>Disappears</c> timer.
/// </summary>
internal static class UndyingArmorFix
{
    private static readonly AccessTools.FieldRef<HediffComp_ArmorBoost, int> TicksLeft =
        AccessTools.FieldRefAccess<HediffComp_ArmorBoost, int>("ticksLeft");

    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        EnsureDefWiring();

        StatDef? sharp = StatDefOf.ArmorRating_Sharp;
        StatDef? blunt = StatDefOf.ArmorRating_Blunt;
        if (sharp == null || blunt == null)
        {
            return;
        }
        AddPart(sharp);
        AddPart(blunt);

        MethodInfo setup = AccessTools.Method(typeof(HediffComp_ArmorBoost), nameof(HediffComp_ArmorBoost.Setup))
            ?? throw new MissingMethodException("DMSSR.Hediffs.HediffComp_ArmorBoost.Setup");
        MethodInfo expose = AccessTools.Method(
            typeof(HediffComp_ArmorBoost), nameof(HediffComp_ArmorBoost.CompExposeData))
            ?? throw new MissingMethodException("HediffComp_ArmorBoost.CompExposeData");

        harmony.Patch(setup, postfix: new HarmonyMethod(
            typeof(UndyingArmorFix), nameof(SetupPostfix)));
        harmony.Patch(expose, postfix: new HarmonyMethod(
            typeof(UndyingArmorFix), nameof(ExposeDataPostfix)));
        Installed = true;
    }

    internal static void EnsureDefWiring()
    {
        HediffDef? def = DMS_HediffDefOf.DMS_UndyingArmorBoost;
        if (def?.comps == null)
        {
            return;
        }

        for (int i = 0; i < def.comps.Count; i++)
        {
            if (def.comps[i] is HediffCompProperties_Disappears disappears
                && disappears.disappearsAfterTicks.min < 20000)
            {
                disappears.disappearsAfterTicks = new IntRange(20000, 20000);
            }
        }
    }

    private static void AddPart(StatDef stat)
    {
        stat.parts ??= new List<StatPart>();
        for (int i = 0; i < stat.parts.Count; i++)
        {
            if (stat.parts[i] is StatPart_UndyingArmor)
            {
                return;
            }
        }

        stat.parts.Add(new StatPart_UndyingArmor { parentStat = stat });
    }

    private static void SetupPostfix(HediffComp_ArmorBoost __instance, int totalDurationTicks)
    {
        HediffComp_Disappears? disappears =
            HediffUtility.TryGetComp<HediffComp_Disappears>(__instance.parent);
        if (disappears == null || totalDurationTicks <= 0)
        {
            return;
        }

        disappears.ticksToDisappear = totalDurationTicks;
        disappears.disappearsAfterTicks = totalDurationTicks;
    }

    private static void ExposeDataPostfix(HediffComp_ArmorBoost __instance)
    {
        if (Scribe.mode != LoadSaveMode.PostLoadInit)
        {
            return;
        }

        HediffComp_Disappears? disappears =
            HediffUtility.TryGetComp<HediffComp_Disappears>(__instance.parent);
        if (disappears == null)
        {
            return;
        }

        int remaining = TicksLeft(__instance);
        if (remaining > 0 && disappears.ticksToDisappear < remaining)
        {
            disappears.ticksToDisappear = remaining;
        }
        if (remaining > 0 && disappears.disappearsAfterTicks < remaining)
        {
            disappears.disappearsAfterTicks = remaining;
        }
    }
}

internal sealed class StatPart_UndyingArmor : StatPart
{
    public override void TransformValue(StatRequest req, ref float val)
    {
        if (req.Thing is not Pawn pawn)
        {
            return;
        }

        HediffComp_ArmorBoost? comp = HediffFlagCache.GetUndyingArmor(pawn);
        if (comp == null)
        {
            return;
        }

        float target = comp.GetTargetValue(parentStat);
        if (target >= 0f)
        {
            val = target;
        }
    }

    public override string ExplanationPart(StatRequest req) => string.Empty;
}
