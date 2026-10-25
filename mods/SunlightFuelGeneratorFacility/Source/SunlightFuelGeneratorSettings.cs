using System;
using Verse;

namespace WhiteGiver.SunlightFuelGeneratorFacility;

public sealed class SunlightFuelGeneratorSettings : ModSettings
{
    public const float DefaultPowerOutputWatts = 7500f;
    public const float DefaultFuelCapacity = 400f;
    public const float DefaultFuelConsumptionPerDay = 2.22f;

    public float PowerOutputWatts = DefaultPowerOutputWatts;
    public float FuelCapacity = DefaultFuelCapacity;
    public float FuelConsumptionPerDay = DefaultFuelConsumptionPerDay;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref PowerOutputWatts, "powerOutputWatts", DefaultPowerOutputWatts);
        Scribe_Values.Look(ref FuelCapacity, "fuelCapacity", DefaultFuelCapacity);
        Scribe_Values.Look(ref FuelConsumptionPerDay, "fuelConsumptionPerDay", DefaultFuelConsumptionPerDay);
        Clamp();
    }

    public void ResetToDefaults()
    {
        PowerOutputWatts = DefaultPowerOutputWatts;
        FuelCapacity = DefaultFuelCapacity;
        FuelConsumptionPerDay = DefaultFuelConsumptionPerDay;
    }

    public void Clamp()
    {
        PowerOutputWatts = Math.Clamp(PowerOutputWatts, 100f, 250000f);
        FuelCapacity = Math.Clamp(FuelCapacity, 10f, 100000f);
        FuelConsumptionPerDay = Math.Clamp(FuelConsumptionPerDay, 0.01f, 10000f);
    }
}
