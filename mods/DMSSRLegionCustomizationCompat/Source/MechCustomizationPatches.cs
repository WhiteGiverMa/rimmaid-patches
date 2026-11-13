using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRLegionCustomizationCompat
{
    /// <summary>
    /// Two narrowly scoped postfixes:
    ///
    /// 1. <c>DMSSR.UI.MechCustomizationUtility.IsSupportedPawn(Pawn)</c> is the single predicate
    ///    behind both <c>CanOpenFor</c> and the Fortified window factory. Upstream only accepts
    ///    DMS_Mech_* / Cinder_Mech_*; adding DMSL_Mech_* here makes the factory return
    ///    <c>Window_MechCustomization</c> for Legion mechs, exactly as it already does for the
    ///    other two families.
    ///
    /// 2. <c>DMSSR.Patches.Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs()</c>
    ///    is the method that grants the <c>DMS_MechCustomization</c> hediff to already-spawned
    ///    mechs when MicroelectronicsBasics finishes. Upstream only scans DMS_Mech_*; the postfix
    ///    performs the same scan for DMSL_Mech_*.
    ///
    /// No other DMSSR method is touched. In particular the stat/capacity postfixes and the
    /// path-cost utility are untouched, so this composes with performance patches that target
    /// those methods.
    /// </summary>
    internal static class MechCustomizationPatches
    {
        internal const string UtilityTypeName = "DMSSR.UI.MechCustomizationUtility";
        internal const string UnlockResearchTypeName = "DMSSR.Patches.Patch_MechCustomization_UnlockOnResearch";

        internal static bool IsSupportedPawnInstalled { get; private set; }

        internal static bool UnlockOnResearchInstalled { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            ApplyIsSupportedPawnPatch(harmony);
            ApplyUnlockOnResearchPatch(harmony);
        }

        private static void ApplyIsSupportedPawnPatch(Harmony harmony)
        {
            try
            {
                Type? utilityType = AccessTools.TypeByName(UtilityTypeName);
                MethodInfo? target = utilityType == null
                    ? null
                    : AccessTools.Method(utilityType, "IsSupportedPawn", new[] { typeof(Pawn) });

                if (target == null)
                {
                    SafeLog.Error("Could not resolve " + UtilityTypeName + ".IsSupportedPawn(Pawn); "
                        + "DMSL_Mech_* mechs will not receive the customization window.");
                    return;
                }

                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(
                        typeof(MechCustomizationPatches),
                        nameof(IsSupportedPawnPostfix)));

                IsSupportedPawnInstalled = true;
            }
            catch (Exception exception)
            {
                SafeLog.Error("Failed to install the IsSupportedPawn postfix.", exception);
            }
        }

        private static void ApplyUnlockOnResearchPatch(Harmony harmony)
        {
            try
            {
                Type? unlockType = AccessTools.TypeByName(UnlockResearchTypeName);
                MethodInfo? target = unlockType == null
                    ? null
                    : AccessTools.Method(unlockType, "ApplyToSpawnedMechs", Type.EmptyTypes);

                if (target == null)
                {
                    SafeLog.Error("Could not resolve " + UnlockResearchTypeName + ".ApplyToSpawnedMechs(); "
                        + "already-spawned DMSL_Mech_* mechs will only receive the customization hediff when the window is opened.");
                    return;
                }

                harmony.Patch(
                    target,
                    postfix: new HarmonyMethod(
                        typeof(MechCustomizationPatches),
                        nameof(ApplyToSpawnedMechsPostfix)));

                UnlockOnResearchInstalled = true;
            }
            catch (Exception exception)
            {
                SafeLog.Error("Failed to install the ApplyToSpawnedMechs postfix.", exception);
            }
        }

        private static void IsSupportedPawnPostfix(Pawn pawn, ref bool __result)
        {
            if (!__result && LegionMechUtility.IsLegionMech(pawn))
            {
                __result = true;
            }
        }

        private static void ApplyToSpawnedMechsPostfix()
        {
            LegionMechUtility.GrantCustomizationToSpawnedLegionMechs();
        }
    }
}
