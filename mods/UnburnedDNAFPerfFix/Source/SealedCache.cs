using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Unburned;
using Verse;

namespace UnburnedDNAFPerfFix;

public static class SealedCache
{
    private sealed class SealedEntry
    {
        internal int Expires = int.MinValue;
        internal int AtTick;
        internal bool Value;
    }
    private static readonly ConditionalWeakTable<Pawn, SealedEntry> Cache = new();
    private static readonly object DefLock = new();
    private static HashSet<ThingDef> armors = new();
    private static HashSet<ThingDef> helmets = new();
    private static List<ThingDef>? armorSource, helmetSource;
    private static readonly Action Generate = AccessTools.Method(typeof(Settings), "GenerateOptimized").CreateDelegate<Action>();

    internal static void Install(Harmony h)
    {
        Rebuild();
        h.Patch(AccessTools.Method(typeof(StatWorker_Flammability), nameof(StatWorker_Flammability.IsSealed)),
            prefix: new HarmonyMethod(typeof(SealedCache), nameof(Prefix)));
        foreach (string method in new[] { "DoEnableApparel", "DoDisableApparel", "GenerateDefaults" })
            h.Patch(AccessTools.Method(typeof(Settings), method), postfix: new HarmonyMethod(typeof(SealedCache), nameof(SettingsChanged)));
        h.Patch(AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelChanged)),
            postfix: new HarmonyMethod(typeof(SealedCache), nameof(ApparelChanged)));
        // Nothing writes the old strong dictionary after the replacement is installed.
        ((System.Collections.IDictionary)AccessTools.Field(typeof(StatWorker_Flammability), "sealedCache").GetValue(null)!).Clear();
    }

    private static bool rebuilding;
    private static void Rebuild()
    {
        lock (DefLock)
        {
            if (rebuilding) return;
            rebuilding = true;
            try
            {
                if (Settings.powerArmorsOptimized == null || Settings.powerHelmetsOptimized == null) Generate();
                armorSource = Settings.powerArmorsOptimized;
                helmetSource = Settings.powerHelmetsOptimized;
                armors = new HashSet<ThingDef>(armorSource ?? []);
                helmets = new HashSet<ThingDef>(helmetSource ?? []);
            }
            finally { rebuilding = false; }
        }
    }

    private static void SettingsChanged() { Rebuild(); Cache.Clear(); }
    private static void ApparelChanged(Pawn ___pawn) => Cache.Remove(___pawn);

    public static bool Prefix(Pawn? pawn, ref bool __result)
    {
        __result = false;
        if (pawn?.apparel == null || pawn.apparel.WornApparelCount == 0) return false;
        SealedEntry entry = Cache.GetValue(pawn, static _ => new());
        lock (entry)
        {
            int now = Find.TickManager.TicksGame;
            if (now >= entry.AtTick && now < entry.Expires) { __result = entry.Value; return false; }
            HashSet<ThingDef> a, h;
            lock (DefLock)
            {
                if (!ReferenceEquals(armorSource, Settings.powerArmorsOptimized) || !ReferenceEquals(helmetSource, Settings.powerHelmetsOptimized)) Rebuild();
                a = armors; h = helmets;
            }
            bool armor = false, helmet = false;
            List<Apparel> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                ThingDef def = worn[i].def;
                armor |= a.Contains(def); helmet |= h.Contains(def);
                if (armor && helmet) break;
            }
            entry.AtTick = now;
            entry.Expires = now + 900 + pawn.GetHashCode() % 63;
            __result = entry.Value = armor && helmet;
        }
        return false;
    }
}
