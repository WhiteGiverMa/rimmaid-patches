using RimWorld;
using Verse;

namespace WhiteGiver.ReinforcedConcreteHeavyBridge;

[StaticConstructorOnStartup]
internal static class BridgeCost
{
    internal const string DefName = "WhiteGiver_ReinforcedConcreteHeavyBridge";

    static BridgeCost() => Apply(BridgeMod.Settings);

    internal static void Apply(BridgeSettings settings)
    {
        settings.Clamp();
        TerrainDef? bridge = DefDatabase<TerrainDef>.GetNamedSilentFail(DefName);
        if (bridge == null)
        {
            return;
        }

        ThingDefCountClass cost = bridge.costList[0];
        if (cost.count != settings.MaterialCost)
        {
            cost.count = settings.MaterialCost;
            CostListCalculator.Reset();
            WealthWatcher.ResetStaticData();
            if (Current.ProgramState == ProgramState.Playing)
            {
                foreach (Map map in Find.Maps)
                {
                    map.wealthWatcher.ForceRecount();
                    foreach (Room room in map.regionGrid.AllRooms) room.Notify_TerrainChanged();
                }
            }
        }
    }
}
