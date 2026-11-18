using MiliraTheOneEntwinedWithYou.Data;
using MiliraTheOneEntwinedWithYou.GameComponents;
using MiliraTheOneEntwinedWithYou.Rewards;
using MiliraTheOneEntwinedWithYou.Runtime;
using RimWorld;
using Verse;

namespace WhiteGiver.MTOEWYStatPerfFix;

/// <summary>
/// Byte-for-byte copy of the upstream <c>IdealExclusiveTraits.Owner</c> decision, except
/// that the game component is cached and the pawn load-id string is allocated once.
/// </summary>
internal static class FastOwner
{
    /// <summary>
    /// Equivalent to <c>IdealExclusiveTraits.Owner(pawn) != null</c>.
    /// <para>
    /// Upstream evaluates <c>IsCurrentIdeal(pawn)</c> (which calls
    /// <c>pawn.GetUniqueLoadID()</c> and allocates two/three strings) before the cheap
    /// <c>RuntimeCache.ResolvedPawn != pawn</c> check. Both are pure reads, so testing
    /// the reference first is result-identical and skips the allocation for every pawn
    /// except the one ideal.
    /// </para>
    /// </summary>
    internal static GameComponent_IdealTracker? Owner(Pawn? pawn)
    {
        if (pawn == null)
        {
            return null;
        }

        GameComponent_IdealTracker? tracker = TrackerCache.Current();
        if (tracker == null)
        {
            return null;
        }

        if (Scribe.mode != LoadSaveMode.Inactive)
        {
            return null;
        }

        IdealRuntimeCache cache = tracker.RuntimeCache;
        if (cache == null || !ReferenceEquals(cache.ResolvedPawn, pawn))
        {
            return null;
        }

        if (!tracker.State.IsCurrentIdeal(TrackerCache.LoadIdFor(pawn)))
        {
            return null;
        }

        if (tracker.State.P4Phase != IdealP4Phase.Active)
        {
            return null;
        }

        if (UpstreamRefs.HasOriginalRecoveryTransaction!(tracker))
        {
            return null;
        }

        if (pawn.Dead || pawn.Destroyed || pawn.Discarded)
        {
            return null;
        }

        return tracker;
    }
}
