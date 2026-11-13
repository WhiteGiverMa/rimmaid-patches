using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using HarmonyLib;
using HARAnimate;
using HARAnimate.Animation;

namespace WhiteGiver.HARAnimatePerfFix
{
    /// <summary>
    /// Zero-allocation lookup key for <see cref="AnimatorSystem" />'s registered stacks.
    ///
    /// Upstream builds <c>pawn.def.defName + ":" + label</c> before every dictionary probe, which
    /// allocates a brand new string (64 bytes for a typical race/label pair) on every render node,
    /// every frame. This struct lets the probe run on the two existing string references instead.
    ///
    /// <c>null</c> and <c>""</c> are normalised to the same value because upstream's concatenation maps
    /// both to <c>"race:"</c>.
    /// </summary>
    internal readonly struct StackLookupKey : IEquatable<StackLookupKey>
    {
        internal readonly string Race;
        internal readonly string Label;

        internal StackLookupKey(string? race, string? label)
        {
            Race = race ?? string.Empty;
            Label = label ?? string.Empty;
        }

        public bool Equals(StackLookupKey other)
        {
            return string.Equals(Race, other.Race, StringComparison.Ordinal)
                   && string.Equals(Label, other.Label, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is StackLookupKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (StringComparer.Ordinal.GetHashCode(Race) * 397)
                       ^ StringComparer.Ordinal.GetHashCode(Label);
            }
        }
    }

    /// <summary>
    /// Mirror of <c>AnimatorSystem.stacks</c> (a private <c>Dictionary&lt;string, AnimatorStack&gt;</c>
    /// keyed by <c>"race:label"</c>) that can be probed without building the composite string.
    ///
    /// Two maps are kept:
    ///  * <see cref="ByRaceAndLabel" /> — the fast path, filled by splitting each registration at its
    ///    single ':'.
    ///  * <see cref="ByRawKey" /> — an exact copy of the upstream key, used as a fallback if any
    ///    registration ever contains more than one ':' (a colon inside a race defName or inside a
    ///    translated body-part label). In that case every lookup goes through the exact string, i.e.
    ///    exactly what upstream does, so behaviour is preserved even for pathological keys.
    /// </summary>
    internal static class FastAnimatorSystem
    {
        private static readonly ConcurrentDictionary<StackLookupKey, AnimatorStack> ByRaceAndLabel = new();
        private static readonly ConcurrentDictionary<string, AnimatorStack> ByRawKey = new(StringComparer.Ordinal);
        private static readonly object SyncGate = new();

        /// <summary>1 while every registration had exactly one ':' (fast keys are exact), 0 otherwise.</summary>
        private static int unambiguous = 1;

        /// <summary>Number of upstream <c>stacks</c> entries mirrored by the last sync.</summary>
        private static int mirroredCount = -1;

        private static AccessTools.FieldRef<object, Dictionary<string, AnimatorStack>>? stacksAccessor;

        internal static int MirroredCount => Volatile.Read(ref mirroredCount);

        internal static bool HasAmbiguousKeys => Volatile.Read(ref unambiguous) == 0;

        /// <summary>
        /// Mirrors one registration. Called from the <c>AnimatorSystem.Register</c> prefix, and from the
        /// startup scan for registrations that happened before this mod was patched in.
        /// </summary>
        internal static void Mirror(string? raceAndLabel, AnimatorStack? stack)
        {
            if (raceAndLabel is null || stack is null)
            {
                return;
            }

            ByRawKey[raceAndLabel] = stack;

            int firstColon = raceAndLabel.IndexOf(':');

            if (firstColon < 0 || firstColon != raceAndLabel.LastIndexOf(':'))
            {
                // More than one ':' (or none): we cannot split this key without guessing. Mark the whole
                // lookup path ambiguous so TryGet falls back to the exact upstream concatenation.
                Volatile.Write(ref unambiguous, 0);
                return;
            }

            ByRaceAndLabel[new StackLookupKey(
                raceAndLabel.Substring(0, firstColon),
                raceAndLabel.Substring(firstColon + 1))] = stack;
        }

        /// <summary>
        /// Mirrors every entry currently present in <c>AnimatorSystem.stacks</c>.
        ///
        /// Called from the Mod constructor and from <c>[StaticConstructorOnStartup]</c>, i.e. after
        /// HARAnimate's own <c>[StaticConstructorOnStartup]</c> registration pass. Startup scan and
        /// Register prefix together cover both possible load orders:
        ///  * HARAnimate registers first -> the scan mirrors those entries,
        ///  * anything registers later     -> the Register prefix mirrors it.
        /// </summary>
        internal static void Sync()
        {
            Dictionary<string, AnimatorStack>? stacks = Stacks();
            if (stacks is null)
            {
                return;
            }

            lock (SyncGate)
            {
                KeyValuePair<string, AnimatorStack>[] snapshot;
                try
                {
                    // Only runs at startup or after an out-of-band change to the private dictionary, so
                    // the array copy is irrelevant. Copying also keeps us safe from a concurrent write.
                    snapshot = new KeyValuePair<string, AnimatorStack>[stacks.Count];
                    ((ICollection<KeyValuePair<string, AnimatorStack>>)stacks).CopyTo(snapshot, 0);
                }
                catch (Exception)
                {
                    return;
                }

                for (int i = 0; i < snapshot.Length; i++)
                {
                    Mirror(snapshot[i].Key, snapshot[i].Value);
                }

                Volatile.Write(ref mirroredCount, stacks.Count);
            }
        }

        /// <summary>
        /// Mirrors one live registration and refreshes the observed upstream count so that
        /// <see cref="SyncIfCountChanged" /> does not rescan for a change we already handled.
        /// The prefix runs before the upstream write and the postfix after it, so the value settles on
        /// the post-prefix count; if only the prefix ran, the one-off difference merely triggers one
        /// harmless rescan later.
        /// </summary>
        internal static void MirrorAndRecount(string? raceAndLabel, AnimatorStack? stack)
        {
            Mirror(raceAndLabel, stack);

            Dictionary<string, AnimatorStack>? stacks = Stacks();
            if (stacks is not null)
            {
                Volatile.Write(ref mirroredCount, stacks.Count);
            }
        }

        /// <summary>
        /// Re-syncs only when the upstream dictionary grew or shrank outside our Register prefix
        /// (e.g. another mod wrote the private field by reflection). One int read per miss.
        /// </summary>
        internal static void SyncIfCountChanged()
        {
            Dictionary<string, AnimatorStack>? stacks = Stacks();
            if (stacks is null)
            {
                return;
            }

            if (stacks.Count == Volatile.Read(ref mirroredCount))
            {
                return;
            }

            Sync();
        }

        internal static bool TryGet(string? race, string? label, out AnimatorStack? stack)
        {
            if (Volatile.Read(ref unambiguous) != 0)
            {
                return ByRaceAndLabel.TryGetValue(new StackLookupKey(race, label), out stack);
            }

            if (race is null)
            {
                stack = null;
                return false;
            }

            // Same string the upstream method builds: race + ":" + label (null label => "race:").
            return ByRawKey.TryGetValue(race + ":" + label, out stack);
        }

        private static Dictionary<string, AnimatorStack>? Stacks()
        {
            AccessTools.FieldRef<object, Dictionary<string, AnimatorStack>>? accessor = stacksAccessor;

            if (accessor is null)
            {
                try
                {
                    accessor = AccessTools.FieldRefAccess<Dictionary<string, AnimatorStack>>(
                        typeof(AnimatorSystem),
                        "stacks");
                }
                catch (Exception)
                {
                    return null;
                }

                stacksAccessor = accessor;
            }

            try
            {
                return accessor(null!);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
