using System;
using HarmonyLib;
using Verse;
using WhiteGiver.DMSSRUnifiedFix.Neuroglue;
using WhiteGiver.DMSSRUnifiedFix.StatPathPerf;

namespace WhiteGiver.DMSSRUnifiedFix;

/// <summary>One health invalidation hook for both independent lookup caches.</summary>
internal static class HediffCachePatches
{
    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache), Type.EmptyTypes)
            ?? throw new MissingMethodException("HediffSet.DirtyCache()"),
            postfix: new HarmonyMethod(typeof(HediffCachePatches), nameof(DirtyCachePostfix)));
        Installed = true;
    }

    private static void DirtyCachePostfix(HediffSet __instance)
    {
        HediffFlagCache.Invalidate();
        MechBiogelCache.Invalidate(__instance);
    }
}
