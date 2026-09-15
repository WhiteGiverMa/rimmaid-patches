using System;
using CombatExtended;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Meidocho.CEFix.MiliraFloatUnitElevation;

/// <summary>
/// BASELINE (2026-07-15): Before this patch there was zero float-unit filtering
/// during automatic target acquisition — Milira float units were freely eligible
/// for every auto-target finder in every faction.
///
/// This patch wraps the incoming <c>Predicate&lt;Thing&gt; validator</c> of both
/// the vanilla <see cref="AttackTargetFinder.BestAttackTarget"/> and the CE
/// <see cref="NonSnapAttackTargetFinder.BestAttackTarget"/> so that the policy
/// helper (<see cref="FloatUnitAutoTargetHelper.ShouldSkipFloatUnit"/>) is
/// composed after any existing validator.  Manual player attacks and forced turret
/// targets are never affected because neither path calls a target finder.
/// </summary>
[StaticConstructorOnStartup]
public static class FloatUnitAutoTargetPatch
{
    static FloatUnitAutoTargetPatch()
    {
        var harmony = new Harmony("meidocho.ce_fix_milira_float_unit_autotarget");
        harmony.PatchAll();
    }
}

// ── vanilla AttackTargetFinder.BestAttackTarget ───────────────────────
[HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
public static class Vanilla_BestAttackTarget_Patch
{
    /// <summary>
    /// Composes the incoming <c>validator</c> with the float-unit policy so that
    /// an existing validator (e.g. NeedThreat for turrets, LOS checks) still runs
    /// first; only candidates that pass the original check AND are not skipped by
    /// <see cref="FloatUnitAutoTargetHelper.ShouldSkipFloatUnit"/> are accepted.
    /// </summary>
    public static void Prefix(
        IAttackTargetSearcher searcher,
        ref Predicate<Thing> validator)
    {
        Predicate<Thing>? original = validator;
        validator = t =>
        {
            if (original != null && !original(t))
                return false;
            return !FloatUnitAutoTargetHelper.ShouldSkipFloatUnit(searcher, t);
        };
    }
}

// ── CE NonSnapAttackTargetFinder.BestAttackTarget ─────────────────────
[HarmonyPatch(typeof(NonSnapAttackTargetFinder),
               nameof(NonSnapAttackTargetFinder.BestAttackTarget))]
public static class NonSnap_BestAttackTarget_Patch
{
    /// <summary>
    /// Same composition as <see cref="Vanilla_BestAttackTarget_Patch"/> but for
    /// the CE non-snapping turret target finder.  This ensures CE turrets that
    /// call <c>NonSnapAttackTargetFinder.BestAttackTarget</c> with a
    /// <c>NeedThreat</c>-only validator also respect the policy.
    /// </summary>
    public static void Prefix(
        IAttackTargetSearcher searcher,
        ref Predicate<Thing> validator)
    {
        Predicate<Thing>? original = validator;
        validator = t =>
        {
            if (original != null && !original(t))
                return false;
            return !FloatUnitAutoTargetHelper.ShouldSkipFloatUnit(searcher, t);
        };
    }
}
