using System;
using Verse;

namespace WhiteGiver.ReinforcedConcreteHeavyBridge;

public sealed class BridgeSettings : ModSettings
{
    public const int DefaultCost = 14;
    public int MaterialCost = DefaultCost;
    public bool AutoUseReinforcedConcrete;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref MaterialCost, "materialCost", DefaultCost);
        Scribe_Values.Look(ref AutoUseReinforcedConcrete, "autoUseReinforcedConcrete", false);
        Clamp();
    }

    public void Clamp() => MaterialCost = Math.Clamp(MaterialCost, 1, 10000);
}
