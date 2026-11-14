using System.Collections.Generic;
using System.Threading;
using DMS_Legion.AXF12;
using RimWorld.Planet;
using Verse;

namespace WhiteGiver.DMSL.CommanderPerfFix;

/// <summary>One atomically published (Game, components, manager) snapshot; no save state.</summary>
internal static class ReconManagerCache
{
    private sealed class Snapshot
    {
        internal readonly Game Game;
        internal readonly List<GameComponent> Components;
        internal readonly AXF12ReconMissionManager? Manager;

        internal Snapshot(Game game, List<GameComponent> components, AXF12ReconMissionManager? manager)
        {
            Game = game;
            Components = components;
            Manager = manager;
        }
    }

    private static Snapshot? snapshot;
    private static int scanCount;

    // QA reads this once-per-cache-build counter through reflection. It is not on the warm query path.
    internal static int ScanCount => Volatile.Read(ref scanCount);

    internal static AXF12ReconMissionManager? Instance
    {
        get
        {
            Game? game = Current.Game;
            if (game == null)
            {
                Volatile.Write(ref snapshot, null);
                return null;
            }

            List<GameComponent> components = game.components;
            Snapshot? current = Volatile.Read(ref snapshot);
            if (current != null
                && ReferenceEquals(current.Game, game)
                && ReferenceEquals(current.Components, components))
            {
                return current.Manager;
            }

            // Same first-match "is T" semantics as real Game.GetComponent<T>(), but only on a cache miss.
            AXF12ReconMissionManager? manager = null;
            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] is AXF12ReconMissionManager found)
                {
                    manager = found;
                    break;
                }
            }

            Interlocked.Increment(ref scanCount);
            Volatile.Write(ref snapshot, new Snapshot(game, components, manager));
            return manager;
        }
    }

    internal static bool GetInstancePrefix(ref AXF12ReconMissionManager? __result)
    {
        __result = Instance;
        return false;
    }

    internal static bool CheckRemoveMapNowPrefix(MapParent __instance)
    {
        // Deliberately use the observed reference rather than HasMap. This is O(1) and also preserves the
        // upstream answer for the pathological ObservedMapParent with HasMap == false.
        return !ReferenceEquals(Instance?.ObservedMapParent, __instance);
    }
}
