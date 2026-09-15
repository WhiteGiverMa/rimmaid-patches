using System;
using System.Reflection;
using CombatExtended;
using CombatExtended.AI;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace CE_Fix_IsHunkering_NRE;

/// <summary>
/// CE 的战斗/战术系统补丁集合。
/// 
/// Fix 1: IsHunkering NRE
///   根因: CompSuppressable.IsHunkering 在 pawn.CurJob 为 null 时访问 .def 引发 NRE。
///   修复: Prefix 完全替代 CompTacticalManager.TryGiveTacticalJobs，用 try-catch 保护 IsHunkering。
/// 
/// Fix 2: TendSelf vs TendPatient reservation 冲突
///   根因: CompTend.TryGiveTacticalJob 分配 TendSelf 时不检查目标是否已被其他 pawn 预约（如在做 TendPatient），
///   导致 TryMakePreToilReservations 中 LogCouldNotReserveError 红字刷屏。
///   修复: Prefix 在 CompTend 创建 job 前遍历 ReservationManager 检查预约冲突。
/// </summary>
[StaticConstructorOnStartup]
public static class Patch_IsHunkering
{
	static Patch_IsHunkering()
	{
		try
		{
			var harmony = new Harmony("meidocho.ce_fix_ishunkering_nre");
			harmony.Patch(
				original: AccessTools.Method(typeof(CompTacticalManager), "TryGiveTacticalJobs"),
				prefix: new HarmonyMethod(typeof(Patch_IsHunkering), nameof(Prefix_TryGiveTacticalJobs))
			);
			harmony.Patch(
				original: AccessTools.Method(typeof(CompTend), "TryGiveTacticalJob"),
				prefix: new HarmonyMethod(typeof(Patch_IsHunkering), nameof(Prefix_CompTend_TryGiveTacticalJob))
			);
			Log.Message("[CE_Fix_IsHunkering_NRE] Patches applied (TryGiveTacticalJobs + CompTend.TryGiveTacticalJob).");
		}
		catch (Exception ex)
		{
			Log.Error($"[CE_Fix_IsHunkering_NRE] Failed to apply patches: {ex}");
		}
	}

	// --- Fix 1: IsHunkering NRE ---

	/// <summary>
	/// 原版 TryGiveTacticalJobs():
	///   if (CompSuppressable == null || CompSuppressable.IsHunkering || !SelPawn.Spawned || SelPawn.Downed) return;
	///   foreach (var comp in TacticalComps) { Job job = comp.TryGiveTacticalJob(); if (job != null) { SelPawn.jobs.StartJob(job, ...); return; } }
	/// 
	/// 返回 false 始终跳过原版方法（prefix 已完整处理逻辑）。
	/// </summary>
	public static bool Prefix_TryGiveTacticalJobs(CompTacticalManager __instance)
	{
		// --- Guard: 完全等价原版，但用 try-catch 保护 IsHunkering ---
		var cs = __instance.CompSuppressable;
		if (cs == null)
			return false;

		bool isHunkering;
		try
		{
			isHunkering = cs.IsHunkering;
		}
		catch (NullReferenceException)
		{
			// pawn.CurJob 为 null → IsHunkering 恢复路径中 pawn.CurJob.def 炸了。
			// 安全降级：不蹲伏，继续执行原方法体。
			isHunkering = false;
		}

		if (isHunkering || !__instance.SelPawn.Spawned || __instance.SelPawn.Downed)
			return false; // guard 触发，跳过

		// --- 战术任务分配循环（原版方法体） ---
		foreach (ICompTactics comp in __instance.TacticalComps)
		{
			Job job = comp.TryGiveTacticalJob();
			if (job != null)
			{
				__instance.SelPawn.jobs.StartJob(job, JobCondition.InterruptForced);
				return false; // job 已分配，结束
			}
		}

		return false; // 始终跳过原版（prefix 已完整处理）
	}

	// --- Fix 2: TendSelf reservation conflict ---

	private static readonly FieldInfo _lastTendJobCheckedAtField =
		AccessTools.Field(typeof(CompTend), "lastTendJobCheckedAt");

	/// <summary>
	/// 在 CompTend 尝试创建 TendSelf job 之前，检查目标 pawn 是否已被其他 pawn 预约。
	/// 如果已被预约（如正在被 TendPatient），跳过本次分配并更新冷却计数器。
	/// </summary>
	public static bool Prefix_CompTend_TryGiveTacticalJob(CompTend __instance, ref Job? __result)
	{
		var pawn = __instance.SelPawn;
		if (pawn == null) return true;

		var map = pawn.Map;
		if (map == null) return true;

		var reservations = map.reservationManager.ReservationsReadOnly;
		for (int i = 0; i < reservations.Count; i++)
		{
			if (reservations[i].Target == pawn && reservations[i].Claimant != pawn)
			{
				// 已有其他 pawn 预约了该目标（如在做 TendPatient），跳过 TendSelf
				_lastTendJobCheckedAtField.SetValue(__instance, GenTicks.TicksGame);
				__result = null;
				return false;
			}
		}
		return true;
	}
}
