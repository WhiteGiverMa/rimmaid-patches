using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace WhiteGiver.MTOEWYStatPerfFix;

/// <summary>The exact set of StatDefs MTOEWY's IdealExclusiveStatPatch can ever change.</summary>
internal enum StatId : byte
{
    None = 0,
    GlobalLearningFactor,
    SocialImpact,
    ImmunityGainSpeed,
    RestFallRateFactor,
    GeneralLaborSpeed,
    ResearchSpeed,
    MedicalTendQuality,
    CarryingCapacity,
    MentalBreakThreshold,
    MoveSpeed,
    ConstructionSpeed,
    JoyFallRateFactor,
    AimingDelayFactor,
    MeleeDodgeChance,
}

/// <summary>
/// Lazily resolved StatDef -&gt; <see cref="StatId"/> table. Rebuilt only when the
/// StatDef database size changes or <see cref="StatDefOf.MoveSpeed"/> is rebound,
/// so the hot path is one reference-keyed dictionary lookup.
/// </summary>
internal static class StatDispatch
{
    internal const string GlobalLearningFactor = "GlobalLearningFactor";
    internal const string SocialImpact = "SocialImpact";
    internal const string ImmunityGainSpeed = "ImmunityGainSpeed";
    internal const string RestFallRateFactor = "RestFallRateFactor";
    internal const string GeneralLaborSpeed = "GeneralLaborSpeed";
    internal const string ResearchSpeed = "ResearchSpeed";
    internal const string MedicalTendQuality = "MedicalTendQuality";
    internal const string CarryingCapacity = "CarryingCapacity";
    internal const string MentalBreakThreshold = "MentalBreakThreshold";
    internal const string MoveSpeed = "MoveSpeed";
    internal const string ConstructionSpeed = "ConstructionSpeed";
    internal const string JoyFallRateFactor = "JoyFallRateFactor";
    internal const string AimingDelayFactor = "AimingDelayFactor";
    internal const string MeleeDodgeChance = "MeleeDodgeChance";

    private static readonly string[] DefNames =
    {
        GlobalLearningFactor,
        SocialImpact,
        ImmunityGainSpeed,
        RestFallRateFactor,
        GeneralLaborSpeed,
        ResearchSpeed,
        MedicalTendQuality,
        CarryingCapacity,
        MentalBreakThreshold,
        MoveSpeed,
        ConstructionSpeed,
        JoyFallRateFactor,
        AimingDelayFactor,
        MeleeDodgeChance,
    };

    private static readonly StatId[] DefIds =
    {
        StatId.GlobalLearningFactor,
        StatId.SocialImpact,
        StatId.ImmunityGainSpeed,
        StatId.RestFallRateFactor,
        StatId.GeneralLaborSpeed,
        StatId.ResearchSpeed,
        StatId.MedicalTendQuality,
        StatId.CarryingCapacity,
        StatId.MentalBreakThreshold,
        StatId.MoveSpeed,
        StatId.ConstructionSpeed,
        StatId.JoyFallRateFactor,
        StatId.AimingDelayFactor,
        StatId.MeleeDodgeChance,
    };

    private static Dictionary<StatDef, StatId>? index;
    private static int builtDefCount = -1;
    private static StatDef? builtFirstDef;
    private static StatDef? builtMoveSpeed;

    internal static bool TryGet(StatDef stat, out StatId id)
    {
        Dictionary<StatDef, StatId>? map = index;
        List<StatDef> all = DefDatabase<StatDef>.AllDefsListForReading;
        if (map == null
            || builtDefCount != all.Count
            || !ReferenceEquals(builtFirstDef, all.Count > 0 ? all[0] : null)
            || !ReferenceEquals(builtMoveSpeed, StatDefOf.MoveSpeed))
        {
            // The first-def reference also changes when the DefDatabase is reloaded
            // (dev mode), which keeps the table from going stale with identical counts.
            map = Rebuild(all);
        }

        return map.TryGetValue(stat, out id);
    }

    private static Dictionary<StatDef, StatId> Rebuild(List<StatDef> all)
    {
        Dictionary<StatDef, StatId> map = new(DefNames.Length, ReferenceComparer.Instance);
        for (int i = 0; i < DefNames.Length; i++)
        {
            StatDef? def = DefDatabase<StatDef>.GetNamedSilentFail(DefNames[i]);
            if (def != null)
            {
                map[def] = DefIds[i];
            }
        }

        index = map;
        builtDefCount = all.Count;
        builtFirstDef = all.Count > 0 ? all[0] : null;
        builtMoveSpeed = StatDefOf.MoveSpeed;
        return map;
    }

    /// <summary>Reference identity, because the table is keyed by the live Def instances.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<StatDef>
    {
        internal static readonly ReferenceComparer Instance = new();

        public bool Equals(StatDef? x, StatDef? y) => ReferenceEquals(x, y);

        public int GetHashCode(StatDef obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
