using System;
using System.Collections.Generic;
using System.Reflection;
using DMSE;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WhiteGiver.DMSEWorldPerfFix;

/// <summary>
/// FIX 3b - make <c>WorldComponent_BVRNetwork.BuildNodes(nodes, activeOnly: true)</c> scale with the number of
/// <em>active</em> nodes instead of the number of world objects.
///
/// Upstream <c>RebuildScanNodes</c> is called on every scan tick (15 ticks by default) whenever a missile or
/// drop-pod window is open. It walks <c>Find.WorldObjects.AllWorldObjects.OfType&lt;MapParent&gt;()</c> and calls
/// <c>GetComponent&lt;WorldObjectComp_AirDefense&gt;()</c> plus <c>WorldComponent_SettlementDefense.DefenseFor</c>
/// on every settlement on the planet, only to discard all but the handful of ids present in
/// <c>nodeActiveUntil</c>. On a 1,300-settlement world that is ~1,300 component lookups and ~1,300
/// <c>Eligible()</c> checks per scan tick.
///
/// This prefix keeps the exact same candidate set and the exact same list order (the order of
/// <c>AllWorldObjects</c>), but resolves only the ids present in <c>nodeActiveUntil</c> through an index that is
/// rebuilt only when the world object list changes or an id misses. Expired ids are dropped without touching
/// the world. The map-level nodes from <c>Find.Maps</c> are built first, in the original order, so the resulting
/// <c>scanNodes</c> list is element-by-element identical to upstream.
///
/// The <c>activeOnly: false</c> rebuild (every 2,000 ticks) is intentionally left untouched; FIX 1 already
/// removes its state-creation side effect.
/// </summary>
internal static class BvrScanPatches
{
    private static readonly HashSet<Faction> SeenFactions = new();
    private static readonly List<MapParent> OrderedParents = new();
    private static readonly List<int> OrderedIndexes = new();
    private static readonly ParentIndex Index = new();

    private static FieldInfo? nodeActiveUntilField;
    private static Action<List<BVRNode>>? applyAlertStages;

    internal static void Install(Harmony harmony, Type networkType)
    {
        MethodInfo target = AccessTools.DeclaredMethod(
            networkType,
            "BuildNodes",
            new[] { typeof(List<BVRNode>), typeof(bool) })
            ?? throw new MissingMethodException(networkType.FullName, "BuildNodes(List<BVRNode>, bool)");

        nodeActiveUntilField = AccessTools.DeclaredField(networkType, "nodeActiveUntil")
            ?? throw new MissingFieldException(networkType.FullName, "nodeActiveUntil");

        MethodInfo stages = AccessTools.DeclaredMethod(networkType, "ApplyAlertStages", new[] { typeof(List<BVRNode>) })
            ?? throw new MissingMethodException(networkType.FullName, "ApplyAlertStages");
        applyAlertStages = (Action<List<BVRNode>>)stages.CreateDelegate(typeof(Action<List<BVRNode>>));

        harmony.Patch(
            target,
            prefix: new HarmonyMethod(typeof(BvrScanPatches), nameof(BuildNodesPrefix)));
    }

    private static bool BuildNodesPrefix(WorldComponent_BVRNetwork __instance, List<BVRNode> nodes, bool activeOnly)
    {
        if (!activeOnly)
        {
            return true;
        }

        int ticksGame = Find.TickManager.TicksGame;
        nodes.Clear();

        // Identical to upstream: per-map BVR combat nodes first, factions in first-seen order.
        foreach (Map map in Find.Maps)
        {
            MapComponent_BVRCombat? component = map.GetComponent<MapComponent_BVRCombat>();
            if (component == null)
            {
                continue;
            }

            SeenFactions.Clear();
            foreach (CompSearchRadar radar in component.searchRadars)
            {
                Faction? faction = radar.parent?.Faction;
                if (faction != null && SeenFactions.Add(faction))
                {
                    nodes.Add(new BVRNode { parent = map.Parent, faction = faction, local = component });
                }
            }

            foreach (CompFireControlRadar radar in component.fireControlRadars)
            {
                Faction? faction = radar.parent?.Faction;
                if (faction != null && SeenFactions.Add(faction))
                {
                    nodes.Add(new BVRNode { parent = map.Parent, faction = faction, local = component });
                }
            }
        }

        bool enabled = WorldComponent_FactionAirDefenseAlert.Enabled;

        if (nodeActiveUntilField?.GetValue(__instance) is not Dictionary<int, int> nodeActiveUntil || nodeActiveUntil.Count == 0)
        {
            Finish(nodes);
            return false;
        }

        OrderedParents.Clear();
        OrderedIndexes.Clear();

        foreach (KeyValuePair<int, int> pair in nodeActiveUntil)
        {
            if (pair.Value < ticksGame)
            {
                continue;
            }

            if (!Index.TryGet(pair.Key, out MapParent? parent, out int index) || parent == null)
            {
                continue;
            }

            InsertOrdered(parent, index);
        }

        for (int i = 0; i < OrderedParents.Count; i++)
        {
            MapParent parent = OrderedParents[i];
            if (parent.Destroyed)
            {
                continue;
            }

            WorldObjectComp_AirDefense? airDefense = parent.GetComponent<WorldObjectComp_AirDefense>();
            if (!enabled && (airDefense == null || !airDefense.Props.ignoreNetworkResearch))
            {
                continue;
            }

            if (airDefense != null && airDefense.Available)
            {
                nodes.Add(new BVRNode
                {
                    parent = parent,
                    faction = parent.Faction,
                    defense = new WorldAirDefenseAdapter(airDefense),
                });
                continue;
            }

            IBVRDefense? defense = WorldComponent_SettlementDefense.Current?.DefenseFor(parent);
            if (defense != null && defense.Available)
            {
                nodes.Add(new BVRNode
                {
                    parent = parent,
                    faction = parent.Faction,
                    defense = defense,
                });
            }
        }

        Finish(nodes);
        return false;
    }

    private static void Finish(List<BVRNode> nodes)
    {
        applyAlertStages!(nodes);
        for (int i = 0; i < nodes.Count; i++)
        {
            nodes[i].jamEff = EWEnvironment.NodeJam(nodes[i]);
        }
    }

    private static void InsertOrdered(MapParent parent, int index)
    {
        int position = OrderedIndexes.Count;
        while (position > 0 && OrderedIndexes[position - 1] > index)
        {
            position--;
        }

        OrderedIndexes.Insert(position, index);
        OrderedParents.Insert(position, parent);
    }

    /// <summary>
    /// id -> (MapParent, position in <c>AllWorldObjects</c>). Rebuilt when the holder, the list instance or the
    /// count changes, when an id misses, or when a cached position no longer points at the cached object.
    /// </summary>
    private sealed class ParentIndex
    {
        private WorldObjectsHolder? holder;
        private List<WorldObject>? list;
        private int count = -1;
        private readonly Dictionary<int, MapParent> byId = new();
        private readonly Dictionary<int, int> indexById = new();

        internal bool TryGet(int id, out MapParent? parent, out int index)
        {
            EnsureFresh();

            if (!byId.TryGetValue(id, out MapParent? found))
            {
                // Not a MapParent in the current world object list. Stale nodeActiveUntil ids always point at
                // objects that were removed (ids are never reused), so this needs no rescan.
                parent = null;
                index = -1;
                return false;
            }

            if (indexById.TryGetValue(id, out int foundIndex)
                && list != null
                && foundIndex >= 0
                && foundIndex < list.Count
                && ReferenceEquals(list[foundIndex], found))
            {
                parent = found;
                index = foundIndex;
                return true;
            }

            // The id exists but its cached position moved (an object was inserted/removed without changing the
            // count). Rebuild once and re-check.
            Rebuild();
            if (byId.TryGetValue(id, out found)
                && indexById.TryGetValue(id, out foundIndex)
                && foundIndex >= 0
                && foundIndex < list!.Count
                && ReferenceEquals(list[foundIndex], found))
            {
                parent = found;
                index = foundIndex;
                return true;
            }

            parent = null;
            index = -1;
            return false;
        }

        private void EnsureFresh()
        {
            WorldObjectsHolder currentHolder = Find.WorldObjects;
            List<WorldObject> currentList = currentHolder.AllWorldObjects;
            if (ReferenceEquals(holder, currentHolder) && ReferenceEquals(list, currentList) && count == currentList.Count)
            {
                return;
            }

            Rebuild();
        }

        private void Rebuild()
        {
            holder = Find.WorldObjects;
            list = holder.AllWorldObjects;
            count = list.Count;
            byId.Clear();
            indexById.Clear();

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is MapParent parent)
                {
                    byId[parent.ID] = parent;
                    indexById[parent.ID] = i;
                }
            }
        }
    }
}
