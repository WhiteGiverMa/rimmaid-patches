using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSEMissileBaseFix;

/// <summary>
/// Startup entry point. Installs the three independent Harmony fixes and ensures the three DMSE
/// enemy missile-base WorldObjectDefs allow shuttle / transport-pod targeting.
/// </summary>
[StaticConstructorOnStartup]
public static class DMSEMissileBaseFixMod
{
    public const string HarmonyId = "whitegiver.dmse.missilebasefix";

    internal const string LogPrefix = "[DMSE MissileBase Fix]";

    static DMSEMissileBaseFixMod()
    {
        try
        {
            int updatedDefs = EnsureLaunchTargetsEnabled();
            int installed = PatchInstaller.Apply();
            Log.Message($"{LogPrefix} installed {installed} Harmony patches (launchTargetDefsEnabled={updatedDefs}).");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} failed during startup; keeping upstream DMSE behaviour.\n{exception}");
        }
    }

    public static IReadOnlyList<string> InstalledPatches => PatchInstaller.Installed;

    public static int EnsureLaunchTargetsEnabled()
    {
        return MissileBaseSiteUtility.EnableMissileBaseLaunchTargets();
    }

    public static int ApplyPatches()
    {
        return PatchInstaller.Apply();
    }

    public static void RemovePatches()
    {
        new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        PatchInstaller.Reset();
    }
}
