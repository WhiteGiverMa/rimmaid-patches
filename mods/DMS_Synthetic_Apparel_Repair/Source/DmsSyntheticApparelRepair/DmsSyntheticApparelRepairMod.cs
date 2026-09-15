using UnityEngine;
using Verse;

namespace DmsSyntheticApparelRepair;

public sealed class DmsSyntheticApparelRepairMod : Mod
{
	public static RepairSettings Settings { get; private set; }

	public DmsSyntheticApparelRepairMod(ModContentPack content) : base(content)
	{
		Settings = GetSettings<RepairSettings>();
	}

	public override void DoSettingsWindowContents(Rect inRect)
	{
		Settings.DoWindowContents(inRect);
	}

	public override string SettingsCategory()
	{
		return "DMS_SyntheticApparelRepair_SettingsCategory".Translate();
	}
}
