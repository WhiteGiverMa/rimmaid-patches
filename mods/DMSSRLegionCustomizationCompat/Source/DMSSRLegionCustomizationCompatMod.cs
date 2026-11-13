using System;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRLegionCustomizationCompat
{
    /// <summary>
    /// Standalone compatibility layer between:
    ///  - The Dead Man's Switch - Synthriada (Cephalopod.DeadManSwitch.Synthriada, Workshop 3835512735)
    ///  - The Dead Man's Switch - Legion    (Srxf.DeadManSwitch.Legion,        Workshop 3666117000)
    ///
    /// It only widens DMSSR's name predicate for DMS Legion's DMSL_Mech_* family and mirrors the
    /// upstream "grant on MicroelectronicsBasics" scan. Nothing is scribed, no Defs are added,
    /// and no XML or DLL of either mod is modified.
    /// </summary>
    public sealed class DMSSRLegionCustomizationCompatMod : Mod
    {
        internal const string HarmonyId = "whitegiver.dmssr.legioncustomizationcompat";

        private static bool applied;

        public DMSSRLegionCustomizationCompatMod(ModContentPack content) : base(content)
        {
            ApplyPatches();
        }

        internal static bool Applied => applied;

        internal static bool IsSupportedPawnPatchInstalled => MechCustomizationPatches.IsSupportedPawnInstalled;

        internal static bool UnlockOnResearchPatchInstalled => MechCustomizationPatches.UnlockOnResearchInstalled;

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
                MechCustomizationPatches.Apply(harmony);
            }
            catch (Exception exception)
            {
                SafeLog.Error("Unexpected failure while installing patches.", exception);
                return;
            }

            SafeLog.Message(
                "installed: isSupportedPawn=" + MechCustomizationPatches.IsSupportedPawnInstalled
                + " unlockOnResearch=" + MechCustomizationPatches.UnlockOnResearchInstalled);
        }
    }
}
