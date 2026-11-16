using System;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    /// <summary>
    /// RimWorld funnels every hediff add/remove and every save load through HediffSet.DirtyCache:
    /// HediffSet.AddDirect / RemoveDirect / Clear / ExposeData all call it, and Pawn_HealthTracker
    /// calls it after AddHediff/RemoveHediff. That makes it the single reliable invalidation point
    /// for the per-pawn hediff answers cached in HediffFlagCache.
    /// </summary>
    internal static class HediffCachePatches
    {
        internal static bool Installed { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            System.Reflection.MethodInfo? dirtyCache = AccessTools.Method(typeof(HediffSet), "DirtyCache", Type.EmptyTypes);
            if (dirtyCache == null)
            {
                SafeLog.Error("HediffSet.DirtyCache was not found; the hediff flag cache cannot be invalidated safely.");
                return;
            }

            try
            {
                harmony.Patch(dirtyCache, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(HediffCachePatches), nameof(DirtyCachePostfix))));
                Installed = true;
            }
            catch (Exception exception)
            {
                SafeLog.Error("HediffSet.DirtyCache postfix could not be installed.", exception);
            }
        }

        private static void DirtyCachePostfix()
        {
            HediffFlagCache.Invalidate();
        }
    }
}
