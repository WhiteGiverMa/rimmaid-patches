using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using WRMegaCorp;

namespace Fix_WRMegaCorp_QuestPartCSC;

/// <summary>
/// 注册 Harmony 补丁。修复逻辑在 <see cref="QuestPartCscLoadFix"/>。
/// </summary>
[StaticConstructorOnStartup]
public static class Patch_QuestPartCSC
{
    static Patch_QuestPartCSC()
    {
        try
        {
            var harmony = new Harmony("whitegiver.fix_wrmegacorp_questpartcsc");

            MethodInfo? createInstance = AccessTools.Method(
                typeof(ScribeExtractor),
                "CreateInstance",
                new[] { typeof(Type), typeof(object[]) });

            if (createInstance == null)
                throw new MissingMethodException("ScribeExtractor.CreateInstance(Type, object[]) not found.");

            harmony.Patch(
                createInstance,
                prefix: new HarmonyMethod(typeof(QuestPartCscLoadFix), nameof(QuestPartCscLoadFix.CreateInstance_Prefix)));

            MethodInfo? exposeData = AccessTools.Method(typeof(QuestPart_CSC), nameof(QuestPart_CSC.ExposeData));
            if (exposeData == null)
                throw new MissingMethodException("WRMegaCorp.QuestPart_CSC.ExposeData not found.");

            harmony.Patch(
                exposeData,
                postfix: new HarmonyMethod(typeof(QuestPartCscLoadFix), nameof(QuestPartCscLoadFix.ExposeData_Postfix)));

            MethodInfo? loadGame = AccessTools.Method(typeof(Game), nameof(Game.LoadGame));
            if (loadGame == null)
                throw new MissingMethodException("Verse.Game.LoadGame not found.");

            harmony.Patch(
                loadGame,
                postfix: new HarmonyMethod(typeof(QuestPartCscLoadFix), nameof(QuestPartCscLoadFix.GameLoadGame_Postfix)));

            Log.Message("[Fix_WRMegaCorp_QuestPartCSC] Patches applied.");
        }
        catch (Exception ex)
        {
            Log.Error($"[Fix_WRMegaCorp_QuestPartCSC] Failed to apply patch: {ex}");
        }
    }
}

/// <summary>
/// 修复 1.7 读档时 WRMegaCorp.QuestPart_CSC 无法反序列化的 MissingMethodException。
///
/// 根因:
///   RimWorld 1.7 的 Quest.ExposeData 用
///   Scribe_Collections.Look(ref parts, "parts", LookMode.Deep)
///   加载 quest parts，不传 ctorArgs；ScribeExtractor 因此用无参构造函数
///   实例化每个 part。WRMegaCorp 的 QuestPart_CSC 只有一个五参数构造函数，
///   于是 Activator.CreateInstance 抛 MissingMethodException，该 part 变成 null
///   并被 Quest.ExposeData 从 parts 里移除。
///
/// 修复:
///   在 ScribeExtractor.CreateInstance 执行前，仅当目标是 QuestPart_CSC
///   且 ctorArgs 为空时，直接用其原本就存在的五参数构造函数创建实例。
///   随后 ExposeData 会用存档里的 shipChunkCount / signalTag / map 覆盖字段。
///
/// 附带:
///   QuestPart_CSC.ExposeData 没有保存 pos，读档后 pos 永远是 (0,0,0)。
///   Postfix 补上 pos 的序列化；旧存档没有 pos 节点时给一个地图上的落点，
///   避免接受任务后飞船残骸掉到地图角落。
/// </summary>
public static class QuestPartCscLoadFix
{
    private static bool injectedOnce;

    /// <summary>
    /// ScribeExtractor.CreateInstance 在 ctorArgs 为空时用无参构造函数实例化。
    /// QuestPart_CSC 只有五参数构造函数，这里直接返回用其真实构造函数创建的对象。
    /// </summary>
    public static bool CreateInstance_Prefix(Type type, object[] ctorArgs, ref IExposable __result)
    {
        if (type != typeof(QuestPart_CSC))
            return true;

        if (ctorArgs != null && ctorArgs.Length > 0)
            return true;

        __result = new QuestPart_CSC(null!, null!, 0, IntVec3.Zero, null!);

        if (injectedOnce)
            return false;

        injectedOnce = true;
        Log.Message("[Fix_WRMegaCorp_QuestPartCSC] Created QuestPart_CSC with its real five-arg constructor.");
        return false;
    }

    /// <summary>
    /// 补上 QuestPart_CSC.pos 的序列化。读档时 map 引用还没解析，
    /// 落点推迟到 <see cref="GameLoadGame_Postfix"/> 处理。
    /// </summary>
    public static void ExposeData_Postfix(QuestPart_CSC __instance)
    {
        Scribe_Values.Look(ref __instance.pos, "pos");

        if (Scribe.mode != LoadSaveMode.LoadingVars)
            return;

        Log.Message(
            "[Fix_WRMegaCorp_QuestPartCSC] Loaded QuestPart_CSC: "
            + $"shipChunkCount={__instance.shipChunkCount}, "
            + $"signalTag={__instance.signalTag ?? "null"}, "
            + $"pos={__instance.pos}");
    }

    /// <summary>
    /// 存档加载完成后 map 引用已解析。旧存档没有 pos 节点（读成 0,0,0）时
    /// 给一个地图上的随机可站立落点，避免接受任务后残骸掉到地图角落。
    /// </summary>
    public static void GameLoadGame_Postfix()
    {
        QuestManager? manager = Find.QuestManager;
        if (manager == null)
            return;

        foreach (Quest quest in manager.QuestsListForReading)
        {
            foreach (QuestPart part in quest.PartsListForReading)
            {
                if (part is not QuestPart_CSC csc)
                    continue;

                if (csc.map == null)
                {
                    Log.Warning("[Fix_WRMegaCorp_QuestPartCSC] QuestPart_CSC has no map; leaving pos unchanged.");
                    continue;
                }

                if (csc.pos != IntVec3.Zero)
                    continue;

                csc.pos = DropCellFinder.RandomDropSpot(csc.map);
                Log.Message($"[Fix_WRMegaCorp_QuestPartCSC] Placed QuestPart_CSC at {csc.pos} on {csc.map}.");
            }
        }
    }
}
