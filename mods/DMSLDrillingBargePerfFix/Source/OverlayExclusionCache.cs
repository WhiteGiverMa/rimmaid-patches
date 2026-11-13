using System;
using System.Collections.Generic;
using DMS_Legion;
using Verse;

namespace WhiteGiver.DMSL.DrillingBargePerfFix;

/// <summary>
/// Replacement for <c>CompDrillingBargeDeepResourceOverlay.IsExcludedDeepResource</c>.
///
/// Upstream resolves <c>DefDatabase&lt;ThingDef&gt;.GetNamedSilentFail("DMSL_Mech_DrillingBarge")</c> and walks
/// <c>ThingDef.comps</c> on *every* call. That method sits inside the 21-cell radial loop of
/// <c>TryGetDeepResource</c>, so a single full-map work scan performs roughly 105,625 x 21 lookups.
///
/// This cache resolves the def once and only re-resolves when the ThingDef database size changes
/// (i.e. defs were reloaded). The comps list, the overlay CompProperties and its
/// <c>excludedDeepResourceDefNames</c> list are additionally validated by reference and count, so a
/// mod that swaps or re-populates the list at runtime is still observed.
/// Matching semantics are identical to upstream: the *first* overlay CompProperties wins, and the
/// comparison is <see cref="StringComparer.Ordinal"/> (the default comparer of List&lt;string&gt;.Contains).
/// </summary>
internal static class OverlayExclusionCache
{
    private static ThingDef? cachedDef;
    private static int cachedDefCount = -1;

    private static List<CompProperties>? cachedComps;
    private static List<string>? cachedNames;
    private static int cachedNameCount = -1;
    private static HashSet<string>? cachedExcludedNames;

    /// <summary>True when the def is present and its first overlay comp excludes <paramref name="resourceDef"/>.</summary>
    internal static bool IsExcluded(ThingDef? resourceDef)
    {
        if (resourceDef == null)
        {
            return false;
        }

        HashSet<string>? excluded = GetExcludedNames();
        return excluded != null && excluded.Contains(resourceDef.defName);
    }

    private static HashSet<string>? GetExcludedNames()
    {
        ThingDef? def = ResolveBargeDef();
        if (def == null)
        {
            return null;
        }

        List<CompProperties>? comps = def.comps;
        if (comps == null)
        {
            ResetOverlayCache();
            return null;
        }

        CompProperties_DrillingBargeDeepResourceOverlay? props = FindFirstOverlayProps(comps);
        if (props == null)
        {
            cachedComps = comps;
            cachedNames = null;
            cachedNameCount = -1;
            cachedExcludedNames = null;
            return null;
        }

        List<string>? names = props.excludedDeepResourceDefNames;
        if (ReferenceEquals(comps, cachedComps)
            && ReferenceEquals(names, cachedNames)
            && (names == null ? -1 : names.Count) == cachedNameCount)
        {
            return cachedExcludedNames;
        }

        cachedComps = comps;
        cachedNames = names;
        cachedNameCount = names == null ? -1 : names.Count;

        if (names == null || names.Count == 0)
        {
            cachedExcludedNames = null;
            return null;
        }

        HashSet<string> set = new(StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            if (!string.IsNullOrEmpty(name))
            {
                set.Add(name);
            }
        }

        cachedExcludedNames = set.Count > 0 ? set : null;
        return cachedExcludedNames;
    }

    private static ThingDef? ResolveBargeDef()
    {
        int defCount = DefDatabase<ThingDef>.DefCount;
        if (cachedDef != null && cachedDefCount == defCount)
        {
            return cachedDef;
        }

        cachedDef = DefDatabase<ThingDef>.GetNamedSilentFail(DrillingBargePerfFix.DrillingBargeDefName);
        cachedDefCount = defCount;
        ResetOverlayCache();
        return cachedDef;
    }

    private static void ResetOverlayCache()
    {
        cachedComps = null;
        cachedNames = null;
        cachedNameCount = -1;
        cachedExcludedNames = null;
    }

    private static CompProperties_DrillingBargeDeepResourceOverlay? FindFirstOverlayProps(List<CompProperties> comps)
    {
        for (int i = 0; i < comps.Count; i++)
        {
            if (comps[i] is CompProperties_DrillingBargeDeepResourceOverlay overlay)
            {
                return overlay;
            }
        }

        return null;
    }
}
