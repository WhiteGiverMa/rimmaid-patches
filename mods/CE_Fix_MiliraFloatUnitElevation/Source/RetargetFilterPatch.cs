using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CombatExtended;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Meidocho.CEFix.MiliraFloatUnitElevation;

/// <summary>
/// BASELINE (2026-07-15): Verb_LaunchProjectileCE.Retarget() unconditionally
/// enumerates PawnsNearSegment(…).OfType&lt;Pawn&gt;() and selects the first
/// candidate whose faction matches the original target, is hostile to Caster,
/// is not downed, and is in line-of-fire (CanHitFromCellIgnoringRange).  No
/// float-unit filtering existed — Milira float units were freely chosen as
/// mid-burst retarget candidates regardless of the auto-target policy.
///
/// SOURCE (CE current at plan time, always check Harmony log if anchor fails):
///   G:\dev\CombatExtended\…\Verb_LaunchProjectileCE.cs:997
///     foreach (Pawn possibleTarget in Caster.Position
///         .PawnsNearSegment(lastTargetPos, caster.Map, 3, false)
///         .OfType&lt;Pawn&gt;())                              ← anchor
///
/// This transpiler injects candidate filtering immediately after the
/// OfType&lt;Pawn&gt;() call.  It passes the verb instance (ldarg.0) and the
/// IEnumerable&lt;Pawn&gt; to FilterCandidates(), which removes any pawn
/// rejected by FloatUnitAutoTargetHelper.ShouldSkipFloatUnit(searcher, pawn).
/// All original faction / downed / line-of-fire checks continue unchanged.
///
/// SAFETY: If the anchor is absent (CE restructured the method), the patch
/// logs a clear error and returns unmodified IL — no silent behaviour change.
/// </summary>
[StaticConstructorOnStartup]
public static class RetargetFilterPatch
{
    // ── cached reflection lookups (resolved once, shared across all uses) ──

    private static MethodInfo? _ofTypePawnCached;
    private static MethodInfo? _filterHelperCached;

    static RetargetFilterPatch()
    {
        try
        {
            var retargetMethod = AccessTools.Method(
                typeof(Verb_LaunchProjectileCE), "Retarget");

            if (retargetMethod == null)
            {
                Log.Error("[CE_Fix_MiliraFloatUnitElevation] " +
                    "Verb_LaunchProjectileCE.Retarget() not found; " +
                    "retarget float-unit filter disabled.");
                return;
            }

            new Harmony("meidocho.ce_fix_milira_float_unit_retarget").Patch(
                retargetMethod,
                transpiler: new HarmonyMethod(
                    typeof(RetargetFilterPatch), nameof(Transpiler)));
        }
        catch (Exception ex)
        {
            Log.Error("[CE_Fix_MiliraFloatUnitElevation] " +
                $"Retarget filter init failed: {ex}");
        }
    }

    // ── MethodInfo resolution (Harmony first, raw-reflection fallback) ───

    /// <summary>
    /// Resolves the closed generic <c>Enumerable.OfType&lt;Pawn&gt;</c>.
    /// Returns <c>null</c> if resolution is impossible (missing reference).
    /// </summary>
    private static MethodInfo? _resolveOfTypePawn()
    {
        // Primary: Harmony's AccessTools (handles generic resolution cleanly)
        var method = AccessTools.Method(
            typeof(Enumerable), "OfType",
            new[] { typeof(System.Collections.IEnumerable) },
            new[] { typeof(Pawn) });

        // Fallback: raw reflection search for the open-generic definition
        if (method == null)
        {
            var candidates = typeof(Enumerable).GetMethods(
                    BindingFlags.Static | BindingFlags.Public)
                .Where(m => m.Name == "OfType"
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType
                        == typeof(System.Collections.IEnumerable))
                .ToArray();

            if (candidates.Length > 0)
                method = candidates[0].MakeGenericMethod(typeof(Pawn));
        }

        _ofTypePawnCached = method;
        return method;
    }

    // ── transpiler ────────────────────────────────────────────────────────

    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        _ofTypePawnCached ??= _resolveOfTypePawn();
        if (_ofTypePawnCached == null)
        {
            Log.Error("[CE_Fix_MiliraFloatUnitElevation] " +
                "Cannot resolve Enumerable.OfType<Pawn>; " +
                "retarget filter inactive.");
            return instructions;
        }

        _filterHelperCached ??= AccessTools.Method(
            typeof(RetargetFilterPatch), nameof(FilterCandidates));
        if (_filterHelperCached == null)
        {
            Log.Error("[CE_Fix_MiliraFloatUnitElevation] " +
                "Cannot resolve FilterCandidates helper; " +
                "retarget filter inactive.");
            return instructions;
        }

        // ── IL match: call Enumerable.OfType<Pawn>(IEnumerable) ──
        var matcher = new CodeMatcher(instructions)
            .MatchStartForward(
                new CodeMatch(OpCodes.Call, _ofTypePawnCached));

        if (matcher.IsInvalid)
        {
            Log.Error("[CE_Fix_MiliraFloatUnitElevation] " +
                "Retarget IL anchor (call Enumerable.OfType<Pawn>) not " +
                "found — CE may have restructured " +
                "Verb_LaunchProjectileCE.Retarget().  " +
                "Float-unit retarget filtering DISABLED.  No IL modified.");
            return instructions;
        }

        // pos is at the OfType<Pawn> call.  Advance past it, then inject:
        //   ldarg.0             → push 'this' (Verb_LaunchProjectileCE)
        //   call FilterCandidates(IEnumerable<Pawn>, Verb) → IEnumerable<Pawn>
        //
        // Stack transformation:
        //   Before OfType:        [..., IEnumerable ]
        //   After  OfType:        [..., IEnumerable<Pawn>]
        //   After  ldarg.0:       [..., IEnumerable<Pawn>, Verb]
        //   After  FilterCandidates: [..., IEnumerable<Pawn>  (filtered)]
        matcher.Advance(1);
        matcher.Insert(
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, _filterHelperCached));

        Log.Message("[CE_Fix_MiliraFloatUnitElevation] " +
            "Retarget float-unit filter injected after OfType<Pawn>().");
        return matcher.InstructionEnumeration();
    }

    // ── injected helper ───────────────────────────────────────────────────

    /// <summary>
    /// Called from injected IL.  Removes from <paramref name="candidates"/>
    /// any <see cref="Pawn"/> that the active auto-target policy says the
    /// shooter should skip (see
    /// <see cref="FloatUnitAutoTargetHelper.ShouldSkipFloatUnit"/>).
    ///
    /// Preserves original enumeration order.  Non-pawn casters and null verb
    /// references pass candidates through unchanged — the filter is never a
    /// breaking change for unknown callers.
    /// </summary>
    /// <param name="candidates">The OfType&lt;Pawn&gt;() result from the
    ///     PawnsNearSegment chain.</param>
    /// <param name="verb">The Verb_LaunchProjectileCE instance (ldarg.0).</param>
    private static IEnumerable<Pawn> FilterCandidates(
        IEnumerable<Pawn> candidates, Verb verb)
    {
        var searcher = verb?.Caster as IAttackTargetSearcher;
        if (searcher == null)
            return candidates;

        return candidates.Where(
            p => !FloatUnitAutoTargetHelper.ShouldSkipFloatUnit(searcher, p));
    }
}
