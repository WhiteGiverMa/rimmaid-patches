using System;
using HarmonyLib;
using HARAnimate.Animation;
using Verse;

namespace WhiteGiver.HARAnimatePerfFix
{
    /// <summary>
    /// Replaces the whole cache body of <see cref="AnimatorCache" /> with <see cref="FastAnimatorCache" />.
    /// Signatures and return values are identical to upstream, so every caller (HARAnimate's own
    /// OffsetFor / ScaleFor / RotationFor postfixes and any third-party caller) keeps working.
    /// </summary>
    internal static class AnimatorCachePatches
    {
        internal static bool GetPrefix(
            Pawn pawn,
            PawnDrawParms parms,
            int addonId,
            string label,
            int tick,
            ref BodyAddonTransform __result)
        {
            __result = FastAnimatorCache.Get(pawn, parms, addonId, label, tick);
            return false;
        }

        internal static bool CleanupPrefix(int currentTick, int maxAge)
        {
            FastAnimatorCache.Cleanup(currentTick, maxAge);
            return false;
        }

        internal static bool CleanAllPrefix()
        {
            FastAnimatorCache.CleanAll();
            return false;
        }

        internal static bool CacheCountPrefix(ref int __result)
        {
            __result = FastAnimatorCache.Count;
            return false;
        }
    }

    /// <summary>
    /// Replaces <c>AnimatorCacheCleaner.CacheCleaner</c> so that:
    ///  * the sweep runs against the replacement cache (which is what is actually populated),
    ///  * <c>Cleanup</c> enumerates the ConcurrentDictionary directly (no <c>ToArray()</c> copy),
    ///  * the "Cache cleanup: N -> N (removed 0)" line is only emitted in dev mode and only when
    ///    something was actually removed. In 1.7 every <c>Log.Message</c> goes through
    ///    <c>LogCategory.Info</c>, which captures a full 15-frame stack trace and writes it to the log
    ///    file even when nothing changed, every 30,000 ticks.
    /// </summary>
    internal static class AnimatorCacheCleanerPatches
    {
        private const int CleanupInterval = 30000;

        private static int lastCleanupTick = -99999;

        internal static bool CacheCleanerPrefix()
        {
            int currentTick = Find.TickManager.TicksGame;

            if (currentTick - lastCleanupTick < CleanupInterval)
            {
                return false;
            }

            lastCleanupTick = currentTick;

            int before = FastAnimatorCache.Count;
            FastAnimatorCache.Cleanup(currentTick, AnimatorCache.CleanupMaxAge);

            if (Prefs.DevMode)
            {
                int after = FastAnimatorCache.Count;
                if (after != before)
                {
                    Log.Message($"[HARAnimate]Cache cleanup: {before} -> {after} (removed {before - after})");
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Replaces <c>AnimatorSystem.Evaluate</c> with a lookup that never builds
    /// <c>race + ":" + label</c>. Falls back to <see cref="BodyAddonTransform.Identity" /> exactly like
    /// upstream when the race/label pair has no registered stack.
    /// </summary>
    internal static class AnimatorSystemPatches
    {
        internal static bool EvaluatePrefix(
            Pawn pawn,
            PawnDrawParms parms,
            string label,
            int tick,
            ref BodyAddonTransform __result)
        {
            string race = pawn.def.defName;

            if (FastAnimatorSystem.TryGet(race, label, out AnimatorStack? stack))
            {
                __result = stack!.Evaluate(pawn, parms, tick, pawn.thingIDNumber);
                return false;
            }

            // Cheap: one int comparison unless the private dictionary changed behind our back.
            FastAnimatorSystem.SyncIfCountChanged();

            if (FastAnimatorSystem.TryGet(race, label, out stack))
            {
                __result = stack!.Evaluate(pawn, parms, tick, pawn.thingIDNumber);
                return false;
            }

            __result = BodyAddonTransform.Identity;
            return false;
        }

        internal static void RegisterPrefix(string raceAndLabel, AnimatorStack stack)
        {
            FastAnimatorSystem.MirrorAndRecount(raceAndLabel, stack);
        }

        /// <summary>
        /// Belt and braces: if some other prefix consumed the call and returned false (so the original
        /// Register body never ran), the postfix still ensures the mirror is complete.
        /// </summary>
        internal static void RegisterPostfix(string raceAndLabel, AnimatorStack stack)
        {
            FastAnimatorSystem.MirrorAndRecount(raceAndLabel, stack);
        }
    }
}
