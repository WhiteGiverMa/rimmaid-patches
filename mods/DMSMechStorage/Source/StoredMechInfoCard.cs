using System.Collections.Generic;
using System.Linq;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSMechStorage;

[HarmonyPatch(typeof(StatsReportUtility), "DescriptionEntry", new[] { typeof(Thing) })]
public static class CapsuleMechInfo
{
    private static readonly AccessTools.FieldRef<StatDrawEntry, IEnumerable<Dialog_InfoCard.Hyperlink>?> Hyperlinks =
        AccessTools.FieldRefAccess<StatDrawEntry, IEnumerable<Dialog_InfoCard.Hyperlink>?>("hyperlinks");

    public static void Postfix(Thing thing, StatDrawEntry __result)
    {
        if (thing is not Building_MechCapsule capsule || !MechStorage.IsDmsCapsule(capsule)) return;
        Hyperlinks(__result) = (__result.GetHyperlinks(StatRequest.For(thing))
            ?? Enumerable.Empty<Dialog_InfoCard.Hyperlink>()).Append(new Dialog_InfoCard.Hyperlink(capsule.Mech));
    }
}
