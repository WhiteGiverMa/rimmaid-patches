using System.Globalization;
using UnityEngine;
using Verse;

namespace WhiteGiver.SunlightFuelGeneratorFacility;

public sealed class SunlightFuelGeneratorMod : Mod
{
    internal static SunlightFuelGeneratorSettings Settings { get; private set; } = new();

    private string powerBuffer = SunlightFuelGeneratorSettings.DefaultPowerOutputWatts.ToString("0.###", CultureInfo.InvariantCulture);
    private string capacityBuffer = SunlightFuelGeneratorSettings.DefaultFuelCapacity.ToString("0.###", CultureInfo.InvariantCulture);
    private string consumptionBuffer = SunlightFuelGeneratorSettings.DefaultFuelConsumptionPerDay.ToString("0.###", CultureInfo.InvariantCulture);

    public SunlightFuelGeneratorMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<SunlightFuelGeneratorSettings>();
        RefreshBuffers();
    }

    public override string SettingsCategory() => "SFGF_Category".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.Label("SFGF_Intro".Translate());
        listing.Gap(6f);
        listing.TextFieldNumericLabeled("SFGF_PowerOutput".Translate(), ref Settings.PowerOutputWatts, ref powerBuffer, 100f, 250000f);
        listing.TextFieldNumericLabeled("SFGF_FuelCapacity".Translate(), ref Settings.FuelCapacity, ref capacityBuffer, 10f, 100000f);
        listing.TextFieldNumericLabeled("SFGF_FuelConsumption".Translate(), ref Settings.FuelConsumptionPerDay, ref consumptionBuffer, 0.01f, 10000f);
        listing.Gap(6f);
        listing.Label("SFGF_Note".Translate());
        listing.Gap(6f);
        if (listing.ButtonText("SFGF_Reset".Translate()))
        {
            Settings.ResetToDefaults();
            RefreshBuffers();
        }
        listing.End();

        SunlightFuelGeneratorUtility.Apply();
    }

    public override void WriteSettings()
    {
        Settings.Clamp();
        base.WriteSettings();
        SunlightFuelGeneratorUtility.Apply();
    }

    private void RefreshBuffers()
    {
        powerBuffer = Settings.PowerOutputWatts.ToString("0.###", CultureInfo.InvariantCulture);
        capacityBuffer = Settings.FuelCapacity.ToString("0.###", CultureInfo.InvariantCulture);
        consumptionBuffer = Settings.FuelConsumptionPerDay.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
