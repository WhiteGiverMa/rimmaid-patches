using System;
using HarmonyLib;
using Verse;

namespace DMSSRLegionCustomizationCompat.Validation;

/// <summary>
/// Harness-only shims for code that exists solely for the Unity scene. They are owned by the
/// validation assembly, never by the compatibility mod.
///
/// - HediffSet.DirtyCache() walks pawn renderers, portraits and texture atlases.
/// - Pawn_HealthTracker.CheckForStateChange() runs death/downed capacity math that is unrelated
///   to the customization window and needs a fully spawned, needs-bearing pawn.
///
/// Skipping their bodies keeps the offline result focused on the DMSSR code paths under test.
/// </summary>
internal static class Probe
{
    internal static int DirtyCacheCalls;
    internal static int CheckForStateChangeCalls;

    private static Harmony? harmony;

    internal static void Install()
    {
        if (harmony != null)
        {
            return;
        }

        harmony = new Harmony("whitegiver.dmssr.legioncustomizationcompat.validation.probe");

        harmony.Patch(
            AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache), Type.EmptyTypes)!,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(SkipDirtyCache))!));

        harmony.Patch(
            AccessTools.Method(typeof(Pawn_HealthTracker), "CheckForStateChange")!,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(SkipStateChange))!));
    }

    internal static void Remove()
    {
        harmony?.UnpatchAll("whitegiver.dmssr.legioncustomizationcompat.validation.probe");
        harmony = null;
    }

    internal static void Reset()
    {
        DirtyCacheCalls = 0;
        CheckForStateChangeCalls = 0;
    }

    private static bool SkipDirtyCache()
    {
        DirtyCacheCalls++;
        return false;
    }

    private static bool SkipStateChange()
    {
        CheckForStateChangeCalls++;
        return false;
    }
}
