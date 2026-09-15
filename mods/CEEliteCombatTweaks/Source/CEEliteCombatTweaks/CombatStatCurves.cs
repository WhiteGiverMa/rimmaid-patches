using RimWorld;
using UnityEngine;
using Verse;

namespace CEEliteCombatTweaks;

public static class CombatStatCurves
{
    private const float EliteCooldownMaxReduction = 0.75f;
    private const float EliteCooldownScoreScale = 0.65f;
    public const float EliteCooldownManipulationWeight = 0.55f;
    public const float EliteCooldownMovingWeight = 0.30f;
    public const float EliteCooldownBreathingWeight = 0.15f;

    public static float EliteCooldownReduction(Pawn pawn)
    {
        float score = EliteCooldownScore(pawn);
        if (score <= 0f)
            return 0f;

        return EliteCooldownMaxReduction * (1f - Mathf.Exp(-score / EliteCooldownScoreScale));
    }

    public static float EliteCooldownScore(Pawn pawn)
    {
        if (pawn?.health?.capacities == null)
            return 0f;

        return NonNegativeFiniteOrZero(
            CapacityExcess(pawn, PawnCapacityDefOf.Manipulation) * EliteCooldownManipulationWeight
            + CapacityExcess(pawn, PawnCapacityDefOf.Moving) * EliteCooldownMovingWeight
            + CapacityExcess(pawn, PawnCapacityDefOf.Breathing) * EliteCooldownBreathingWeight);
    }

    public static float CapacityLevel(Pawn pawn, PawnCapacityDef capacity)
    {
        if (pawn?.health?.capacities == null)
            return 1f;

        return NonNegativeFiniteOrZero(FiniteOr(pawn.health.capacities.GetLevel(capacity), 1f));
    }

    public static float CapacityWeightedExcess(Pawn pawn, PawnCapacityDef capacity, float weight)
    {
        return CapacityExcess(pawn, capacity) * weight;
    }

    public static float NonNegativeFiniteOrZero(float value)
    {
        if (float.IsNaN(value) || float.IsNegativeInfinity(value) || value < 0f)
            return 0f;

        return float.IsPositiveInfinity(value) ? float.MaxValue : value;
    }

    private static float FiniteOr(float value, float fallback)
    {
        if (float.IsNaN(value))
            return fallback;

        if (float.IsPositiveInfinity(value))
            return float.MaxValue;

        if (float.IsNegativeInfinity(value))
            return -float.MaxValue;

        return value;
    }

    public static float CapacityExcess(Pawn pawn, PawnCapacityDef capacity)
    {
        return Mathf.Max(0f, CapacityLevel(pawn, capacity) - 1f);
    }

}
