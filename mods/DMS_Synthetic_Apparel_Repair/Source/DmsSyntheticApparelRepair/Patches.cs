using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DmsSyntheticApparelRepair;

[StaticConstructorOnStartup]
public static class Patches
{
	private const string SyntheticPackageId = "aoba.deadmanswitch.synthetic";
	private const string CorePackageId = "aoba.deadmanswitch.core";
	private const string DmsLegionPackageId = "srxf.deadmanswitch.legion";
	private const string DmsLegionRepairDriverTypeName = "DMS_Legion.JobDriver_MechRepairMech";
	private const string DmsLegionRepairCallbackName = "<MakeNewToils>b__4_3";
	private const string DmsLegionLegacyRepairCallbackName = "<MakeNewToils>b__3_2";
	private const string MilianRepairDriverTypeName = "Milira.JobDriver_MilianRepair";
	private const string MilianRepairCallbackName = "<MakeNewToils>b__20_2";
	private const string AutomatroidWeaponCategory = "AutomatroidWeapon";
	private const string MechApparelCompClassName = "Fortified.CompMechApparel";
	private const string TerraBodyTypeCompClassName = "DMS_Legion.CompTerraBodyType";
	private const string MechApparelCompPropsClassName = "Fortified.CompProperties_MechApparel";
	private const string TerraBodyTypeCompPropsClassName = "DMS_Legion.CompProperties_TerraBodyType";
	private const string RepairTickCallbackPrefix = "<MakeNewToils>b__";
	private static readonly string[] EligiblePackageIds = { SyntheticPackageId, CorePackageId, DmsLegionPackageId };
	private static readonly MethodInfo SingleParameterRepairTick = AccessTools.Method(typeof(MechRepairUtility), nameof(MechRepairUtility.RepairTick), new[] { typeof(Pawn) });
	private static readonly MethodInfo DeltaRepairTick = AccessTools.Method(typeof(MechRepairUtility), nameof(MechRepairUtility.RepairTick), new[] { typeof(Pawn), typeof(int) });
	private static readonly Dictionary<Thing, float> GearRepairRemainders = new();
	private static readonly Dictionary<short, OpCode> OpCodesByValue = BuildOpCodesByValue();
	private static bool vanillaRepairTransactionAvailable = true;
	private static bool dmsLegionRepairTransactionAvailable = true;
	private static bool miliraRepairTransactionAvailable = true;

	private static readonly HashSet<string> SyntheticDefNames = new()
	{
		"DMS_Mech_Maiden",
		"DMS_Mech_Armiman",
		"DMS_Mech_BigSister",
		"DMS_Mech_Lady",
		"DMSL_Mech_Terra",
		"DMS_Mech_Frogman"
	};

	static Patches()
	{
		Harmony harmony = new("whitegiverma.dmssyntheticapparelrepair");
		harmony.PatchAll();
		PatchVanillaRepairTransaction(harmony);
		PatchDmsLegionRepairTransaction(harmony);
		PatchMiliraRepairTransaction(harmony);
		try
		{
			EnsureSyntheticRepairableComps();
		}
		catch (Exception exception)
		{
			Log.Error($"[DMS Synthetic Apparel Repair] Could not normalise CompProperties_MechRepairable: {exception}");
		}
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

	private static bool IsSynthetic(ThingDef def)
	{
		if (def == null || def.race?.IsMechanoid != true || !IsEligibleDmsPackage(def))
		{
			return false;
		}

		return SyntheticDefNames.Contains(def.defName) || HasMechApparelComp(def);
	}

	private static bool IsSynthetic(Pawn pawn)
	{
		if (pawn?.def == null)
		{
			return false;
		}
		if (IsSynthetic(pawn.def))
		{
			return true;
		}
		if (pawn.def.race?.IsMechanoid != true || !IsEligibleDmsPackage(pawn.def))
		{
			return false;
		}

		foreach (ThingComp comp in pawn.AllComps)
		{
			string fullName = comp?.GetType().FullName;
			if (fullName == MechApparelCompClassName || fullName == TerraBodyTypeCompClassName)
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsEligibleDmsPackage(ThingDef def)
	{
		string packageId = def.modContentPack?.PackageIdPlayerFacing;
		if (packageId == null)
		{
			return false;
		}

		foreach (string eligiblePackageId in EligiblePackageIds)
		{
			if (string.Equals(packageId, eligiblePackageId, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasMechApparelComp(ThingDef def)
	{
		if (def.comps == null)
		{
			return false;
		}

		foreach (CompProperties compProps in def.comps)
		{
			if (compProps == null)
			{
				continue;
			}
			string compClassName = compProps.compClass?.FullName;
			if (compClassName == MechApparelCompClassName || compClassName == TerraBodyTypeCompClassName)
			{
				return true;
			}
			string compPropsClassName = compProps.GetType().FullName;
			if (compPropsClassName == MechApparelCompPropsClassName || compPropsClassName == TerraBodyTypeCompPropsClassName)
			{
				return true;
			}
		}

		return false;
	}

	private static void EnsureSyntheticRepairableComps()
	{
		int inspected = 0;
		int deduplicated = 0;
		foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
		{
			if (!IsSynthetic(def))
			{
				continue;
			}

			inspected++;
			List<CompProperties> comps = def.comps;
			if (comps == null)
			{
				comps = new List<CompProperties>();
				def.comps = comps;
			}

			int existing = 0;
			for (int index = 0; index < comps.Count; index++)
			{
				if (comps[index] is CompProperties_MechRepairable)
				{
					existing++;
				}
			}
			if (existing == 1)
			{
				continue;
			}

			try
			{
				if (existing == 0)
				{
					CompProperties_MechRepairable compProps = new();
					comps.Add(compProps);
					compProps.ResolveReferences(def);
					continue;
				}

				bool kept = false;
				for (int index = comps.Count - 1; index >= 0; index--)
				{
					if (!(comps[index] is CompProperties_MechRepairable))
					{
						continue;
					}
					if (kept)
					{
						comps.RemoveAt(index);
						deduplicated++;
					}
					else
					{
						kept = true;
					}
				}
			}
			catch (Exception exception)
			{
				Log.Error($"[DMS Synthetic Apparel Repair] Could not normalise CompProperties_MechRepairable on {def.defName}: {exception}");
			}
		}

		if (deduplicated > 0)
		{
			Log.Warning($"[DMS Synthetic Apparel Repair] Removed {deduplicated} duplicate CompProperties_MechRepairable entries across {inspected} synthetic mech defs.");
		}
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
			DisableVanillaRepairTransaction("Vanilla repair callback was not found.");
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

		MethodInfo repairTickAction = FindDmsLegionRepairTickAction(dmsLegionRepairDriverType);
		if (repairTickAction == null)
		{
			DisableDmsLegionRepairTransaction("DMS Legion repair callback was not found.");
			return;
		}

		harmony.Patch(repairTickAction, transpiler: new HarmonyMethod(AccessTools.Method(typeof(Patches), nameof(TranspileDmsLegionRepairTransaction))));
	}

	private static MethodInfo FindDmsLegionRepairTickAction(Type dmsLegionRepairDriverType)
	{
		MethodInfo repairTickAction = AccessTools.Method(dmsLegionRepairDriverType, DmsLegionRepairCallbackName)
			?? AccessTools.Method(dmsLegionRepairDriverType, DmsLegionLegacyRepairCallbackName);
		if (IsRepairTickAction(repairTickAction))
		{
			return repairTickAction;
		}

		foreach (MethodInfo method in AccessTools.GetDeclaredMethods(dmsLegionRepairDriverType))
		{
			if (IsRepairTickAction(method) && CallsRepairTick(method))
			{
				return method;
			}
		}

		return null;
	}

	private static bool IsRepairTickAction(MethodInfo method)
	{
		if (method == null
			|| method.ReturnType != typeof(void)
			|| !method.Name.StartsWith(RepairTickCallbackPrefix, StringComparison.Ordinal))
		{
			return false;
		}

		ParameterInfo[] parameters = method.GetParameters();
		return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
	}

	private static void PatchMiliraRepairTransaction(Harmony harmony)
	{
		Type milianRepairDriverType = AccessTools.TypeByName(MilianRepairDriverTypeName);
		if (milianRepairDriverType == null)
		{
			return;
		}

		MethodInfo repairTickAction = AccessTools.Method(milianRepairDriverType, MilianRepairCallbackName);
		if (repairTickAction == null
			|| AccessTools.Property(milianRepairDriverType, "energyCostFactor_Target") == null
			|| AccessTools.Property(milianRepairDriverType, "energyCostFactor_Caster") == null)
		{
			DisableMiliraRepairTransaction("Milira repair transaction changed.");
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
			DisableVanillaRepairTransaction("Vanilla repair transaction changed.");
			return code;
		}

		InsertTransactionGuard(code, transactionStart, repairTickIndex + 1, AccessTools.Method(typeof(Patches), nameof(TryRunVanillaGearTransaction)), false, generator);
		return code;
	}

	private static IEnumerable<CodeInstruction> TranspileDmsLegionRepairTransaction(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		List<CodeInstruction> code = new(instructions);
		MethodInfo repairTick = SingleParameterRepairTick;
		int repairTickIndex = code.FindIndex(instruction => Calls(instruction, repairTick));
		bool hasDelta = repairTickIndex < 0;
		if (hasDelta)
		{
			repairTick = DeltaRepairTick;
			repairTickIndex = code.FindIndex(instruction => Calls(instruction, repairTick));
		}
		if (!TryFindEnergyTransactionStart(code, repairTickIndex, out int transactionStart))
		{
			DisableDmsLegionRepairTransaction("DMS Legion repair transaction changed.");
			return code;
		}

		MethodInfo transaction = AccessTools.Method(typeof(Patches), hasDelta ? nameof(TryRunDmsLegionGearTransaction) : nameof(TryRunDmsLegionSingleGearTransaction));
		InsertTransactionGuard(code, transactionStart, repairTickIndex + 1, transaction, hasDelta, generator);
		return code;
	}

	private static IEnumerable<CodeInstruction> TranspileMiliraRepairTransaction(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
	{
		List<CodeInstruction> code = new(instructions);
		MethodInfo repairTick = SingleParameterRepairTick;
		int repairTickIndex = code.FindIndex(instruction => Calls(instruction, repairTick));
		if (!TryFindMiliraTransactionStart(code, repairTickIndex, out int transactionStart))
		{
			DisableMiliraRepairTransaction("Milira repair transaction changed.");
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

	private static Dictionary<short, OpCode> BuildOpCodesByValue()
	{
		Dictionary<short, OpCode> result = new();
		foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.GetValue(null) is OpCode opcode)
			{
				result[opcode.Value] = opcode;
			}
		}

		return result;
	}

	private static bool CallsRepairTick(MethodInfo method)
	{
		MethodBody body = method.GetMethodBody();
		byte[] il = body?.GetILAsByteArray();
		if (il == null)
		{
			return false;
		}

		Type[] typeArguments = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
		Type[] methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
		int index = 0;
		while (index < il.Length)
		{
			int value = il[index++];
			if (value == 0xFE)
			{
				if (index >= il.Length)
				{
					return false;
				}
				value = 0xFE00 | il[index++];
			}
			if (!OpCodesByValue.TryGetValue(unchecked((short)value), out OpCode opcode))
			{
				return false;
			}
			if ((opcode == OpCodes.Call || opcode == OpCodes.Callvirt) && index + 4 <= il.Length)
			{
				int token = BitConverter.ToInt32(il, index);
				try
				{
					MethodBase target = method.Module.ResolveMethod(token, typeArguments, methodArguments);
					if (target != null && target.DeclaringType == typeof(MechRepairUtility) && target.Name == nameof(MechRepairUtility.RepairTick))
					{
						return true;
					}
				}
				catch (ArgumentException)
				{
				}
				catch (BadImageFormatException)
				{
				}
				catch (MissingMemberException)
				{
				}
			}

			index += OperandSize(opcode.OperandType, il, index);
		}

		return false;
	}

	private static int OperandSize(OperandType operandType, byte[] il, int index)
	{
		switch (operandType)
		{
			case OperandType.InlineNone:
				return 0;
			case OperandType.ShortInlineBrTarget:
			case OperandType.ShortInlineI:
			case OperandType.ShortInlineVar:
				return 1;
			case OperandType.InlineVar:
				return 2;
			case OperandType.InlineI8:
			case OperandType.InlineR:
				return 8;
			case OperandType.InlineSwitch:
				return index + 4 <= il.Length ? 4 + 4 * BitConverter.ToInt32(il, index) : il.Length - index;
			default:
				return 4;
		}
	}

	private static void DisableVanillaRepairTransaction(string reason)
	{
		vanillaRepairTransactionAvailable = false;
		LogDisable(reason, "Vanilla");
	}

	private static void DisableDmsLegionRepairTransaction(string reason)
	{
		dmsLegionRepairTransactionAvailable = false;
		LogDisable(reason, "DMS Legion");
	}

	private static void DisableMiliraRepairTransaction(string reason)
	{
		miliraRepairTransactionAvailable = false;
		LogDisable(reason, "Milira");
	}

	private static void LogDisable(string reason, string driverName)
	{
		Log.Error($"[DMS Synthetic Apparel Repair] {reason} Gear repair was disabled for the {driverName} repair driver only, to avoid an unsafe energy transaction. Other repair drivers are unaffected.");
	}

	private static bool AnyGearRepairTransactionAvailable => vanillaRepairTransactionAvailable || dmsLegionRepairTransactionAvailable || miliraRepairTransactionAvailable;

	private static bool TryRunVanillaGearTransaction(JobDriver_RepairMech repairJob)
	{
		if (!vanillaRepairTransactionAvailable)
		{
			return false;
		}

		Pawn mech = repairJob.job.GetTarget(TargetIndex.A).Thing as Pawn;
		return TryRunGearRepairTransaction(mech, 1, 1f, false);
	}

	private static bool TryRunDmsLegionGearTransaction(JobDriver_RepairMech repairJob, int delta)
	{
		if (!dmsLegionRepairTransactionAvailable)
		{
			return false;
		}

		Pawn mech = repairJob.job.GetTarget(TargetIndex.A).Thing as Pawn;
		if (delta > 0 && mech != null && IsMilian(mech) && TryRunMilianRepairTransaction(repairJob.pawn, mech, delta))
		{
			return true;
		}

		return TryRunGearRepairTransaction(mech, delta, delta, true);
	}

	private static bool TryRunDmsLegionSingleGearTransaction(JobDriver_RepairMech repairJob)
	{
		if (!dmsLegionRepairTransactionAvailable)
		{
			return false;
		}

		Pawn mech = repairJob.job.GetTarget(TargetIndex.A).Thing as Pawn;
		if (mech != null && IsMilian(mech) && TryRunMilianRepairTransaction(repairJob.pawn, mech, 1))
		{
			return true;
		}

		return TryRunGearRepairTransaction(mech, 1, 1f, false);
	}

	private static bool TryRunMiliraGearTransaction(JobDriver repairJob)
	{
		if (!miliraRepairTransactionAvailable)
		{
			return false;
		}

		Pawn mech = repairJob.job.GetTarget(TargetIndex.A).Thing as Pawn;
		if (!TryCreateGearRepairPlan(mech, 1, out GearRepairPlan plan))
		{
			return false;
		}
		if (!TryGetMilianRepairEnergyFactors(repairJob, out float targetFactor, out float casterFactor))
		{
			DisableMiliraRepairTransaction("Milira energy factors could not be read.");
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
			Log.WarningOnce($"[DMS Synthetic Apparel Repair] Could not read Milira's repair energy rules ({exception.GetType().Name}); Milira gear repair was disabled.", 1912252936);
			return false;
		}
	}

	private static bool HasDamagedGear(Pawn pawn)
	{
		if (!AnyGearRepairTransactionAvailable)
		{
			return false;
		}

		if (ApparelRepairEnabled && ApparelRepairSpeedMultiplier > 0f && IsSynthetic(pawn))
		{
			foreach (Apparel apparel in pawn.apparel.WornApparel)
			{
				if (CanRepair(apparel))
				{
					return true;
				}
			}
		}

		if (!WeaponRepairEnabled || WeaponRepairSpeedMultiplier <= 0f || !IsDmsWeaponMech(pawn))
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
		if (!AnyGearRepairTransactionAvailable || mech == null || repairAmount <= 0)
		{
			return false;
		}

		bool repairsApparel = ApparelRepairEnabled && ApparelRepairSpeedMultiplier > 0f && IsSynthetic(mech);
		bool repairsWeapons = WeaponRepairEnabled && WeaponRepairSpeedMultiplier > 0f && IsDmsWeaponMech(mech);
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
				AddGearRepair(result, apparel, repairAmount, ApparelRepairEnergyFraction, ApparelRepairSpeedMultiplier);
			}
		}
		if (repairsWeapons)
		{
			foreach (ThingWithComps weapon in mech.equipment.AllEquipmentListForReading)
			{
				AddGearRepair(result, weapon, repairAmount, IsDmsMechStandardWeapon(weapon.def) ? StandardWeaponRepairEnergyFraction : WeaponRepairEnergyFraction, WeaponRepairSpeedMultiplier);
			}
		}

		if (result.Repairs.Count == 0)
		{
			return false;
		}

		plan = result;
		return true;
	}

	private static void AddGearRepair(GearRepairPlan plan, Thing gear, int repairAmount, float repairUnitsPerHitPoint, float speedMultiplier)
	{
		if (!CanRepair(gear))
		{
			GearRepairRemainders.Remove(gear);
			return;
		}

		float totalRepair = repairAmount * speedMultiplier;
		if (GearRepairRemainders.TryGetValue(gear, out float remainder))
		{
			totalRepair += remainder;
		}

		int repairedAmount = Math.Min(Mathf.FloorToInt(totalRepair + 0.0001f), gear.MaxHitPoints - gear.HitPoints);
		if (repairedAmount > 0)
		{
			plan.Repairs.Add(new GearRepair(gear, repairedAmount));
			plan.GearRepairUnits += repairedAmount * repairUnitsPerHitPoint;
		}

		if (gear.HitPoints + repairedAmount >= gear.MaxHitPoints)
		{
			GearRepairRemainders.Remove(gear);
		}
		else
		{
			GearRepairRemainders[gear] = Mathf.Clamp(totalRepair - repairedAmount, 0f, 0.9999f);
		}
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
			// The delta overload is obsolete in 1.7 but is still the actual 1.6 repair entry point.
#pragma warning disable CS0612
			MechRepairUtility.RepairTick(mech, repairAmount);
#pragma warning restore CS0612
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

	private static float ApparelRepairSpeedMultiplier => DmsSyntheticApparelRepairMod.Settings?.ApparelRepairSpeedMultiplier ?? RepairSettings.DefaultSpeedMultiplier;

	private static float WeaponRepairSpeedMultiplier => DmsSyntheticApparelRepairMod.Settings?.WeaponRepairSpeedMultiplier ?? RepairSettings.DefaultSpeedMultiplier;

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
