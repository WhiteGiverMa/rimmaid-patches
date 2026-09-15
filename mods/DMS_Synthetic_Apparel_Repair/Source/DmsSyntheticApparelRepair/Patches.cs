using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DmsSyntheticApparelRepair;

[StaticConstructorOnStartup]
public static class Patches
{
	private const string SyntheticPackageId = "aoba.deadmanswitch.synthetic";
	private const string CorePackageId = "aoba.deadmanswitch.core";
	private const string DmsLegionRepairDriverTypeName = "DMS_Legion.JobDriver_MechRepairMech";
	private const string MilianRepairDriverTypeName = "Milira.JobDriver_MilianRepair";
	private const string AutomatroidWeaponCategory = "AutomatroidWeapon";
	private static readonly MethodInfo SingleParameterRepairTick = AccessTools.Method(typeof(MechRepairUtility), nameof(MechRepairUtility.RepairTick), new[] { typeof(Pawn) });
	private static bool gearRepairTransactionsAvailable = true;

	private static readonly HashSet<string> SyntheticDefNames = new()
	{
		"DMS_Mech_Maiden",
		"DMS_Mech_Armiman",
		"DMS_Mech_BigSister",
		"DMS_Mech_Lady"
	};

	static Patches()
	{
		Harmony harmony = new("whitegiverma.dmssyntheticapparelrepair");
		harmony.PatchAll();
		PatchVanillaRepairTransaction(harmony);
		PatchDmsLegionRepairTransaction(harmony);
		PatchMiliraRepairTransaction(harmony);
	}

	[HarmonyPatch(typeof(MechRepairUtility), nameof(MechRepairUtility.CanRepair))]
	private static class MechRepairUtility_CanRepair
	{
		private static void Postfix(Pawn mech, ref bool __result)
		{
			if (!__result && mech.TryGetComp<CompMechRepairable>() != null && HasDamagedGear(mech))
			{
				__result = true;
			}
		}
	}

	private static bool IsSynthetic(Pawn pawn)
	{
		string packageId = pawn.def.modContentPack?.PackageIdPlayerFacing;
		return SyntheticDefNames.Contains(pawn.def.defName)
			&& (string.Equals(packageId, SyntheticPackageId, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(packageId, CorePackageId, StringComparison.OrdinalIgnoreCase));
	}

	private static bool IsMilian(Pawn pawn)
	{
		return pawn.RaceProps.IsMechanoid && pawn.RaceProps.body?.defName == "Milian_Body";
	}

	private static bool IsDmsWeaponMech(Pawn pawn)
	{
		if (pawn.def.modExtensions == null)
		{
			return false;
		}

		foreach (DefModExtension extension in pawn.def.modExtensions)
		{
			string typeName = extension.GetType().FullName;
			if (typeName == "DMS.MechWeaponExtension" || typeName == "Fortified.MechWeaponExtension")
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsDmsMechStandardWeapon(ThingDef weaponDef)
	{
		if (weaponDef.thingCategories == null)
		{
			return false;
		}

		foreach (ThingCategoryDef category in weaponDef.thingCategories)
		{
			if (category.defName == AutomatroidWeaponCategory)
			{
				return true;
			}
		}

		return false;
	}

	private static void PatchVanillaRepairTransaction(Harmony harmony)
	{
		MethodInfo repairTickAction = AccessTools.Method(typeof(JobDriver_RepairMech), "<MakeNewToils>b__10_2");
		if (repairTickAction == null)
		{
			DisableGearRepair("Vanilla repair callback was not found.");
			return;
		}

		harmony.Patch(repairTickAction, transpiler: new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(TranspileVanillaRepairTransaction))));
	}

	private static void PatchDmsLegionRepairTransaction(Harmony harmony)
	{
		Type dmsLegionRepairDriverType = AccessTools.TypeByName(DmsLegionRepairDriverTypeName);
		if (dmsLegionRepairDriverType == null)
		{
			return;
		}

		MethodInfo repairTickAction = AccessTools.Method(dmsLegionRepairDriverType, "<MakeNewToils>b__3_2");
		if (repairTickAction == null)
		{
			DisableGearRepair("DMS Legion repair callback was not found.");
			return;
		}

		harmony.Patch(repairTickAction, transpiler: new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(TranspileDmsLegionRepairTransaction))));
	}

	private static void PatchMiliraRepairTransaction(Harmony harmony)
	{
		Type milianRepairDriverType = AccessTools.TypeByName(MilianRepairDriverTypeName);
		if (milianRepairDriverType == null)
		{
			return;
		}

		MethodInfo repairTickAction = AccessTools.Method(milianRepairDriverType, "<MakeNewToils>b__20_2");
		if (repairTickAction == null
			|| AccessTools.Property(milianRepairDriverType, "energyCostFactor_Target") == null
			|| AccessTools.Property(milianRepairDriverType, "energyCostFactor_Caster") == null)
		{
			DisableGearRepair("Milira repair transaction changed.");
			return;
		}

		harmony.Patch(repairTickAction, transpiler: new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(TranspileMiliraRepairTransaction))));
	}

	private static IEnumerable<CodeInstruction> TranspileVanillaRepairTransaction(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		List<CodeInstruction> code = new(instructions);
		MethodInfo repairTick = AccessTools.Method(typeof(MechRepairUtility), nameof(MechRepairUtility.RepairTick), new[] { typeof(Pawn) });
		int repairTickIndex = code.FindIndex(instruction => Calls(instruction, repairTick));
		if (!TryFindEnergyTransactionStart(code, repairTickIndex, out int transactionStart))
		{
			DisableGearRepair("Vanilla repair transaction changed.");
			return code;
		}

		InsertTransactionGuard(code, transactionStart, repairTickIndex + 1, AccessTools.Method(typeof(Patches), nameof(TryRunVanillaGearTransaction)), false, generator);
		return code;
	}

	private static IEnumerable<CodeInstruction> TranspileDmsLegionRepairTransaction(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		List<CodeInstruction> code = new(instructions);
		MethodInfo repairTick = AccessTools.Method(typeof(MechRepairUtility), nameof(MechRepairUtility.RepairTick), new[] { typeof(Pawn), typeof(int) });
		int repairTickIndex = code.FindIndex(instruction => Calls(instruction, repairTick));
		if (!TryFindEnergyTransactionStart(code, repairTickIndex, out int transactionStart))
		{
			DisableGearRepair("DMS Legion repair transaction changed.");
			return code;
		}

		InsertTransactionGuard(code, transactionStart, repairTickIndex + 1, AccessTools.Method(typeof(Patches), nameof(TryRunDmsLegionGearTransaction)), true, generator);
		return code;
	}

	private static IEnumerable<CodeInstruction> TranspileMiliraRepairTransaction(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		List<CodeInstruction> code = new(instructions);
		MethodInfo repairTick = AccessTools.Method(typeof(MechRepairUtility), nameof(MechRepairUtility.RepairTick), new[] { typeof(Pawn) });
		int repairTickIndex = code.FindIndex(instruction => Calls(instruction, repairTick));
		if (!TryFindMiliraTransactionStart(code, repairTickIndex, out int transactionStart))
		{
			DisableGearRepair("Milira repair transaction changed.");
			return code;
		}

		InsertTransactionGuard(code, transactionStart, repairTickIndex + 1, AccessTools.Method(typeof(Patches), nameof(TryRunMiliraGearTransaction)), false, generator);
		return code;
	}

	private static bool TryFindEnergyTransactionStart(List<CodeInstruction> code, int repairTickIndex, out int transactionStart)
	{
		transactionStart = -1;
		MethodInfo getMech = AccessTools.PropertyGetter(typeof(JobDriver_RepairMech), "Mech");
		if (repairTickIndex < 0 || getMech == null)
		{
			return false;
		}

		for (int index = repairTickIndex - 1; index >= Math.Max(0, repairTickIndex - 20); index--)
		{
			if (index + 3 < repairTickIndex
				&& code[index].opcode == OpCodes.Ldarg_0
				&& Calls(code[index + 1], getMech)
				&& LoadsField(code[index + 2], "needs")
				&& LoadsField(code[index + 3], "energy"))
			{
				transactionStart = index;
				return true;
			}
		}

		return false;
	}

	private static bool TryFindMiliraTransactionStart(List<CodeInstruction> code, int repairTickIndex, out int transactionStart)
	{
		transactionStart = -1;
		if (repairTickIndex < 0)
		{
			return false;
		}

		for (int index = repairTickIndex - 1; index >= 1; index--)
		{
			if (index + 5 < repairTickIndex
				&& code[index].opcode == OpCodes.Ldarg_0
				&& LoadsField(code[index + 1], "pawn")
				&& LoadsField(code[index + 2], "needs")
				&& LoadsField(code[index + 3], "energy")
				&& code[index + 4].opcode == OpCodes.Ldnull
				&& code[index + 5].opcode == OpCodes.Cgt_Un)
			{
				transactionStart = code[index - 1].opcode == OpCodes.Nop ? index - 1 : index;
				return true;
			}
		}

		return false;
	}

	private static void InsertTransactionGuard(List<CodeInstruction> code, int transactionStart, int continuationIndex, MethodInfo transaction, bool hasDelta, ILGenerator generator)
	{
		Label originalTransaction = generator.DefineLabel();
		Label continuation = generator.DefineLabel();
		List<CodeInstruction> guard = new()
		{
			new(OpCodes.Ldarg_0)
		};
		if (hasDelta)
		{
			guard.Add(new CodeInstruction(OpCodes.Ldarg_1));
		}
		guard.Add(new CodeInstruction(OpCodes.Call, transaction));
		guard.Add(new CodeInstruction(OpCodes.Brfalse, originalTransaction));
		guard.Add(new CodeInstruction(OpCodes.Br, continuation));
		guard[0].labels.AddRange(code[transactionStart].labels);
		guard[0].blocks.AddRange(code[transactionStart].blocks);
		code[transactionStart].labels.Clear();
		code[transactionStart].blocks.Clear();
		code[transactionStart].labels.Add(originalTransaction);
		code[continuationIndex].labels.Add(continuation);
		code.InsertRange(transactionStart, guard);
	}

	private static bool Calls(CodeInstruction instruction, MethodInfo method)
	{
		return method != null && (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && Equals(instruction.operand, method);
	}

	private static bool LoadsField(CodeInstruction instruction, string fieldName)
	{
		return instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.Name == fieldName;
	}

	private static bool IsStoreLocal(CodeInstruction instruction)
	{
		return instruction.opcode == OpCodes.Stloc
			|| instruction.opcode == OpCodes.Stloc_0
			|| instruction.opcode == OpCodes.Stloc_1
			|| instruction.opcode == OpCodes.Stloc_2
			|| instruction.opcode == OpCodes.Stloc_3
			|| instruction.opcode == OpCodes.Stloc_S;
	}

	private static bool TryRunVanillaGearTransaction(JobDriver_RepairMech repairJob)
	{
		Pawn mech = repairJob.job.GetTarget(TargetIndex.A).Thing as Pawn;
		return TryRunGearRepairTransaction(mech, 1, 1f, false);
	}

	private static bool TryRunDmsLegionGearTransaction(JobDriver_RepairMech repairJob, int delta)
	{
		Pawn mech = repairJob.job.GetTarget(TargetIndex.A).Thing as Pawn;
		if (delta > 0 && mech != null && IsMilian(mech) && TryRunMilianRepairTransaction(repairJob.pawn, mech, delta))
		{
			return true;
		}

		return TryRunGearRepairTransaction(mech, delta, delta, true);
	}

	private static bool TryRunMiliraGearTransaction(JobDriver repairJob)
	{
		Pawn mech = repairJob.job.GetTarget(TargetIndex.A).Thing as Pawn;
		if (SingleParameterRepairTick == null)
		{
			DisableGearRepair("Vanilla single-parameter repair callback was not found.");
			return false;
		}
		if (!TryCreateGearRepairPlan(mech, 1, out GearRepairPlan plan))
		{
			return false;
		}
		if (!TryGetMilianRepairEnergyFactors(repairJob, out float targetFactor, out float casterFactor))
		{
			DisableGearRepair("Milira energy factors could not be read.");
			return false;
		}

		if (plan.RunVanilla)
		{
			ConsumeRepairEnergy(repairJob.pawn, casterFactor);
		}
		RunGearRepairPlan(mech, plan, targetFactor, 1, false);
		return true;
	}

	private static bool TryRunMilianRepairTransaction(Pawn repairer, Pawn mech, int delta)
	{
		if (SingleParameterRepairTick == null || !TryGetMilianRepairEnergyFactors(repairer, out float targetFactor, out float casterFactor))
		{
			return false;
		}

		for (int repair = 0; repair < delta; repair++)
		{
			ConsumeRepairEnergy(repairer, casterFactor);
			ConsumeRepairEnergy(mech, targetFactor);
			SingleParameterRepairTick.Invoke(null, new object[] { mech });
		}

		return true;
	}

	private static bool TryGetMilianRepairEnergyFactors(Pawn repairer, out float targetFactor, out float casterFactor)
	{
		targetFactor = 0f;
		casterFactor = 0f;
		try
		{
			Type driverType = AccessTools.TypeByName(MilianRepairDriverTypeName);
			if (driverType == null || repairer == null)
			{
				return false;
			}

			JobDriver driver = (JobDriver)Activator.CreateInstance(driverType);
			driver.pawn = repairer;
			return TryGetMilianRepairEnergyFactors(driver, out targetFactor, out casterFactor);
		}
		catch (Exception exception)
		{
			Log.WarningOnce($"[DMS Synthetic Apparel Repair] Could not use Milira's repair energy rules ({exception.GetType().Name}); DMS Legion's original repair rule will be used.", 1968354021);
			return false;
		}
	}

	private static bool TryGetMilianRepairEnergyFactors(JobDriver repairJob, out float targetFactor, out float casterFactor)
	{
		targetFactor = 0f;
		casterFactor = 0f;
		try
		{
			Type driverType = AccessTools.TypeByName(MilianRepairDriverTypeName);
			if (driverType == null || repairJob == null || !driverType.IsInstanceOfType(repairJob))
			{
				return false;
			}

			PropertyInfo targetFactorProperty = AccessTools.Property(driverType, "energyCostFactor_Target");
			PropertyInfo casterFactorProperty = AccessTools.Property(driverType, "energyCostFactor_Caster");
			if (targetFactorProperty == null || casterFactorProperty == null)
			{
				return false;
			}

			targetFactor = (float)targetFactorProperty.GetValue(repairJob);
			casterFactor = (float)casterFactorProperty.GetValue(repairJob);
			return true;
		}
		catch (Exception exception)
		{
			Log.WarningOnce($"[DMS Synthetic Apparel Repair] Could not read Milira's repair energy rules ({exception.GetType().Name}); gear repair was disabled.", 1912252936);
			return false;
		}
	}

	private static void DisableGearRepair(string reason)
	{
		if (!gearRepairTransactionsAvailable)
		{
			return;
		}

		gearRepairTransactionsAvailable = false;
		Log.Error($"[DMS Synthetic Apparel Repair] {reason} Gear repair was disabled to avoid an unsafe energy transaction.");
	}

	private static bool HasDamagedGear(Pawn pawn)
	{
		if (!gearRepairTransactionsAvailable)
		{
			return false;
		}

		if (ApparelRepairEnabled && IsSynthetic(pawn))
		{
			foreach (Apparel apparel in pawn.apparel.WornApparel)
			{
				if (CanRepair(apparel))
				{
					return true;
				}
			}
		}

		if (!WeaponRepairEnabled || !IsDmsWeaponMech(pawn))
		{
			return false;
		}

		foreach (ThingWithComps weapon in pawn.equipment.AllEquipmentListForReading)
		{
			if (CanRepair(weapon))
			{
				return true;
			}
		}

		return false;
	}

	private sealed class GearRepairPlan
	{
		public readonly List<GearRepair> Repairs = new();
		public bool RunVanilla;
		public float GearRepairUnits;
	}

	private readonly struct GearRepair
	{
		public readonly Thing Gear;
		public readonly int Amount;

		public GearRepair(Thing gear, int amount)
		{
			Gear = gear;
			Amount = amount;
		}
	}

	private static bool TryRunGearRepairTransaction(Pawn mech, int repairAmount, float vanillaRepairUnits, bool useDeltaRepair)
	{
		if (!useDeltaRepair && SingleParameterRepairTick == null)
		{
			DisableGearRepair("Vanilla single-parameter repair callback was not found.");
			return false;
		}
		if (!TryCreateGearRepairPlan(mech, repairAmount, out GearRepairPlan plan))
		{
			return false;
		}

		RunGearRepairPlan(mech, plan, vanillaRepairUnits, repairAmount, useDeltaRepair);
		return true;
	}

	private static bool TryCreateGearRepairPlan(Pawn mech, int repairAmount, out GearRepairPlan plan)
	{
		plan = null;
		if (!gearRepairTransactionsAvailable || mech == null || repairAmount <= 0)
		{
			return false;
		}

		bool repairsApparel = ApparelRepairEnabled && IsSynthetic(mech);
		bool repairsWeapons = WeaponRepairEnabled && IsDmsWeaponMech(mech);
		if (!repairsApparel && !repairsWeapons)
		{
			return false;
		}

		GearRepairPlan result = new()
		{
			RunVanilla = HasRepairableBodyDamage(mech) || IsMissingRequiredWeapon(mech)
		};
		if (repairsApparel)
		{
			foreach (Apparel apparel in mech.apparel.WornApparel)
			{
				AddGearRepair(result, apparel, repairAmount, ApparelRepairEnergyFraction);
			}
		}
		if (repairsWeapons)
		{
			foreach (ThingWithComps weapon in mech.equipment.AllEquipmentListForReading)
			{
				AddGearRepair(result, weapon, repairAmount, IsDmsMechStandardWeapon(weapon.def) ? StandardWeaponRepairEnergyFraction : WeaponRepairEnergyFraction);
			}
		}

		if (result.Repairs.Count == 0)
		{
			return false;
		}

		plan = result;
		return true;
	}

	private static void AddGearRepair(GearRepairPlan plan, Thing gear, int repairAmount, float repairUnitsPerHitPoint)
	{
		if (!CanRepair(gear))
		{
			return;
		}

		int repairedAmount = Math.Min(repairAmount, gear.MaxHitPoints - gear.HitPoints);
		if (repairedAmount <= 0)
		{
			return;
		}

		plan.Repairs.Add(new GearRepair(gear, repairedAmount));
		plan.GearRepairUnits += repairedAmount * repairUnitsPerHitPoint;
	}

	private static void RunGearRepairPlan(Pawn mech, GearRepairPlan plan, float vanillaRepairUnits, int repairAmount, bool useDeltaRepair)
	{
		float targetRepairUnits = (plan.RunVanilla ? vanillaRepairUnits : 0f) + plan.GearRepairUnits;
		ConsumeRepairEnergy(mech, targetRepairUnits);
		foreach (GearRepair repair in plan.Repairs)
		{
			repair.Gear.HitPoints += repair.Amount;
		}

		if (!plan.RunVanilla)
		{
			return;
		}

		if (useDeltaRepair)
		{
			MechRepairUtility.RepairTick(mech, repairAmount);
		}
		else
		{
			SingleParameterRepairTick.Invoke(null, new object[] { mech });
		}
	}

	private static void ConsumeRepairEnergy(Pawn pawn, float repairUnits)
	{
		if (pawn == null || pawn.needs.energy == null || repairUnits <= 0f)
		{
			return;
		}

		pawn.needs.energy.CurLevel -= pawn.GetStatValue(StatDefOf.MechEnergyLossPerHP) * repairUnits;
	}

	private static bool ApparelRepairEnabled => DmsSyntheticApparelRepairMod.Settings?.ApparelRepairEnabled ?? true;

	private static bool WeaponRepairEnabled => DmsSyntheticApparelRepairMod.Settings?.WeaponRepairEnabled ?? true;

	private static float ApparelRepairEnergyFraction => DmsSyntheticApparelRepairMod.Settings?.ApparelRepairEnergyFraction ?? RepairSettings.DefaultEnergyFraction;

	private static float WeaponRepairEnergyFraction => DmsSyntheticApparelRepairMod.Settings?.WeaponRepairEnergyFraction ?? RepairSettings.DefaultEnergyFraction;

	private static float StandardWeaponRepairEnergyFraction => DmsSyntheticApparelRepairMod.Settings?.StandardWeaponRepairEnergyFraction ?? 0f;

	private static bool CanRepair(Thing gear)
	{
		return gear.def.useHitPoints && gear.HitPoints < gear.MaxHitPoints;
	}

	private static bool HasRepairableBodyDamage(Pawn pawn)
	{
		foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
		{
			if (hediff is Hediff_Injury || hediff is Hediff_MissingPart)
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsMissingRequiredWeapon(Pawn pawn)
	{
		List<string> requiredTags = pawn.kindDef.weaponTags;
		if (requiredTags.NullOrEmpty())
		{
			return false;
		}

		foreach (ThingWithComps equipment in pawn.equipment.AllEquipmentListForReading)
		{
			List<string> equippedTags = equipment.def.weaponTags;
			if (equippedTags.NullOrEmpty())
			{
				continue;
			}

			foreach (string equippedTag in equippedTags)
			{
				if (requiredTags.Contains(equippedTag))
				{
					return false;
				}
			}
		}

		return true;
	}
}
