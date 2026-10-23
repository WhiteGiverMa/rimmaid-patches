using System;
using Verse;

namespace WhiteGiver.CicadaVolumeControl;

public sealed class CicadaVolumeSettings : ModSettings
{
    public int VolumePercent = 100;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref VolumePercent, "volumePercent", 100);
        VolumePercent = Math.Clamp(VolumePercent, 0, 100);
    }
}
