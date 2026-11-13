using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSEWorldPerfFix;

/// <summary>
/// Resolves DMSE types by name and installs the four independent fixes. Each fix is optional: if the
/// upstream member is missing (DMSE update, other fork) that fix is skipped and the others still run.
/// </summary>
internal static class PatchInstaller
{
    private const string AlertTypeName = "DMSE.WorldComponent_FactionAirDefenseAlert";
    private const string NetworkTypeName = "DMSE.WorldComponent_BVRNetwork";
    private const string DoctrineTypeName = "DMSE.MissileDoctrine";

    private static bool applied;
    private static readonly List<string> installed = new();

    internal static IReadOnlyList<string> Installed => installed;

    internal static void Reset()
    {
        applied = false;
        installed.Clear();
    }

    internal static int Apply()
    {
        if (applied)
        {
            return installed.Count;
        }

        applied = true;
        installed.Clear();

        Harmony harmony = new(WorldPerfFixMod.HarmonyId);
        harmony.UnpatchAll(WorldPerfFixMod.HarmonyId);

        Type? alertType = AccessTools.TypeByName(AlertTypeName);
        Type? networkType = AccessTools.TypeByName(NetworkTypeName);
        Type? doctrineType = AccessTools.TypeByName(DoctrineTypeName);

        if (alertType == null)
        {
            Log.Error($"{WorldPerfFixMod.LogPrefix} could not resolve {AlertTypeName}. Is The Dead Man's Switch - Expedition loaded?");
            return 0;
        }

        TryInstall(installed, "StageOf/RadarWarm read-only queries", () => AirDefenseQueryPatches.Install(harmony, alertType));
        TryInstall(installed, "Air-defense tick pruning and hibernation", () => AirDefenseTickPatches.Install(harmony, alertType));

        if (networkType != null)
        {
            TryInstall(installed, "Active BVR scan-node rebuild", () => BvrScanPatches.Install(harmony, networkType));
        }
        else
        {
            Log.Warning($"{WorldPerfFixMod.LogPrefix} could not resolve {NetworkTypeName}; scan-node fast path skipped.");
        }

        if (doctrineType != null)
        {
            TryInstall(installed, "MissileDoctrine faction cache", () => DoctrineCachePatches.Install(harmony, doctrineType));
        }
        else
        {
            Log.Warning($"{WorldPerfFixMod.LogPrefix} could not resolve {DoctrineTypeName}; doctrine cache skipped.");
        }

        return installed.Count;
    }

    private static void TryInstall(List<string> target, string label, Action install)
    {
        try
        {
            install();
            target.Add(label);
        }
        catch (Exception exception)
        {
            Log.Error($"{WorldPerfFixMod.LogPrefix} skipped '{label}': {exception.GetType().Name}: {exception.Message}");
        }
    }
}
