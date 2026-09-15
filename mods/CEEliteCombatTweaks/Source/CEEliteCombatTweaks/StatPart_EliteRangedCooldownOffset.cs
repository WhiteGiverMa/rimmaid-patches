using System.Text;
using RimWorld;
using Verse;

namespace CEEliteCombatTweaks;

public class StatPart_EliteRangedCooldownOffset : StatPart
{
    public override void TransformValue(StatRequest req, ref float val)
    {
        // Explanation-only StatPart; the actual value enters StatWorker.GetValueUnfinalized as an offset.
    }

    public override string ExplanationPart(StatRequest req)
    {
        if (req.Thing is not Pawn pawn)
            return null;

        float reduction = CombatStatCurves.EliteCooldownReduction(pawn);
        if (reduction <= 0.001f)
            return null;

        var explanation = new StringBuilder();
        explanation.AppendLine("CE Elite body control: -" + reduction.ToStringPercent("0.#"));
        explanation.AppendLine("  " + ("StatsReport_Health".CanTranslate() ? "StatsReport_Health".Translate() : "StatsReport_HealthFactors".Translate()));
        AppendCapacityLine(explanation, pawn, PawnCapacityDefOf.Manipulation, CombatStatCurves.EliteCooldownManipulationWeight);
        AppendCapacityLine(explanation, pawn, PawnCapacityDefOf.Moving, CombatStatCurves.EliteCooldownMovingWeight);
        AppendCapacityLine(explanation, pawn, PawnCapacityDefOf.Breathing, CombatStatCurves.EliteCooldownBreathingWeight);
        explanation.AppendLine("  score = " + CombatStatCurves.EliteCooldownScore(pawn).ToStringPercent("0.#"));
        explanation.Append("  offset = -75% * (1 - exp(-score / 65%)) = -" + reduction.ToStringPercent("0.#"));
        return explanation.ToString();
    }

    private static void AppendCapacityLine(StringBuilder explanation, Pawn pawn, PawnCapacityDef capacity, float weight)
    {
        float level = CombatStatCurves.CapacityLevel(pawn, capacity);
        float excess = CombatStatCurves.CapacityExcess(pawn, capacity);
        float contribution = CombatStatCurves.CapacityWeightedExcess(pawn, capacity, weight);
        string label = capacity.GetLabelFor(pawn).CapitalizeFirst();
        string impact = "HealthFactorPercentImpact".Translate(weight.ToStringPercent());

        explanation.AppendLine(
            "    " + label + ": " + level.ToStringPercent("0.#")
            + " (" + impact + ", excess " + excess.ToStringPercent("0.#")
            + " -> +" + contribution.ToStringPercent("0.#") + ")");
    }
}
