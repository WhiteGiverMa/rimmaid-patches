using Verse;

namespace WhiteGiver.SunlightFuelGeneratorFacility;

[StaticConstructorOnStartup]
internal static class SunlightFuelGeneratorBootstrap
{
    static SunlightFuelGeneratorBootstrap()
    {
        SunlightFuelGeneratorSettings? settings =
            LoadedModManager.GetMod<SunlightFuelGeneratorMod>()?.GetSettings<SunlightFuelGeneratorSettings>();
        SunlightFuelGeneratorUtility.Apply(settings);
    }
}
