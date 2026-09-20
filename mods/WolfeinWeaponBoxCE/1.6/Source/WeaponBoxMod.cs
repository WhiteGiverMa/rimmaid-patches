using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meidocho.WolfeinWeaponBoxCE;

public sealed class WeaponBoxMod : Mod
{
    private readonly WeaponBoxSettings settings;
    private ThingDef weaponBox;
    private StatDef carryBulk;
    private StatDef carryWeight;

    public WeaponBoxMod(ModContentPack content) : base(content)
    {
        settings = GetSettings<WeaponBoxSettings>();
        LongEventHandler.ExecuteWhenFinished(Initialize);
    }

    private void Initialize()
    {
        if (!ModsConfig.IsActive("ceteam.combatextended"))
            return;

        weaponBox = DefDatabase<ThingDef>.GetNamedSilentFail("Wolfein_WeaponCase");
        carryBulk = DefDatabase<StatDef>.GetNamedSilentFail("CarryBulk");
        carryWeight = DefDatabase<StatDef>.GetNamedSilentFail("CarryWeight");
        if (weaponBox == null || carryBulk == null || carryWeight == null)
        {
            Log.Warning("[Wolfein Weapon Box CE] Required defs are missing; no changes applied. Check Wolfein Race / CE versions.");
            return;
        }

        ApplySettings();
        Log.Message($"[Wolfein Weapon Box CE] Applied: CarryBulk +{settings.BulkBonus}, CarryWeight +{settings.WeightBonus}.");
    }

    private void ApplySettings()
    {
        if (weaponBox == null || carryBulk == null || carryWeight == null)
            return;

        weaponBox.equippedStatOffsets ??= new List<StatModifier>();
        SetOffset(carryBulk, settings.BulkBonus);
        SetOffset(carryWeight, settings.WeightBonus);
        // ponytail: CE reads capacity live; only offset changes, not Mass/WornBulk, need no inventory refresh.
    }

    private void SetOffset(StatDef stat, int value)
    {
        weaponBox.equippedStatOffsets.RemoveAll(modifier => modifier.stat == stat);
        weaponBox.equippedStatOffsets.Add(new StatModifier { stat = stat, value = value });
    }

    public override string SettingsCategory() => "WWBCE_Category".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        int previousBulk = settings.BulkBonus;
        int previousWeight = settings.WeightBonus;
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.Label("WWBCE_Intro".Translate());
        listing.Gap();
        listing.Label("WWBCE_Bulk".Translate(settings.BulkBonus));
        settings.BulkBonus = (int)Math.Round(listing.Slider(settings.BulkBonus, 0, WeaponBoxSettings.MaxBonus));
        listing.Gap();
        listing.Label("WWBCE_Weight".Translate(settings.WeightBonus));
        settings.WeightBonus = (int)Math.Round(listing.Slider(settings.WeightBonus, 0, WeaponBoxSettings.MaxBonus));
        listing.Gap();
        listing.Label("WWBCE_Note".Translate());
        if (weaponBox == null || carryBulk == null || carryWeight == null)
            listing.Label("WWBCE_Inactive".Translate());
        listing.Gap();
        if (listing.ButtonText("WWBCE_Reset".Translate()))
        {
            settings.BulkBonus = WeaponBoxSettings.DefaultBulk;
            settings.WeightBonus = 0;
        }
        listing.End();

        if (settings.BulkBonus != previousBulk || settings.WeightBonus != previousWeight)
            ApplySettings();
    }

    public override void WriteSettings()
    {
        ApplySettings();
        base.WriteSettings();
    }
}
