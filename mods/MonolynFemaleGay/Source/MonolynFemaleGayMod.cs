using UnityEngine;
using Verse;

namespace WhiteGiver.MonolynFemaleGay;

public sealed class MonolynFemaleGaySettings : ModSettings
{
    public bool forceFemaleGay = true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref forceFemaleGay, "forceFemaleGay", true);
    }
}

public sealed class MonolynFemaleGayMod : Mod
{
    internal static MonolynFemaleGayMod? Instance { get; private set; }
    internal static MonolynFemaleGaySettings? Settings { get; private set; }

    public MonolynFemaleGayMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<MonolynFemaleGaySettings>();
        Instance = this;
    }

    public override string SettingsCategory() => "WGMonolynFemaleGay.SettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        if (MonolynFemaleGayPatcher.Ready)
        {
            listing.Label("WGMonolynFemaleGay.Explanation".Translate());
            listing.Gap();
            MonolynFemaleGayPatcher.DrawToggle(listing);
            listing.Label(MonolynFemaleGayPatcher.StatusKey.Translate());
        }
        else
        {
            listing.Label("WGMonolynFemaleGay.Unavailable".Translate());
        }
        listing.End();
    }
}
