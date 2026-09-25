using Verse;

namespace CEOverpenetration;

public sealed class CEOverpenetrationSettings : ModSettings
{
    public bool LogContinuations;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref LogContinuations, "logContinuations", false);
    }
}
