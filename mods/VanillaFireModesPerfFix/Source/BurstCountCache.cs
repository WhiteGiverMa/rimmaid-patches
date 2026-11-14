using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Verse;

namespace WhiteGiver.VanillaFireModesPerfFix;

/// <summary>
/// Per-<see cref="Verb"/> burst length that was locked in by <see cref="BurstShotCountPatches"/> when the verb
/// finished its warm-up. One instance is created per verb that actually fires a fire-mode-modified burst and is
/// dropped together with the verb (weak key), exactly like Vanilla Fire Modes' own table.
/// </summary>
internal sealed class BurstCountState
{
    /// <summary>Locked burst length; <c>0</c> means "no locked burst", which is also the neutral value.</summary>
    internal int Count;
}

/// <summary>
/// The replacement storage for <c>VFM_VanillaFireModes.Patches.Patch_BurstShotCount._burstCache</c>.
///
/// A <see cref="ConditionalWeakTable{TKey,TValue}"/> is kept because it is the only per-verb storage that does
/// not leak verbs (and through them pawns, maps and the whole colony) after a pawn is destroyed. The cost of a
/// table probe is what this mod removes: no <see cref="Verb.VerbTick"/> hook exists any more, and the getter
/// postfix only reaches this table while a verb is inside a burst or inside its own warm-up, so an idle colony
/// performs zero probes per frame. <see cref="Probes"/> counts every table access and exists so the offline
/// harness can assert that number instead of trusting a comment.
/// </summary>
internal static class BurstCountCache
{
    private static readonly ConditionalWeakTable<Verb, BurstCountState> Cache = new();

    private static readonly ConditionalWeakTable<Verb, BurstCountState>.CreateValueCallback Factory =
        static _ => new BurstCountState();

    /// <summary>Diagnostic counter: every <see cref="ConditionalWeakTable{TKey,TValue}"/> access in this mod.</summary>
    internal static int Probes;

    internal static void ResetProbes()
    {
        Probes = 0;
    }

    internal static bool TryGet(Verb verb, [NotNullWhen(true)] out BurstCountState? state)
    {
        Probes++;
        return Cache.TryGetValue(verb, out state);
    }

    /// <summary>Drops a locked burst. Allocation-free and cheap when the verb never locked one.</summary>
    internal static void Clear(Verb verb)
    {
        if (TryGet(verb, out BurstCountState? state))
        {
            state.Count = 0;
        }
    }

    /// <summary>Stores the burst length that the verb must use for the burst that is starting now.</summary>
    internal static void Lock(Verb verb, int count)
    {
        if (!TryGet(verb, out BurstCountState? state))
        {
            state = Cache.GetValue(verb, Factory);
        }

        state.Count = count;
    }
}
