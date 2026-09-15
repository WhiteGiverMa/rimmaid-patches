using System;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using Verse;

namespace Meidocho.CEFix.MiliraFloatUnitElevation;

[StaticConstructorOnStartup]
public static class FloatUnitElevationPatch
{
    private const float FlightHeight = 0.5f;
    private const string FloatUnitBodyDef = "Milira_FloatUnit";

    static FloatUnitElevationPatch()
    {
        try
        {
            MethodInfo? calculateHeightRange = AccessTools.Method(
                typeof(CollisionVertical),
                "CalculateHeightRange",
                new[] { typeof(Thing), typeof(FloatRange).MakeByRefType(), typeof(float).MakeByRefType() });

            if (calculateHeightRange is null)
            {
                Log.Error("[CE_Fix_MiliraFloatUnitElevation] CE's collision-height method was not found; no patch applied.");
                return;
            }

            new Harmony("meidocho.ce_fix_milira_float_unit_elevation").Patch(
                calculateHeightRange,
                postfix: new HarmonyMethod(typeof(FloatUnitElevationPatch), nameof(ApplyFloatUnitElevation)));

            Log.Message("[CE_Fix_MiliraFloatUnitElevation] Applied +0.5 CE elevation to Milira float units.");
        }
        catch (Exception exception)
        {
            Log.Error($"[CE_Fix_MiliraFloatUnitElevation] Failed to apply patch: {exception}");
        }
    }

    public static void ApplyFloatUnitElevation(Thing thing, ref FloatRange heightRange, ref float shotHeight)
    {
        Pawn? pawn = thing as Pawn;
        if (pawn is null || pawn.Flying || pawn.RaceProps?.body?.defName != FloatUnitBodyDef)
            return;

        heightRange = new FloatRange(heightRange.min + FlightHeight, heightRange.max + FlightHeight);
        shotHeight += FlightHeight;
    }
}
