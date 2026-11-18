using System;
using System.Collections.Generic;
using System.Threading;
using MiliraTheOneEntwinedWithYou.GameComponents;
using Verse;

namespace WhiteGiver.MTOEWYStatPerfFix;

/// <summary>
/// Replaces the upstream "walk Current.Game.components and build a fresh
/// GetUniqueLoadID() string on every stat query" pattern with a reference-checked
/// cache: the game and its component list are captured once, and the load-id string
/// of the one ideal pawn is materialised exactly once.
/// </summary>
internal static class TrackerCache
{
    private sealed class Cache
    {
        internal readonly Game Game;
        internal readonly List<GameComponent>? Components;
        internal readonly GameComponent_IdealTracker? Tracker;

        internal Cache(Game game, List<GameComponent>? components, GameComponent_IdealTracker? tracker)
        {
            Game = game;
            Components = components;
            Tracker = tracker;
        }
    }

    private sealed class LoadIdEntry
    {
        internal readonly Pawn Pawn;
        internal readonly string LoadId;

        internal LoadIdEntry(Pawn pawn, string loadId)
        {
            Pawn = pawn;
            LoadId = loadId;
        }
    }

    private static Cache? cache;
    private static LoadIdEntry? loadId;

    /// <summary>
    /// Same object <c>Current.Game.GetComponent&lt;GameComponent_IdealTracker&gt;()</c>
    /// would return, but only one linear component scan per game instance.
    /// </summary>
    internal static GameComponent_IdealTracker? Current()
    {
        Game? game = Verse.Current.Game;
        if (game == null)
        {
            if (Volatile.Read(ref cache) != null)
            {
                Reset();
            }

            return null;
        }

        Cache? current = Volatile.Read(ref cache);
        if (current != null && ReferenceEquals(current.Game, game) && ReferenceEquals(current.Components, game.components))
        {
            if (current.Tracker != null)
            {
                return current.Tracker;
            }
        }
        else
        {
            current = null;
        }

        List<GameComponent>? components = game.components;
        if (components == null)
        {
            return null;
        }

        GameComponent_IdealTracker? tracker = game.GetComponent<GameComponent_IdealTracker>();
        if (tracker != null)
        {
            Volatile.Write(ref cache, new Cache(game, components, tracker));
        }
        else if (current == null)
        {
            // Do not memoise "no tracker yet": the component is created with the Game,
            // and a transient null must not disable the effects for the whole session.
            Volatile.Write(ref cache, new Cache(game, components, null));
        }

        return tracker;
    }

    /// <summary>Stable <c>Thing.GetUniqueLoadID()</c> value per pawn, allocated once.</summary>
    internal static string LoadIdFor(Pawn pawn)
    {
        LoadIdEntry? entry = Volatile.Read(ref loadId);
        if (entry != null && ReferenceEquals(entry.Pawn, pawn))
        {
            return entry.LoadId;
        }

        entry = new LoadIdEntry(pawn, pawn.GetUniqueLoadID());
        Volatile.Write(ref loadId, entry);
        return entry.LoadId;
    }

    internal static void Reset()
    {
        Volatile.Write(ref cache, null);
        Volatile.Write(ref loadId, null);
    }
}
