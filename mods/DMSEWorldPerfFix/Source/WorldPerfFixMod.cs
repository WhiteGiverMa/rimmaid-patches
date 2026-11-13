using System;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSEWorldPerfFix;

/// <summary>
/// Entry point. The install itself is deliberately failure tolerant: every upstream type and member is
/// resolved by name, and a missing or renamed DMSE only disables this patch instead of breaking startup.
/// </summary>
[StaticConstructorOnStartup]
public static class WorldPerfFixMod
{
    public const string HarmonyId = "whitegiver.dmse.worldperffix";

    internal const string LogPrefix = "[DMSE World Perf Fix]";

    static WorldPerfFixMod()
    {
        try
        {
            int installed = PatchInstaller.Apply();
            if (installed > 0)
            {
                Log.Message($"{LogPrefix} installed {installed} Harmony patches.");
            }
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} failed to install its Harmony patches; the game keeps running with unpatched DMSE.\n{exception}");
        }
    }

    /// <summary>Labels of the patch groups that installed successfully (used by the validation harness).</summary>
    public static System.Collections.Generic.IReadOnlyList<string> InstalledPatches => PatchInstaller.Installed;

    /// <summary>Public hook used by the offline validation harness.</summary>
    public static int ApplyPatches()
    {
        return PatchInstaller.Apply();
    }

    /// <summary>Public hook used by the offline validation harness to return to vanilla DMSE behaviour.</summary>
    public static void RemovePatches()
    {
        new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        PatchInstaller.Reset();
    }
}
