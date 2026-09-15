using UnityEngine;
using Verse;

namespace DmsSyntheticApparelRepair;

public sealed class RepairSettings : ModSettings
{
	public const float DefaultEnergyFraction = 0.1f;

	private const float MinEnergyFraction = 0f;
	private const float MaxEnergyFraction = 1f;

	private bool apparelRepairEnabled = true;
	private bool weaponRepairEnabled = true;
	private float apparelRepairEnergyFraction = DefaultEnergyFraction;
	private float weaponRepairEnergyFraction = DefaultEnergyFraction;
	private float standardWeaponRepairEnergyFraction;

	public bool ApparelRepairEnabled => apparelRepairEnabled;
	public bool WeaponRepairEnabled => weaponRepairEnabled;
	public float ApparelRepairEnergyFraction => apparelRepairEnergyFraction;
	public float WeaponRepairEnergyFraction => weaponRepairEnergyFraction;
	public float StandardWeaponRepairEnergyFraction => standardWeaponRepairEnergyFraction;

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
	}

	public override void ExposeData()
	{
		Scribe_Values.Look(ref apparelRepairEnabled, nameof(apparelRepairEnabled), true);
		Scribe_Values.Look(ref weaponRepairEnabled, nameof(weaponRepairEnabled), true);
		Scribe_Values.Look(ref apparelRepairEnergyFraction, nameof(apparelRepairEnergyFraction), DefaultEnergyFraction);
		Scribe_Values.Look(ref weaponRepairEnergyFraction, nameof(weaponRepairEnergyFraction), DefaultEnergyFraction);
		Scribe_Values.Look(ref standardWeaponRepairEnergyFraction, nameof(standardWeaponRepairEnergyFraction), 0f);
		apparelRepairEnergyFraction = NormalizeEnergyFraction(apparelRepairEnergyFraction);
		weaponRepairEnergyFraction = NormalizeEnergyFraction(weaponRepairEnergyFraction);
		standardWeaponRepairEnergyFraction = NormalizeEnergyFraction(standardWeaponRepairEnergyFraction);
	}

	private static float NormalizeEnergyFraction(float value)
	{
		return Mathf.Clamp(Mathf.Round(value * 100f) / 100f, MinEnergyFraction, MaxEnergyFraction);
	}
}
