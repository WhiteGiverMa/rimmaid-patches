using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CeleTech.Base;
using Verse;

namespace WhiteGiver.CeleTechTurretUpgradePerfFix;

/// <summary>
/// Per-map registry of spawned <see cref="Comp_TurretUpgrade"/> instances.
///
/// The registry is filled lazily from <see cref="ListerThings.ThingsInGroup"/> once per map (this catches
/// turrets that already exist when a save is loaded) and incrementally from
/// <see cref="Comp_TurretUpgrade.PostSpawnSetup"/> (this catches everything spawned afterwards). Query methods
/// prune entries whose parent has been destroyed, despawned or moved to another map, so the registry never
/// answers from stale state.
///
/// <para><b>Why the answer is exact.</b> <c>WorkGiver_TurretUpgrade.HasJobOnThing</c> returns false immediately
/// when the target has no <see cref="Comp_TurretUpgrade"/> or its <c>queued</c> list is empty. Therefore
/// "no tracked comp on this map has a non-empty queued list" implies that upstream would reject every single
/// <c>ThingRequestGroup.BuildingArtificial</c> candidate. The converse is guaranteed by registration: every
/// comp that exists on a map is either found by the one-time lister scan or registered by its spawn setup.</para>
///
/// <para>RimWorld runs map work on the main thread; the lock only protects the registry against a mod that
/// spawns things from another thread and costs one uncontended monitor enter per query.</para>
/// </summary>
public static class MapTurretUpgradeTracker
{
    private sealed class MapState
    {
        internal readonly List<Comp_TurretUpgrade> Comps = new();
        internal bool Initialized;
    }

    private static readonly ConditionalWeakTable<Map, MapState> States = new();

    // The giver's PotentialWorkThingRequest. Filtering the search set with the same request keeps the
    // optimization from ever proposing a thing that upstream's normal work scan could not see.
    private static readonly ThingRequest BuildingArtificialRequest =
        ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial);

    /// <summary>
    /// True when at least one live <see cref="Comp_TurretUpgrade"/> on <paramref name="map"/> has a queued
    /// upgrade. A null map returns false (there is no work on a null map). A map whose lister is not
    /// available yet returns true so callers keep the upstream behaviour instead of skipping a possible job.
    /// </summary>
    public static bool HasAnyQueuedUpgrade(Map? map)
    {
        if (map == null)
        {
            return false;
        }

        MapState state = GetState(map);
        EnsureInitialized(map, state);

        lock (state)
        {
            if (!state.Initialized)
            {
                // Conservative fallback: never claim "nothing queued" when the map could not be enumerated.
                return true;
            }

            for (int i = state.Comps.Count - 1; i >= 0; i--)
            {
                Comp_TurretUpgrade comp = state.Comps[i];
                Thing? parent = comp.parent;

                if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map != map)
                {
                    state.Comps.RemoveAt(i);
                    continue;
                }

                List<TurretUpgradeDef>? queued = comp.queued;
                if (queued != null && queued.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// The exact set of spawned turret buildings on <paramref name="map"/> whose <c>queued</c> list is
    /// non-empty, in registry order. Returns an empty list when nothing is queued, and <c>null</c> when the
    /// map could not be enumerated (the caller must then fall back to the original method).
    /// </summary>
    public static IReadOnlyList<Thing>? GetQueuedTurrets(Map? map)
    {
        if (map == null)
        {
            return null;
        }

        MapState state = GetState(map);
        EnsureInitialized(map, state);

        lock (state)
        {
            if (!state.Initialized)
            {
                return null;
            }

            List<Thing>? result = null;

            // Forward order keeps the list in lister/registration order, matching the order in which the
            // upstream global search would encounter the same turrets.
            for (int i = 0; i < state.Comps.Count;)
            {
                Comp_TurretUpgrade comp = state.Comps[i];
                Thing? parent = comp.parent;

                if (parent == null || parent.Destroyed || !parent.Spawned || parent.Map != map)
                {
                    state.Comps.RemoveAt(i);
                    continue;
                }

                List<TurretUpgradeDef>? queued = comp.queued;
                if (queued != null && queued.Count > 0 && parent.def != null && BuildingArtificialRequest.Accepts(parent))
                {
                    result ??= new List<Thing>();
                    result.Add(parent);
                }

                i++;
            }

            return result == null ? Array.Empty<Thing>() : result;
        }
    }

    /// <summary>
    /// Called from the <see cref="Comp_TurretUpgrade.PostSpawnSetup"/> postfix. Duplicate registration is
    /// harmless; the lazy lister scan and a spawn may both observe the same comp.
    /// </summary>
    public static void Register(Comp_TurretUpgrade? comp)
    {
        if (comp == null)
        {
            return;
        }

        Thing? parent = comp.parent;
        if (parent == null)
        {
            return;
        }

        Map? map = parent.Map;
        if (map == null)
        {
            return;
        }

        MapState state = GetState(map);
        lock (state)
        {
            if (!state.Comps.Contains(comp))
            {
                state.Comps.Add(comp);
            }
        }
    }

    private static MapState GetState(Map map)
    {
        return States.GetValue(map, static _ => new MapState());
    }

    private static void EnsureInitialized(Map map, MapState state)
    {
        lock (state)
        {
            if (state.Initialized)
            {
                return;
            }

            if (map.listerThings == null)
            {
                // Map is still being constructed. Leave Initialized false; queries stay conservative.
                return;
            }

            List<Thing> buildings;
            try
            {
                buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce(TurretUpgradePerfFix.LogPrefix + " could not enumerate BuildingArtificial things; the fast path stays conservative: " + exception, 84739201);
                return;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                Comp_TurretUpgrade? comp = buildings[i].TryGetComp<Comp_TurretUpgrade>();
                if (comp != null && !state.Comps.Contains(comp))
                {
                    state.Comps.Add(comp);
                }
            }

            state.Initialized = true;
        }
    }
}
