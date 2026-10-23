using HarmonyLib;
using Verse;

namespace WhiteGiver.DescriptionWhitespaceFix;

public sealed class DescriptionFixMod : Mod
{
    public DescriptionFixMod(ModContentPack content) : base(content)
    {
        new Harmony("whitegiver.targeteddescriptionwhitespacefix").PatchAll(typeof(DescriptionFixMod).Assembly);
    }
}

[HarmonyPatch(typeof(Def), nameof(Def.ConfigErrors))]
public static class DescriptionFix
{
    [HarmonyPrefix]
    public static void Prefix(Def __instance)
    {
        // ponytail: 只修这两个已报告的描述；新增目标需独立核验，不全局吞掉配置错误。
        if (__instance.defName == "Techprint_Kiiro_PrototypeGun" ||
            __instance.defName == "OA_RK_New_Windbreaker_G")
            __instance.description = __instance.description?.Trim();
    }
}
