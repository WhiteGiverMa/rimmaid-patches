using System;
using UnityEngine;
using Verse;

namespace WhiteGiver.CicadaVolumeControl;

public sealed class CicadaVolumeMod : Mod
{
    internal static CicadaVolumeSettings Settings { get; private set; } = new();

    public CicadaVolumeMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<CicadaVolumeSettings>();
    }

    public override string SettingsCategory() => "CVC_Category".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.Label("CVC_Intro".Translate());
        listing.Gap();
        listing.Label("CVC_Volume".Translate(Settings.VolumePercent));
        Settings.VolumePercent = (int)Math.Round(listing.Slider(Settings.VolumePercent, 0, 100));
        listing.Gap();
        listing.Label("CVC_Note".Translate());
        listing.Gap();
        if (listing.ButtonText("CVC_Reset".Translate()))
            Settings.VolumePercent = 100;
        listing.End();
    }
}
