using System;
using System.Collections.Generic;
using HarmonyLib;
using PickUpAndHaul;
using Verse;

namespace WhiteGiver.BulkLoadPuahStaleFix;

/// <summary>
/// Bulk Load 的 SoftlockCleaner 只看 PUAH 记录的 count，不检查物品是否还在背包里。
/// 当一条记录指向已经离开背包（被放置、转移或回到地图）的物品时，它会每轮都打印
/// "Dropping 1 soft-locked PUAH items"，却因为 Contains 检查而永远不真正 drop，
/// 也永远不移除记录。这里在它检查之前删掉这类幽灵条目。
/// </summary>
public sealed class BulkLoadPuahStaleFixMod : Mod
{
    private const string HarmonyId = "whitegiver.bulkload.puahstale";

    public BulkLoadPuahStaleFixMod(ModContentPack content) : base(content)
    {
        var cleanerType = AccessTools.TypeByName("BulkLoadForTransporters.Core.SoftlockCleaner");
        var target = cleanerType == null
            ? null
            : AccessTools.Method(cleanerType, "TryCleanPawn", new[] { typeof(Pawn) });

        if (target == null || AccessTools.TypeByName("PickUpAndHaul.CompHauledToInventory") == null)
        {
            Log.Warning("[BulkLoad Stale Fix] SoftlockCleaner.TryCleanPawn was not found; patch skipped. Re-check after a Bulk Load update.");
            return;
        }

        new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(
            typeof(BulkLoadPuahStaleFixMod), nameof(RemoveStaleEntries)));
        Log.Message("[BulkLoad Stale Fix] Stale PUAH inventory entries will be removed before Bulk Load's cleaner runs.");
    }

    private static void RemoveStaleEntries(Pawn pawn)
    {
        if (pawn == null || pawn.Dead || !pawn.Spawned) return;

        var comp = pawn.GetComp<CompHauledToInventory>();
        if (comp == null) return;

        HashSet<Thing> hauled = comp.GetHashSet();
        if (hauled.Count == 0) return;

        int removed = CleanStale(
            hauled,
            thing => pawn.inventory?.innerContainer?.Contains(thing) == true,
            pawn.carryTracker?.CarriedThing);

        if (removed > 0 && Prefs.DevMode)
            Log.Message($"[BulkLoad Stale Fix] Removed {removed} stale PUAH entr{(removed == 1 ? "y" : "ies")} from {pawn.LabelShort}.");
    }

    internal static int CleanStale(HashSet<Thing> hauled, Func<Thing, bool> isInInventory, Thing? carried)
    {
        List<Thing>? stale = null;

        foreach (Thing thing in hauled)
        {
            // GetHashSet() already removes nulls, but a concurrent change may still leave one.
            if (thing == null || thing.Destroyed || thing.stackCount <= 0)
            {
                (stale ??= new List<Thing>()).Add(thing!);
                continue;
            }

            if (ReferenceEquals(thing, carried)) continue;
            if (isInInventory(thing)) continue;

            (stale ??= new List<Thing>()).Add(thing);
        }

        if (stale == null) return 0;

        foreach (Thing thing in stale) hauled.Remove(thing);
        return stale.Count;
    }
}
