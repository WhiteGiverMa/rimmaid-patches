using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSMechStorage;

[HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.GetPosture))]
public static class StoredMechPosture
{
    public static void Postfix(Pawn p, ref PawnPosture __result)
    {
        if (!p.Dead && !p.Spawned && p.ParentHolder is Building_MechCapsule capsule && MechStorage.IsDmsCapsule(capsule))
            __result = PawnPosture.Standing;
    }
}
