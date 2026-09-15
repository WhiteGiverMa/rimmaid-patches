// Recovered from ConduitRoomStatsFix.dll with ILSpy 11.0.0 on 2026-09-15.
// This is a readable recovery copy, not the original source file.
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ConduitRoomStatsFix;

public sealed class ConduitRoomStatsFixMod : Mod
{
    public ConduitRoomStatsFixMod(ModContentPack content) : base(content)
    {
        Log.Message("[ConduitRoomStatsFix] Loaded, patching...");
        new Harmony("meidocho.conduitroomstatsfix").PatchAll();
        Log.Message("[ConduitRoomStatsFix] PatchAll done");
    }
}

[HarmonyPatch(typeof(Building), nameof(Building.GetGizmos))]
public static class PatchBuildingGetGizmos
{
    private static void Postfix(Building __instance, ref IEnumerable<Gizmo> __result)
    {
        if (__instance.def != ThingDefOf.HiddenConduit && __instance.def != ThingDefOf.PowerConduit)
            return;

        var filtered = new List<Gizmo>();
        foreach (Gizmo gizmo in __result)
        {
            if (gizmo is not Gizmo_RoomStats)
                filtered.Add(gizmo);
        }

        __result = filtered;
    }
}
