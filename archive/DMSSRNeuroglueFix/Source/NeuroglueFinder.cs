using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSSRNeuroglueFix
{
    /// <summary>
    /// Allocation-free lookup over <c>map.listerThings.ThingsOfDef(Neuroglue)</c>.
    /// Replaces upstream's <c>AllThings.Where(...).OrderBy(...)</c> full-map scan and skips
    /// vanilla's 30-region BFS pre-pass because colony drug stacks are small and localized.
    /// </summary>
    internal static class NeuroglueFinder
    {
        internal static int ThingsOfDefQueries { get; private set; }
        internal static int ReachabilityChecks { get; private set; }

        internal static void ResetCounters()
        {
            ThingsOfDefQueries = 0;
            ReachabilityChecks = 0;
        }

        internal static bool IsAvailable(Pawn actor, Thing? item)
        {
            if (item == null || item.Destroyed || !item.Spawned || item.stackCount <= 0)
            {
                return false;
            }

            if (item.IsForbidden(actor))
            {
                return false;
            }

            if (!actor.CanReserve(item, maxPawns: 1, stackCount: 1, layer: null, ignoreOtherReservations: false))
            {
                return false;
            }

            ReachabilityChecks++;
            return actor.CanReach(item, PathEndMode.ClosestTouch, Danger.Some);
        }

        internal static Thing? FindClosestAvailable(Pawn actor)
        {
            Map? map = actor.Map;
            if (map == null || !actor.Spawned)
            {
                return null;
            }

            NeuroglueDefs.EnsureInitialized();
            ThingDef? neuroglueDef = NeuroglueDefs.Neuroglue;
            if (neuroglueDef == null)
            {
                return null;
            }

            ThingsOfDefQueries++;
            List<Thing>? candidates = map.listerThings.ThingsOfDef(neuroglueDef);
            if (candidates == null || candidates.Count == 0)
            {
                return null;
            }

            IntVec3 actorPos = actor.Position;
            Thing? best = null;
            int bestDistSq = int.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                Thing? candidate = candidates[i];
                if (candidate == null
                    || !ReferenceEquals(candidate.def, neuroglueDef)
                    || candidate.Destroyed
                    || !candidate.Spawned
                    || candidate.stackCount <= 0)
                {
                    continue;
                }

                int distSq = (candidate.Position - actorPos).LengthHorizontalSquared;
                if (distSq >= bestDistSq)
                {
                    continue;
                }

                if (!IsAvailable(actor, candidate))
                {
                    continue;
                }

                best = candidate;
                bestDistSq = distSq;
                if (bestDistSq <= 1)
                {
                    break;
                }
            }

            return best;
        }
    }
}
