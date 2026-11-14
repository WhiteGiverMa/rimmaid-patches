using System;
using HarmonyLib;
using RimWorld;
using VFM_VanillaFireModes.Settings;
using VFM_VanillaFireModes.Utilities;
using Verse;

namespace WhiteGiver.VanillaFireModesPerfFix;

/// <summary>
/// Drop-in replacement for the three hot Harmony patches of Vanilla Fire Modes
/// (<c>VFM_VanillaFireModes.Patches.Patch_BurstShotCount</c>). The observable values are VFM's: the burst length
/// is computed once, when the verb finishes its warm-up, from the pawn's fire mode and the equipped weapon, and
/// that frozen value is what <see cref="Verb.BurstShotCount"/> returns for the whole burst.
///
/// What changes is <em>when the cached value may be consulted</em>:
/// <list type="bullet">
///   <item>VFM probed a <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/> on
///   <b>every</b> <see cref="Verb.VerbTick"/> of <b>every</b> verb in the colony just to find the few verbs that
///   had left <see cref="VerbState.Bursting"/> — roughly 10,000 probes per frame with zero bursts running.</item>
///   <item>This class deletes that tick hook entirely and decides validity from state that is already in the
///   <see cref="Verb"/>: the value applies while the verb is <see cref="VerbState.Bursting"/>, or during the
///   warm-up call that is about to consume it. Everything else returns the vanilla value, so a stale entry can
///   never be observed and no lookup is needed to decide that.</item>
/// </list>
/// </summary>
internal static class BurstShotCountPatches
{
    /// <summary>Harmony id used by Vanilla Fire Modes itself (<c>new Harmony("Aliza.VanillaFireModes")</c>).</summary>
    internal const string VfmHarmonyId = "Aliza.VanillaFireModes";

    /// <summary>
    /// Nesting depth of <see cref="Verb.WarmupComplete"/> calls. Non-zero only for the duration of one warm-up,
    /// which is the one moment where <see cref="Verb.BurstShotCount"/> is read while the verb is not bursting yet
    /// (<c>Verb.WarmupComplete</c> assigns <c>burstShotsLeft = ShotsPerBurst</c> before it sets
    /// <see cref="VerbState.Bursting"/>).
    /// </summary>
    private static int warmupDepth;

    /// <summary>Diagnostic accessor used by the offline harness.</summary>
    internal static int WarmupDepth => warmupDepth;

    /// <summary>
    /// Replaces VFM's <c>LockCount</c>. Runs before the vanilla warm-up body: drop the previous burst, then freeze
    /// the fire-mode burst length for the burst that is starting. VFM read the vanilla getter for its base value
    /// only after invalidating the cache; the invalidation is kept first here for the same reason.
    /// </summary>
    internal static void LockCount(Verb __instance)
    {
        warmupDepth++;
        BurstCountCache.Clear(__instance);

        if (!ShouldModify(__instance, out Pawn? pawn, out ThingWithComps? weapon))
        {
            return;
        }

        int count = FireModeDB.GetBurstCount(
            pawn!.VFM_GetFireMode(),
            baseBurstCount: __instance.BurstShotCount,
            weaponDefName: weapon!.def.defName);

        BurstCountCache.Lock(__instance, Math.Max(1, count));
    }

    /// <summary>
    /// Closes the warm-up window even when the vanilla body throws. A finalizer (not a postfix) is used on
    /// purpose: postfixes are skipped when the original throws, which would leave the window open forever.
    /// </summary>
    internal static void UnlockWarmup()
    {
        if (warmupDepth > 0)
        {
            warmupDepth--;
        }
    }

    /// <summary>
    /// Replaces VFM's <c>BurstShotCountPostFix</c>. Two field reads reject every read that cannot possibly be a
    /// locked burst, so an idle or melee verb never touches the table. The result is byte-for-byte what VFM
    /// returned while a burst is running, including the live <c>ShouldModify</c> re-check.
    /// </summary>
    internal static void ApplyLockedCount(Verb __instance, ref int __result)
    {
        if (__instance.state != VerbState.Bursting && warmupDepth == 0)
        {
            return;
        }

        if (!BurstCountCache.TryGet(__instance, out BurstCountState? state) || state.Count == 0)
        {
            return;
        }

        if (!ShouldModify(__instance, out Pawn? _, out ThingWithComps? _))
        {
            return;
        }

        __result = state.Count;
    }

    /// <summary>
    /// VFM's <c>ShouldModify</c>, re-implemented with the cheap rejections first. Every branch is a pure property
    /// read; there are no allocations and no reflection. These are exactly VFM's eligibility rules, so the set of
    /// verbs that receive a fire-mode burst length is unchanged.
    /// </summary>
    internal static bool ShouldModify(Verb verb, out Pawn? pawn, out ThingWithComps? weapon)
    {
        pawn = null;
        weapon = null;

        if (verb.verbProps == null || verb.verbProps.IsMeleeAttack)
        {
            return false;
        }

        if (verb is Verb_ShootOneUse)
        {
            return false;
        }

        // Verb.EquipmentSource dereferences verbTracker.directOwner; a verb without a tracker cannot be a
        // pawn weapon at all. VFM would throw here, this guard simply rejects.
        if (verb.verbTracker == null || verb.CasterPawn is not Pawn caster)
        {
            return false;
        }

        if (!caster.VFM_ShouldApplyFireMode())
        {
            return false;
        }

        ThingWithComps? equipment = verb.EquipmentSource;
        if (equipment?.def == null || !equipment.def.IsRangedWeapon || GenText.NullOrEmpty(equipment.def.defName))
        {
            return false;
        }

        pawn = caster;
        weapon = equipment;
        return true;
    }
}
