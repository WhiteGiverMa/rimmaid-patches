using System;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    /// <summary>
    /// Standalone Harmony performance patch for
    /// "The Dead Man's Switch - Synthriada" (Cephalopod.DeadManSwitch.Synthriada, Workshop 3835512735).
    ///
    /// Runtime caches and removal of the duplicate terrain clamp; no save fields.
    /// </summary>
    public sealed class DMSSRStatPathPerfFixMod : Mod
    {
        internal const string HarmonyId = "whitegiver.dmssr.statpathperffix";

        private static bool applied;

        public DMSSRStatPathPerfFixMod(ModContentPack content) : base(content)
        {
            // DMSSR installs PatchAll during StaticConstructorOnStartup, not in its Mod ctor.
            // Installing here used to miss its static path postfix and permanently select fallback.
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
                if (!HediffCachePatches.Installed) return;
                PathCostPatches.Apply(harmony);
                MechStatPatch.Apply(harmony);
                MechLookupPatches.Apply(harmony);
                CapacityPatches.Apply(harmony);
                RenderPatches.Apply(harmony);
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
                + " dirtyCacheHook=" + HediffCachePatches.Installed
                + " mechHelpers=" + MechLookupPatches.HelperCount
                + " capacityCache=" + CapacityPatches.Installed
                + " renderCache=" + RenderPatches.Installed);
        }
    }

    [StaticConstructorOnStartup]
    internal static class Startup
    {
        static Startup()
        {
            // Startup types are not ordered. Explicitly complete upstream bootstrap first, so this
            // also works when our cctor is visited before DMSSR.LocalMeshHarmony / ModStartup.
            System.Reflection.MethodInfo? ensure = typeof(DMSSR.Patch_PawnCapacity_Calc).Assembly
                .GetType("DMSSR.Misc.HarmonyBootstrap")?.GetMethod("EnsurePatched",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (ensure == null)
            {
                SafeLog.Error("DMSSR.Misc.HarmonyBootstrap.EnsurePatched was not found; installation skipped.");
                return;
            }
            ensure.Invoke(null, null);
            DMSSRStatPathPerfFixMod.ApplyPatches();
        }
    }
}
