using System;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    /// <summary>
    /// Standalone Harmony performance patch for
    /// "The Dead Man's Switch - Synthriada" (Cephalopod.DeadManSwitch.Synthriada, Workshop 3835512735).
    ///
    /// Nothing is scribed, no Defs are added and no behaviour that the player can observe changes:
    /// the mod only removes duplicated work and adds caches that are dropped whenever the hediff
    /// set changes.
    /// </summary>
    public sealed class DMSSRStatPathPerfFixMod : Mod
    {
        internal const string HarmonyId = "whitegiver.dmssr.statpathperffix";

        private static bool applied;

        public DMSSRStatPathPerfFixMod(ModContentPack content) : base(content)
        {
            ApplyPatches();
        }

        internal static void ApplyPatches()
        {
            if (applied)
            {
                return;
            }

            applied = true;

            Harmony harmony = new(HarmonyId);
            try
            {
                HediffCachePatches.Apply(harmony);
                PathCostPatches.Apply(harmony);
                MechStatPatch.Apply(harmony);
            }
            catch (Exception exception)
            {
                SafeLog.Error("unexpected failure while installing patches.", exception);
                return;
            }

            SafeLog.Message(
                "installed: pathCostSingleClamp=" + PathCostPatches.SingleClampInstalled
                + " pathCostCacheOnly=" + PathCostPatches.CacheOnlyFallbackInstalled
                + " delegationVerified=" + PathCostPatches.DelegationVerified
                + " statGate=" + MechStatPatch.GateInstalled
                + " mechCompCache=" + MechStatPatch.CompCacheInstalled
                + " dirtyCacheHook=" + HediffCachePatches.Installed);
        }
    }
}
