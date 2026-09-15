using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using WRMegaCorp;

namespace Fix_WRMegaCorp_QuestExposeData;

/// <summary>
/// 修复 WRMegaCorp ChoiceLetter_CSC.ExposeData() 中的 NRE。
///
/// 根因: ChoiceLetter_CSC.ExposeData() 在调用 base.ExposeData()
/// （其中 ChoiceLetter 用 Scribe_References.Look 正确处理 quest 引用）
/// 之后又额外调用 Scribe_Deep.Look&lt;Quest&gt; 对同一个 quest 字段做深序列化。
///
/// 加载存档时，Scribe_Deep 尝试从 quest 引用
/// 引用节点创建新 Quest 实例并调用 Quest.ExposeData()，
/// 但 XML 节点缺少子元素导致 NullReferenceException。
///
/// 修复方式: Harmony Transpiler 移除 Scribe_Deep.Look 调用，
/// 保留 base.ExposeData() 对 quest 的正确引用处理。
/// </summary>
[StaticConstructorOnStartup]
public static class Patch_QuestExposeData
{
	static Patch_QuestExposeData()
	{
		try
		{
			var harmony = new Harmony("meidocho.fix_wrmegacorp_quest_exposedata");

			var original = AccessTools.Method(typeof(ChoiceLetter_CSC), "ExposeData");
			var transpiler = new HarmonyMethod(typeof(Patch_QuestExposeData), nameof(Transpiler));

			harmony.Patch(original, transpiler: transpiler);

			Log.Message("[Fix_WRMegaCorp_QuestExposeData] Patch applied — removed Scribe_Deep.Look<Quest> from ChoiceLetter_CSC.ExposeData().");
		}
		catch (Exception ex)
		{
			Log.Error($"[Fix_WRMegaCorp_QuestExposeData] Failed to apply patch: {ex}");
		}
	}

	/// <summary>
	/// 从方法 IL 中移除 Scribe_Deep.Look&lt;Quest&gt; 调用及其参数准备指令。
	///
	/// 目标 IL 模式 (5 条指令):
	///   ldarg.0                              // this
	///   ldflda    ChoiceLetter::quest         // ref base.quest
	///   ldstr     "quest"                     // label
	///   call      Array.Empty&lt;object&gt;()       // ctorArgs
	///   call      Scribe_Deep.Look&lt;Quest&gt;     // 移除目标
	/// </summary>
	public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		var codes = instructions.ToList();

		for (int i = codes.Count - 1; i >= 4; i--)
		{
			var instr = codes[i];

			// 匹配 Scribe_Deep.Look 调用 (通用方法调用，判定 DeclaringType 和泛型参数)
			if (instr.opcode == OpCodes.Call
				&& instr.operand is MethodInfo mi
				&& mi.DeclaringType == typeof(Scribe_Deep)
				&& mi.IsGenericMethod
				&& mi.GetGenericArguments().Length == 1
				&& mi.GetGenericArguments()[0] == typeof(Quest))
			{
				// 验证前一条指令是否为 Array.Empty<object>()
				if (i >= 1 && codes[i - 1].opcode == OpCodes.Call
					&& codes[i - 1].operand is MethodInfo prevMi
					&& prevMi.Name == "Empty"
					&& prevMi.DeclaringType == typeof(Array))
				{
					// 移除 5 条指令: ldarg.0, ldflda, ldstr, call Array.Empty, call Scribe_Deep.Look
					codes.RemoveRange(i - 4, 5);
					Log.Message("[Fix_WRMegaCorp_QuestExposeData] Removed Scribe_Deep.Look<Quest> call from IL.");
				}
				break;
			}
		}

		return codes.AsEnumerable();
	}
}
