using Verse;

namespace WhiteGiver.MTOEWYStatPerfFix;

public sealed class MTOEWYStatPerfFixMod : Mod
{
    public MTOEWYStatPerfFixMod(ModContentPack content)
        : base(content)
    {
        Bootstrap.Install();
        LongEventHandler.ExecuteWhenFinished(Bootstrap.RepairIfNeeded);
    }
}
