using System;
using System.Collections.Concurrent;
using HARAnimate;
using HARAnimate.Animation;
using Verse;

namespace WhiteGiver.HARAnimatePerfFix
{
    /// <summary>
    /// Replacement cache key for <see cref="AnimatorCache" />.
    ///
    /// Upstream <c>AnimatorCache.CacheKey</c> is a plain struct that overrides
    /// <see cref="object.Equals(object)" /> / <see cref="object.GetHashCode" /> but does NOT implement
    /// <see cref="IEquatable{T}" />. <c>ConcurrentDictionary&lt;CacheKey, CacheEntry&gt;</c> therefore
    /// resolves its comparer to <c>ObjectEqualityComparer&lt;CacheKey&gt;</c>, which boxes the 32-byte key
    /// once for <c>GetHashCode()</c> and once per bucket <c>Equals()</c> on every lookup.
    ///
    /// This struct implements <see cref="IEquatable{T}" /> so the generic comparer is used and the key
    /// stays on the stack. Equality is ordinal: a null label and an empty label stay distinct here,
    /// exactly like upstream's <c>label == other.label</c>.
    /// </summary>
    internal readonly struct FastCacheKey : IEquatable<FastCacheKey>
    {
        internal readonly int PawnId;
        internal readonly int AddonId;
        internal readonly string? Label;

        internal FastCacheKey(int pawnId, int addonId, string? label)
        {
            PawnId = pawnId;
            AddonId = addonId;
            Label = label;
        }

        public bool Equals(FastCacheKey other)
        {
            return PawnId == other.PawnId
                   && AddonId == other.AddonId
                   && string.Equals(Label, other.Label, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is FastCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = PawnId;
                hash = (hash * 397) ^ AddonId;
                hash = (hash * 397) ^ (Label is null ? 0 : StringComparer.Ordinal.GetHashCode(Label));
                return hash & 0x7fffffff;
            }
        }
    }

    /// <summary>
    /// One cached node transform. <see cref="Slot" />, <see cref="LastUpdateTick" /> and
    /// <see cref="Value" /> are only ever touched while holding <c>lock (entry)</c>, so a refresh on
    /// one render thread can never be observed half-written by another render thread. The lock object
    /// is the entry itself: different (pawn, addon, label) triples never contend.
    /// </summary>
    internal sealed class FastCacheEntry
    {
        internal int Slot;
        internal int LastUpdateTick;
        internal BodyAddonTransform Value;
    }

    /// <summary>
    /// Behaviour-preserving replacement for <see cref="AnimatorCache" />'s storage.
    /// Same slot math, same refresh rule, same 15,000 tick expiry rule, same defaults.
    /// </summary>
    internal static class FastAnimatorCache
    {
        private static readonly ConcurrentDictionary<FastCacheKey, FastCacheEntry> Entries = new();

        internal static int Count => Entries.Count;

        internal static BodyAddonTransform Get(
            Pawn pawn,
            PawnDrawParms parms,
            int addonId,
            string? label,
            int tick)
        {
            FastCacheKey key = new FastCacheKey(pawn.thingIDNumber, addonId, label);

            // Upstream divides by HARAnimateSettings.CacheRefreshTick without a guard, so a hand-edited
            // settings file with 0 throws DivideByZeroException inside the render loop. Clamp instead.
            int refreshTicks = HARAnimateSettings.CacheRefreshTick;
            if (refreshTicks < 1)
            {
                refreshTicks = 1;
            }

            int slot = tick / refreshTicks;

            // Fast path: no allocation, no boxing, no delegate. GetOrAdd only runs on a cold entry.
            if (!Entries.TryGetValue(key, out FastCacheEntry? entry))
            {
                entry = Entries.GetOrAdd(key, static _ => new FastCacheEntry());
            }

            FastCacheEntry current = entry;

            // Per-entry lock. Zero contention between different nodes, and it makes the 24-byte
            // BodyAddonTransform refresh atomic with respect to readers of the same node. Upstream
            // writes entry.slot before entry.value, so a concurrent reader can pass the slot check and
            // read a half-written struct (Vector3 = 12 bytes, not a single atomic store).
            lock (current)
            {
                if (current.Slot == slot)
                {
                    return current.Value;
                }

                current.Slot = slot;
                current.LastUpdateTick = tick;

                // Call through the real method: if another mod also patches AnimatorSystem.Evaluate,
                // this cache keeps honouring it. Our own prefix makes it allocation free.
                current.Value = AnimatorSystem.Evaluate(pawn, parms, label!, tick);
                return current.Value;
            }
        }

        internal static void Cleanup(int currentTick, int maxAge)
        {
            if (Entries.IsEmpty)
            {
                return;
            }

            int minValidTick = currentTick - maxAge;

            // ConcurrentDictionary supports lock-free enumeration by design. Upstream materialises
            // cache.ToArray() first, which copies every entry into a fresh array every 30,000 ticks.
            foreach (var pair in Entries)
            {
                // int reads are atomic; a stale read only delays removal to the next sweep. Removing an
                // entry that a render thread is concurrently refreshing is harmless: that thread still
                // returns its freshly evaluated value and the next lookup re-inserts the entry.
                if (pair.Value.LastUpdateTick < minValidTick)
                {
                    Entries.TryRemove(pair.Key, out _);
                }
            }
        }

        internal static void CleanAll()
        {
            Entries.Clear();
            Log.Message("[HARAnimate]Cache clear");
        }
    }
}
