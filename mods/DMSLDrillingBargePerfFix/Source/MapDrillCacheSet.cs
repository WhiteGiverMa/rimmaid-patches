using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSL.DrillingBargePerfFix;

/// <summary>
/// Owns the per-<see cref="Map"/> <see cref="MapDrillCache"/> instances and keeps them in sync with
/// <see cref="DeepResourceGrid"/>. Keyed by <see cref="ConditionalWeakTable{TKey,TValue}"/> so a map that is
/// discarded (caravan map, pocket map, new game) also releases its cache.
/// </summary>
internal static class MapDrillCacheSet
{
    private static readonly ConditionalWeakTable<Map, MapDrillCache> caches = new();

    private static AccessTools.FieldRef<DeepResourceGrid, Map>? gridMapField;
    private static bool gridMapFieldResolved;

    /// <summary>
    /// False when <see cref="DeepResourceGrid"/>'s private map field cannot be bound (a future game build
    /// renamed it). In that case <c>SetAt</c> edits could not be observed, so the callers must not install
    /// the cache-consuming patches at all and fall back to the upstream full scan.
    /// </summary>
    internal static bool EnsureTrackingAvailable()
    {
        return ResolveGridMapField() != null;
    }

    /// <summary>Returns the cache for <paramref name="map"/>, creating it on first use.</summary>
    internal static MapDrillCache Get(Map map)
    {
        if (caches.TryGetValue(map, out MapDrillCache? cache))
        {
            return cache;
        }

        cache = new MapDrillCache(map);
        caches.Add(map, cache);
        return cache;
    }

    /// <summary>Returns the cache only when it already exists; never allocates.</summary>
    internal static bool TryGetExisting(Map? map, out MapDrillCache cache)
    {
        if (map != null && caches.TryGetValue(map, out MapDrillCache? existing))
        {
            cache = existing;
            return true;
        }

        cache = null!;
        return false;
    }

    internal static void NotifyCellChanged(DeepResourceGrid grid, IntVec3 cell)
    {
        Map? map = GetGridMap(grid);
        if (TryGetExisting(map, out MapDrillCache cache))
        {
            cache.NotifyCellChanged(cell);
        }
    }

    internal static void Invalidate(DeepResourceGrid grid)
    {
        Map? map = GetGridMap(grid);
        if (map != null)
        {
            caches.Remove(map);
        }
    }

    private static Map? GetGridMap(DeepResourceGrid grid)
    {
        AccessTools.FieldRef<DeepResourceGrid, Map>? field = ResolveGridMapField();
        return field == null ? null : field(grid);
    }

    private static AccessTools.FieldRef<DeepResourceGrid, Map>? ResolveGridMapField()
    {
        if (gridMapFieldResolved)
        {
            return gridMapField;
        }

        gridMapFieldResolved = true;
        try
        {
            gridMapField = AccessTools.FieldRefAccess<DeepResourceGrid, Map>("map");
        }
        catch (Exception exception)
        {
            gridMapField = null;
            Log.Error(DrillingBargePerfFix.LogPrefix + " could not bind DeepResourceGrid.map; the deep-resource index is disabled: " + exception.Message);
        }

        return gridMapField;
    }
}
