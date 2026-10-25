using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace WhiteGiver.SunlightFuelGeneratorFacility;

internal static class SunlightFuelGeneratorUtility
{
    internal const string DefName = "WhiteGiver_SunlightFuelGeneratorFacility";

    // Keep the full-tank blast radius at the Milira generator's scale (10 fuel * 1.25 = 12.5).
    private const float FullTankExplosionRadius = 12.5f;

    private static readonly FieldInfo? BasePowerConsumptionField =
        typeof(CompProperties_Power).GetField("basePowerConsumption", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? FuelField =
        typeof(CompRefuelable).GetField("fuel", BindingFlags.Instance | BindingFlags.NonPublic);

    private static (float Power, float Capacity, float Consumption)? lastApplied;

    internal static void Apply(SunlightFuelGeneratorSettings? settings = null)
    {
        settings ??= SunlightFuelGeneratorMod.Settings;
        if (settings == null)
        {
            return;
        }

        settings.Clamp();
        var key = (settings.PowerOutputWatts, settings.FuelCapacity, settings.FuelConsumptionPerDay);
        if (lastApplied == key)
        {
            return;
        }
        lastApplied = key;

        ThingDef? def = DefDatabase<ThingDef>.GetNamedSilentFail(DefName);
        if (def == null)
        {
            Log.Warning($"[SunlightFuelGeneratorFacility] Def {DefName} not found; settings were not applied.");
            return;
        }

        CompProperties_Power? power = def.GetCompProperties<CompProperties_Power>();
        CompProperties_Refuelable? refuelable = def.GetCompProperties<CompProperties_Refuelable>();
        if (power == null || refuelable == null)
        {
            Log.Error($"[SunlightFuelGeneratorFacility] Def {DefName} is missing its power or refuelable comp.");
            return;
        }
        if (BasePowerConsumptionField == null)
        {
            Log.Error("[SunlightFuelGeneratorFacility] CompProperties_Power.basePowerConsumption was not found; power output cannot be changed.");
            return;
        }

        BasePowerConsumptionField.SetValue(power, -settings.PowerOutputWatts);
        refuelable.fuelCapacity = settings.FuelCapacity;
        refuelable.fuelConsumptionRate = settings.FuelConsumptionPerDay;
        refuelable.initialConfigurableTargetFuelLevel = settings.FuelCapacity;

        CompProperties_Explosive? explosive = def.GetCompProperties<CompProperties_Explosive>();
        if (explosive != null)
        {
            explosive.explosiveExpandPerFuel = FullTankExplosionRadius / settings.FuelCapacity;
        }

        ApplyToExisting(def, settings);
        Log.Message(
            $"[SunlightFuelGeneratorFacility] Applied {settings.PowerOutputWatts:0.##} W, " +
            $"{settings.FuelCapacity:0.##} fuel capacity, {settings.FuelConsumptionPerDay:0.###} fuel/day.");
    }

    private static void ApplyToExisting(ThingDef def, SunlightFuelGeneratorSettings settings)
    {
        List<Map>? maps = Find.Maps;
        if (maps == null)
        {
            return;
        }

        for (int i = 0; i < maps.Count; i++)
        {
            List<Thing> things = maps[i].listerThings.ThingsOfDef(def);
            for (int j = 0; j < things.Count; j++)
            {
                Thing thing = things[j];

                CompRefuelable? existingFuel = thing.TryGetComp<CompRefuelable>();
                if (existingFuel != null)
                {
                    if (existingFuel.TargetFuelLevel > settings.FuelCapacity)
                    {
                        existingFuel.TargetFuelLevel = settings.FuelCapacity;
                    }
                    if (FuelField != null && FuelField.GetValue(existingFuel) is float fuel && fuel > settings.FuelCapacity)
                    {
                        FuelField.SetValue(existingFuel, settings.FuelCapacity);
                    }
                }

                thing.TryGetComp<CompPowerPlant>()?.UpdateDesiredPowerOutput();
            }
        }
    }
}
