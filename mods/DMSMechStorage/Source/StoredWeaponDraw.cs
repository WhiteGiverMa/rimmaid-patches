using Fortified;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace WhiteGiver.DMSMechStorage;

/// <summary>
/// DMS ruins draw the dormant mech's primary weapon on the ground. The capsule's live draw path is
/// DynamicDrawPhaseAt (its def is RealtimeOnly, so Print never runs), and FFF draws no weapon at all.
/// Drawing in a prefix keeps the ruins order: weapon first, mech body over it.
/// </summary>
[HarmonyPatch(typeof(Building_MechCapsule), nameof(Building_MechCapsule.DynamicDrawPhaseAt))]
public static class StoredWeaponDraw
{
    private static readonly FloatRange Scatter = new(-15f, 25f);

    // North, East, South, West. Tables copied from the AncientCorps dormant mech building defs.
    private static readonly Vector3[] MediumOffsets =
    {
        new(0.75f, 0f, 0f), new(-0.1f, 0f, -0.5f), new(-0.15f, 0f, -0.75f), new(-0.15f, 0f, 0.75f)
    };
    private static readonly float[] MediumAngles = { 0f, 100f, 90f, 270f };
    private static readonly float[] MediumLayers = { -1f, 1f, -1f, -1f };
    private static readonly Vector3[] LargeOffsets =
    {
        new(1.2f, 0f, 0f), new(-0.25f, 0f, -1f), new(-1.2f, 0f, 0f), new(0.25f, 0f, 1.1f)
    };
    private static readonly float[] LargeAngles = { 0f, 90f, 180f, 270f };
    private static readonly float[] LargeLayers = { -1f, 1f, -1f, -1f };

    public static void Prefix(Building_MechCapsule __instance, DrawPhase phase, Vector3 drawLoc)
    {
        if (phase != DrawPhase.Draw || !MechStorage.IsDmsCapsule(__instance)) return;
        if (!TryGet(__instance, drawLoc, out Vector3 position, out float angle)) return;
        PawnRenderUtility.DrawEquipmentAiming(__instance.Mech.equipment.Primary, position, angle);
    }

    public static bool TryGet(Building_MechCapsule capsule, Vector3 drawLoc, out Vector3 position, out float angle)
    {
        position = default;
        angle = 0f;
        Pawn? mech = capsule.Mech;
        Thing? weapon = mech?.equipment?.Primary;
        if (mech == null || mech.Dead || weapon == null) return false;

        int size = Mathf.Max(capsule.def.size.x, capsule.def.size.z);
        bool large = size >= 3;
        // AncientCorps only defines 2x2 and 3x3 tables; scale those for other capsule sizes.
        float scale = large ? size / 3f : size / 2f;
        int index = capsule.Rotation.AsInt;
        Vector3 offset = (large ? LargeOffsets : MediumOffsets)[index];
        offset.x *= scale;
        offset.z *= scale;
        offset.y += 0.03658537f * (large ? LargeLayers : MediumLayers)[index];
        position = drawLoc + offset;
        angle = ((large ? LargeAngles : MediumAngles)[index]
            + Scatter.RandomInRangeSeeded(GenText.StableStringHash(capsule.ThingID))) % 360f;
        return true;
    }
}
