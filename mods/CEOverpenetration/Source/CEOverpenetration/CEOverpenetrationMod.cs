using Verse;
using HarmonyLib;
using UnityEngine;

namespace CEOverpenetration;

public class CEOverpenetrationMod : Mod
{
    internal static CEOverpenetrationSettings Settings { get; private set; }
    internal static bool LogContinuationsEnabled => Settings?.LogContinuations == true;

    public CEOverpenetrationMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<CEOverpenetrationSettings>();
        var harmony = new Harmony("WhiteGiverMa.CEOverpenetration");
        harmony.PatchAll();
        if (LogContinuationsEnabled)
            Log.Message("[CE Overpenetration] Initialized; continuation logging enabled.");
    }

    public override string SettingsCategory() => "CEOP_SettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.CheckboxLabeled("CEOP_LogContinuations".Translate(), ref Settings.LogContinuations,
            "CEOP_LogContinuationsDescription".Translate());
        listing.End();
    }
}
