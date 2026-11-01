using System;
using System.Collections.Generic;
using Verse;

namespace WhiteGiver.PuahDuplicateCompFix;

/// <summary>
/// Pick Up And Haul 的 XML patch 用 Defs/ThingDef[thingClass="Pawn"]/comps 选择目标。
/// RimWorld 在继承合并之前应用 PatchOperation，所以原版 BasePawn（显式 thingClass=Pawn）
/// 和任何自己又写了一遍 thingClass=Pawn 的具体 def 会各拿到一份 CompHauledToInventory；
/// 继承合并后这些 def 的 comps 里就有两个同类型 comp。
///
/// 两个 comp 在读档时对同一个 parent 注册同一个 ThingsHauledToInventory 路径：
/// 第二次注册打印 "Tried to register the same list of load IDs twice"，
/// ResolvingCrossRefs 阶段第二次 TakeList 又打印 "Could not get load IDs list"。
///
/// 这里在 DefDatabase 完成后按 compClass 去重，只处理这个 comp，不碰其他 comp。
/// </summary>
[StaticConstructorOnStartup]
public static class DuplicateCompFix
{
    private const string CompTypeName = "PickUpAndHaul.CompHauledToInventory";

    static DuplicateCompFix()
    {
        int removed = 0;
        foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            removed += RemoveDuplicates(def.comps);
        }

        if (removed > 0)
        {
            Log.Message($"[PUAH Duplicate Comp Fix] Removed {removed} inherited duplicate CompHauledToInventory entr{(removed == 1 ? "y" : "ies")} from ThingDef comps.");
        }
    }

    internal static int RemoveDuplicates(List<CompProperties>? comps)
    {
        if (comps == null || comps.Count < 2) return 0;

        bool found = false;
        int removed = 0;
        for (int i = 0; i < comps.Count; i++)
        {
            CompProperties? props = comps[i];
            if (props?.compClass?.FullName != CompTypeName) continue;

            if (!found)
            {
                found = true;
                continue;
            }

            comps.RemoveAt(i);
            removed++;
            i--;
        }

        return removed;
    }
}
