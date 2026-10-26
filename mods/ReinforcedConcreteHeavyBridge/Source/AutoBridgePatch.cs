using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.ReinforcedConcreteHeavyBridge;

internal static class AutoBridgePatch
{
    internal const string HarmonyId = "whitegiver.reinforcedconcrete.heavybridge";
    private static bool applied;

    internal static void Apply()
    {
        if (applied) return;
        Type? type = AccessTools.TypeByName("Replace_Stuff.PlaceBridges.PlaceBridges");
        if (type == null)
        {
            Log.Message("[ReinforcedConcreteHeavyBridge] Replace Stuff is not loaded; automatic bridge selection is inactive.");
            return;
        }
        var target = AccessTools.DeclaredMethod(type, "GetNeededBridge",
            new[] { typeof(BuildableDef), typeof(IntVec3), typeof(Map), typeof(ThingDef) });
        if (target == null || !target.IsStatic || target.ReturnType != typeof(TerrainDef))
        {
            Log.Error("[ReinforcedConcreteHeavyBridge] Expected Replace Stuff GetNeededBridge signature was not found; automatic bridge patch not applied.");
            return;
        }
        try
        {
            new Harmony(HarmonyId).Patch(target, postfix: new HarmonyMethod(typeof(AutoBridgePatch), nameof(Postfix)));
            applied = true;
            Log.Message("[ReinforcedConcreteHeavyBridge] Patched Replace Stuff automatic heavy-bridge selection.");
        }
        catch (Exception exception)
        {
            Log.Error($"[ReinforcedConcreteHeavyBridge] Could not apply automatic bridge patch: {exception}");
        }
    }

    private static void Postfix(BuildableDef def, IntVec3 pos, Map map, ThingDef stuff, ref TerrainDef? __result)
    {
        if (__result == null) return;
        TerrainDef? vanilla = DefDatabase<TerrainDef>.GetNamedSilentFail("HeavyBridge");
        TerrainDef? concrete = DefDatabase<TerrainDef>.GetNamedSilentFail(BridgeCost.DefName);
        if (vanilla == null || concrete == null ||
            (!ReferenceEquals(__result, vanilla) && !ReferenceEquals(__result, concrete))) return;

        TerrainAffordanceDef needed = def.GetTerrainAffordanceNeed(stuff);
        foreach (Thing thing in pos.GetThingList(map))
        {
            if (thing is Blueprint_Build or Frame && thing.def.entityDefToBuild is TerrainDef existing &&
                (ReferenceEquals(existing, vanilla) || ReferenceEquals(existing, concrete)) && existing.affordances.Contains(needed))
            {
                __result = existing;
                return;
            }
        }
        TerrainDef selected = BridgeMod.Settings.AutoUseReinforcedConcrete ? concrete : vanilla;
        // ponytail: 只替换已获准的两种重型桥；研究未完成时保留 Replace Stuff 的结果，不复制其放置规则。
        if (selected.IsResearchFinished) __result = selected;
    }
}
