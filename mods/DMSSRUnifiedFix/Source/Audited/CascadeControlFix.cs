using System.Collections.Generic;
using System;
using System.Reflection;
using DMSSR.Hediffs;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// The cascade root mark expires after 1800 ticks, but neither the emitter nor the leaf link checks
/// that the root is still present. Leaf mechs keep the permanent consciousness bonus. Reject links
/// whose source no longer carries the caster mark, and remove matching leaf hediffs immediately when
/// the root mark is removed or expires.
/// </summary>
internal static class CascadeControlFix
{
    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        MethodInfo isLinkValid = AccessTools.Method(
            typeof(HediffComp_SchizophreniaLink), "IsLinkValid")
            ?? throw new MissingMethodException("DMSSR.Hediffs.HediffComp_SchizophreniaLink.IsLinkValid");
        MethodInfo rootRemoved = AccessTools.Method(
            typeof(HediffWithComps), nameof(HediffWithComps.PostRemoved))
            ?? throw new MissingMethodException("Verse.HediffWithComps.PostRemoved");

        harmony.Patch(isLinkValid, postfix: new HarmonyMethod(
            typeof(CascadeControlFix), nameof(IsLinkValidPostfix)));
        harmony.Patch(rootRemoved, postfix: new HarmonyMethod(
            typeof(CascadeControlFix), nameof(RootRemovedPostfix)));
        Installed = true;
    }

    private static void IsLinkValidPostfix(
        HediffComp_SchizophreniaLink __instance, Pawn pawn, Pawn source, ref bool __result)
    {
        if (!__result || source?.health?.hediffSet == null)
        {
            return;
        }

        HediffDef? mark = DMS_HediffDefOf.DMS_Schizophrenia_CasterMark;
        if (mark == null || !source.health.hediffSet.HasHediff(mark))
        {
            __result = false;
        }
    }

    private static void RootRemovedPostfix(HediffWithComps __instance)
    {
        if (__instance?.def != DMS_HediffDefOf.DMS_Schizophrenia_CasterMark)
        {
            return;
        }

        Pawn? root = __instance.pawn;
        if (root == null || root.Dead)
        {
            return;
        }

        Map? map = root.MapHeld;
        IReadOnlyList<Pawn>? pawns = map?.mapPawns?.AllPawnsSpawned;
        if (pawns == null)
        {
            return;
        }

        HediffDef? mark = DMS_HediffDefOf.DMS_Schizophrenia_CasterMark;
        if (mark != null && root.health?.hediffSet?.HasHediff(mark) == true)
        {
            // A recast replaced/refreshed the root; the leaves are still valid.
            return;
        }

        HediffDef? linkedDef = DMS_HediffDefOf.DMS_Schizophrenia_Linked;
        if (linkedDef == null)
        {
            return;
        }

        int rootId = root.thingIDNumber;
        IReadOnlyList<Pawn> spawned = pawns;
        for (int i = 0; i < spawned.Count; i++)
        {
            Pawn leaf = spawned[i];
            if (leaf?.health?.hediffSet == null)
            {
                continue;
            }

            Hediff? linked = leaf.health.hediffSet.GetFirstHediffOfDef(linkedDef);
            if (linked == null)
            {
                continue;
            }

            HediffComp_SchizophreniaLink? link =
                HediffUtility.TryGetComp<HediffComp_SchizophreniaLink>(linked);
            if (link != null && link.sourceThingID == rootId)
            {
                leaf.health.RemoveHediff(linked);
            }
        }
    }
}
