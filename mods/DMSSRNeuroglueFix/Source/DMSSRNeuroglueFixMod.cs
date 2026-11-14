using System;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRNeuroglueFix
{
    public sealed class DMSSRNeuroglueFixMod : Mod
    {
        internal const string HarmonyId = "whitegiver.dmssr.neurogluefix";

        private static bool applied;

        public DMSSRNeuroglueFixMod(ModContentPack content) : base(content)
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
                MechBiogelCache.ApplyHarmony(harmony);
                UseNeurogluePatches.Apply(harmony);
            }
            catch (Exception exception)
            {
                SafeLog.Error("unexpected failure while installing patches.", exception);
                return;
            }

            SafeLog.Message(
                "installed: canRefill=" + UseNeurogluePatches.CanRefillPatched
                + " makeNewToils=" + UseNeurogluePatches.MakeNewToilsPatched
                + " floatMenuFastLookup=" + UseNeurogluePatches.FloatMenuPatched
                + " dirtyCacheHook=" + MechBiogelCache.DirtyHookInstalled
                + " upstreamRefillOnlyFilter=" + UseNeurogluePatches.UpstreamOutcomeFilterDetected);
        }
    }

    [StaticConstructorOnStartup]
    internal static class DMSSRNeuroglueFixStartup
    {
        static DMSSRNeuroglueFixStartup()
        {
            DMSSRNeuroglueFixMod.ApplyPatches();
            NeuroglueDefs.EnsureInitialized();
        }
    }
}
