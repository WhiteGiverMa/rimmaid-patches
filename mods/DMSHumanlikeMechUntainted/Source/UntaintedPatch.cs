using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Meidocho.DMSHumanlikeMechUntainted;

public sealed class DMSHumanlikeMechUntaintedMod : Mod
{
    public DMSHumanlikeMechUntaintedMod(ModContentPack content) : base(content)
    {
        new Harmony("meidocho.dms.humanlikemechuntainted").PatchAll(typeof(DMSHumanlikeMechUntaintedMod).Assembly);
    }
}

/// <summary>
/// 阻止 DMS 系列能穿人类衣服的机兵在死亡时把穿着的 apparel 标记为 wornByCorpse（死人衣服）。
/// 仅跳过 wornByCorpseInt 标记；耐久损伤由 Pawn_ApparelTracker.Notify_PawnKilled 前半段处理、不受影响，
/// ThingComp.Notify_WearerDied 由本 prefix 手动触发以保留 vanilla 行为。
/// </summary>
[HarmonyPatch(typeof(Apparel), nameof(Apparel.Notify_PawnKilled))]
public static class ApparelNotifyPawnKilledPatch
{
    // ponytail: 只覆盖 DMS Core / Synthetic / Legion 三家；其他 mod 的穿衣机兵不在范围内。
    private static readonly string[] EligiblePackageIds =
    {
        "aoba.deadmanswitch.core",
        "aoba.deadmanswitch.synthetic",
        "srxf.deadmanswitch.legion",
    };

    // 穿衣机制语义标记：FFF 的 CompMechApparel（Synthetic 4 种 + 未来同机制机兵）与 Legion 的 CompTerraBodyType（尘世 Terra）。
    private const string FffMechApparelCompName = "Fortified.CompMechApparel";
    private const string LegionTerraBodyTypeCompName = "DMS_Legion.CompTerraBodyType";

    [HarmonyPrefix]
    public static bool Prefix(Apparel __instance)
    {
        Pawn? wearer = __instance.Wearer;
        if (wearer == null)
        {
            return true;
        }
        if (!IsEligibleDmsHumanlikeMech(wearer))
        {
            return true;
        }

        // 合格机兵：跳过 wornByCorpseInt = true，但仍调用 Notify_WearerDied 保留其他 comp 回调。
        foreach (ThingComp comp in __instance.AllComps)
        {
            comp.Notify_WearerDied();
        }
        return false;
    }

    private static bool IsEligibleDmsHumanlikeMech(Pawn pawn)
    {
        if (pawn.RaceProps == null || !pawn.RaceProps.IsMechanoid)
        {
            return false;
        }
        string? packageId = pawn.def?.modContentPack?.PackageIdPlayerFacing;
        if (packageId == null)
        {
            return false;
        }
        bool isDmsPackage = false;
        foreach (string eligible in EligiblePackageIds)
        {
            if (string.Equals(packageId, eligible, StringComparison.OrdinalIgnoreCase))
            {
                isDmsPackage = true;
                break;
            }
        }
        if (!isDmsPackage)
        {
            return false;
        }

        // 软引用检测：找不到类型时 GetType().FullName 自然不会匹配。
        foreach (ThingComp comp in pawn.AllComps)
        {
            string? fullName = comp.GetType().FullName;
            if (fullName == FffMechApparelCompName || fullName == LegionTerraBodyTypeCompName)
            {
                return true;
            }
        }
        return false;
    }
}
