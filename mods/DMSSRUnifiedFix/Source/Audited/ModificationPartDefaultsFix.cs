using System;
using Verse;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// Defensive startup copy of the XML part-default repair. Keeping it here means a load order that
/// applies our XML patch before DMSSR's defs, or a future def edit, still keeps the install job's
/// chosen Brain/Spine part instead of letting <c>Pawn_HealthTracker.AddHediff</c> rewrite it to
/// Reactor.
/// </summary>
internal static class ModificationPartDefaultsFix
{
    internal static bool Installed { get; private set; }

    internal static void Apply()
    {
        foreach (string defName in new[] { "DMS_CascadeDock", "DMS_Blackwater" })
        {
            HediffDef? def = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            if (def?.defaultInstallPart?.defName == "Reactor")
            {
                def.defaultInstallPart = null;
            }
        }

        Installed = true;
    }
}
