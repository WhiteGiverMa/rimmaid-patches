using UnityEngine;
using Verse;

namespace DmsSyntheticApparelRepair;

public sealed class RepairSettings : ModSettings
{
	public const float DefaultEnergyFraction = 0.1f;
	public const float DefaultSpeedMultiplier = 1f;

	private const float MinEnergyFraction = 0f;
	private const float MaxEnergyFraction = 1f;
	private const float MinSpeedMultiplier = 0f;
	private const float MaxSpeedMultiplier = 5f;

	private bool apparelRepairEnabled = true;
	private bool weaponRepairEnabled = true;
	private float apparelRepairEnergyFraction = DefaultEnergyFraction;
	private float weaponRepairEnergyFraction = DefaultEnergyFraction;
	private float standardWeaponRepairEnergyFraction;
	private float apparelRepairSpeedMultiplier = DefaultSpeedMultiplier;
	private float weaponRepairSpeedMultiplier = DefaultSpeedMultiplier;

	public bool ApparelRepairEnabled => apparelRepairEnabled;
	public bool WeaponRepairEnabled => weaponRepairEnabled;
	public float ApparelRepairEnergyFraction => apparelRepairEnergyFraction;
	public float WeaponRepairEnergyFraction => weaponRepairEnergyFraction;
	public float StandardWeaponRepairEnergyFraction => standardWeaponRepairEnergyFraction;
	public float ApparelRepairSpeedMultiplier => apparelRepairSpeedMultiplier;
	public float WeaponRepairSpeedMultiplier => weaponRepairSpeedMultiplier;

	public void DoWindowContents(Rect inRect)
	{
		Listing_Standard list = new();
		list.Begin(inRect.ContractedBy(10f));
		list.Label("DMS_SyntheticApparelRepair_SettingsHeader".Translate());
		list.GapLine();
		list.CheckboxLabeled(
			"DMS_SyntheticApparelRepair_EnableApparelRepair".Translate(),
			ref apparelRepairEnabled,
			"DMS_SyntheticApparelRepair_EnableApparelRepairDesc".Translate());
		apparelRepairEnergyFraction = NormalizeEnergyFraction(list.SliderLabeled(
			"DMS_SyntheticApparelRepair_ApparelEnergyFraction".Translate(apparelRepairEnergyFraction.ToStringPercent()),
			apparelRepairEnergyFraction,
			MinEnergyFraction,
			MaxEnergyFraction,
			labelPct: 0.6f,
			tooltip: "DMS_SyntheticApparelRepair_ApparelEnergyFractionDesc".Translate()));
		apparelRepairSpeedMultiplier = NormalizeSpeedMultiplier(list.SliderLabeled(
			"DMS_SyntheticApparelRepair_ApparelSpeedMultiplier".Translate(apparelRepairSpeedMultiplier.ToStringPercent()),
			apparelRepairSpeedMultiplier,
			MinSpeedMultiplier,
			MaxSpeedMultiplier,
			labelPct: 0.6f,
			tooltip: "DMS_SyntheticApparelRepair_ApparelSpeedMultiplierDesc".Translate()));
		list.GapLine();
		list.CheckboxLabeled(
			"DMS_SyntheticApparelRepair_EnableWeaponRepair".Translate(),
			ref weaponRepairEnabled,
			"DMS_SyntheticApparelRepair_EnableWeaponRepairDesc".Translate());
		weaponRepairEnergyFraction = NormalizeEnergyFraction(list.SliderLabeled(
			"DMS_SyntheticApparelRepair_WeaponEnergyFraction".Translate(weaponRepairEnergyFraction.ToStringPercent()),
			weaponRepairEnergyFraction,
			MinEnergyFraction,
			MaxEnergyFraction,
			labelPct: 0.6f,
			tooltip: "DMS_SyntheticApparelRepair_WeaponEnergyFractionDesc".Translate()));
		standardWeaponRepairEnergyFraction = NormalizeEnergyFraction(list.SliderLabeled(
			"DMS_SyntheticApparelRepair_StandardWeaponEnergyFraction".Translate(standardWeaponRepairEnergyFraction.ToStringPercent()),
			standardWeaponRepairEnergyFraction,
			MinEnergyFraction,
			MaxEnergyFraction,
			labelPct: 0.6f,
			tooltip: "DMS_SyntheticApparelRepair_StandardWeaponEnergyFractionDesc".Translate()));
		weaponRepairSpeedMultiplier = NormalizeSpeedMultiplier(list.SliderLabeled(
			"DMS_SyntheticApparelRepair_WeaponSpeedMultiplier".Translate(weaponRepairSpeedMultiplier.ToStringPercent()),
			weaponRepairSpeedMultiplier,
			MinSpeedMultiplier,
			MaxSpeedMultiplier,
			labelPct: 0.6f,
			tooltip: "DMS_SyntheticApparelRepair_WeaponSpeedMultiplierDesc".Translate()));
		list.GapLine();
		list.Label("DMS_SyntheticApparelRepair_SettingsNote".Translate());
		if (list.ButtonText("DMS_SyntheticApparelRepair_ResetToDefaults".Translate()))
		{
			ResetToDefaults();
		}
		list.End();
	}

	public void ResetToDefaults()
	{
		apparelRepairEnabled = true;
		weaponRepairEnabled = true;
		apparelRepairEnergyFraction = DefaultEnergyFraction;
		weaponRepairEnergyFraction = DefaultEnergyFraction;
		standardWeaponRepairEnergyFraction = 0f;
		apparelRepairSpeedMultiplier = DefaultSpeedMultiplier;
		weaponRepairSpeedMultiplier = DefaultSpeedMultiplier;
	}

	public override void ExposeData()
	{
		Scribe_Values.Look(ref apparelRepairEnabled, nameof(apparelRepairEnabled), true);
		Scribe_Values.Look(ref weaponRepairEnabled, nameof(weaponRepairEnabled), true);
		Scribe_Values.Look(ref apparelRepairEnergyFraction, nameof(apparelRepairEnergyFraction), DefaultEnergyFraction);
		Scribe_Values.Look(ref weaponRepairEnergyFraction, nameof(weaponRepairEnergyFraction), DefaultEnergyFraction);
		Scribe_Values.Look(ref standardWeaponRepairEnergyFraction, nameof(standardWeaponRepairEnergyFraction), 0f);
		Scribe_Values.Look(ref apparelRepairSpeedMultiplier, nameof(apparelRepairSpeedMultiplier), DefaultSpeedMultiplier);
		Scribe_Values.Look(ref weaponRepairSpeedMultiplier, nameof(weaponRepairSpeedMultiplier), DefaultSpeedMultiplier);
		apparelRepairEnergyFraction = NormalizeEnergyFraction(apparelRepairEnergyFraction);
		weaponRepairEnergyFraction = NormalizeEnergyFraction(weaponRepairEnergyFraction);
		standardWeaponRepairEnergyFraction = NormalizeEnergyFraction(standardWeaponRepairEnergyFraction);
		apparelRepairSpeedMultiplier = NormalizeSpeedMultiplier(apparelRepairSpeedMultiplier);
		weaponRepairSpeedMultiplier = NormalizeSpeedMultiplier(weaponRepairSpeedMultiplier);
	}

	private static float NormalizeEnergyFraction(float value)
	{
		return Mathf.Clamp(Mathf.Round(value * 100f) / 100f, MinEnergyFraction, MaxEnergyFraction);
	}

	private static float NormalizeSpeedMultiplier(float value)
	{
		return Mathf.Clamp(Mathf.Round(value * 20f) / 20f, MinSpeedMultiplier, MaxSpeedMultiplier);
	}
}
