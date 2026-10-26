using UnityEngine;
using Verse;

namespace WhiteGiver.ReinforcedConcreteHeavyBridge;

public sealed class BridgeMod : Mod
{
    internal static BridgeSettings Settings { get; private set; } = new();
    private string costBuffer;
    private Vector2 scrollPosition;
    private float contentHeight = 600f;

    public BridgeMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<BridgeSettings>();
        costBuffer = Settings.MaterialCost.ToString();
        AutoBridgePatch.Apply();
    }

    public override string SettingsCategory() => "RCHB_Category".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var viewRect = new Rect(0, 0, inRect.width - 20f, contentHeight);
        Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
        var listing = new Listing_Standard();
        listing.Begin(viewRect);
        listing.Label("RCHB_Intro".Translate());
        listing.Gap();
        listing.TextFieldNumericLabeled("RCHB_Cost".Translate(), ref Settings.MaterialCost, ref costBuffer, 1, 10000);
        listing.Label("RCHB_CostNote".Translate());
        if (listing.ButtonText("RCHB_Reset".Translate()))
        {
            Settings.MaterialCost = BridgeSettings.DefaultCost;
            costBuffer = Settings.MaterialCost.ToString();
        }
        listing.GapLine();
        listing.Label("RCHB_AutoBridge".Translate());
        if (listing.RadioButton("RCHB_AutoVanilla".Translate(), !Settings.AutoUseReinforcedConcrete))
        {
            Settings.AutoUseReinforcedConcrete = false;
        }
        if (listing.RadioButton("RCHB_AutoConcrete".Translate(), Settings.AutoUseReinforcedConcrete))
        {
            Settings.AutoUseReinforcedConcrete = true;
        }
        listing.Label("RCHB_AutoNote".Translate());
        listing.GapLine();
        listing.Label("RCHB_ConvertNote".Translate());
        if (listing.ButtonText("RCHB_Convert".Translate()))
        {
            BridgeConversion.Confirm();
        }
        contentHeight = listing.CurHeight;
        listing.End();
        Widgets.EndScrollView();
        BridgeCost.Apply(Settings);
    }

    public override void WriteSettings()
    {
        Settings.Clamp();
        base.WriteSettings();
        BridgeCost.Apply(Settings);
    }
}
