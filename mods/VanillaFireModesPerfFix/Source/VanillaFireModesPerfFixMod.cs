using System;
using HarmonyLib;
using Verse;

namespace WhiteGiver.VanillaFireModesPerfFix;

/// <summary>
/// Entry point. Installs the replacement patches twice on purpose:
/// <list type="number">
///   <item>from the <see cref="Mod"/> constructor, which is when Vanilla Fire Modes applies its own patches, so
///   the tick hook disappears before the first game tick;</item>
///   <item>from <c>[StaticConstructorOnStartup]</c>, which RimWorld runs after every mod constructor, so a broken
///   load order (or any other mod that patches later) still ends with exactly one burst-length authority.</item>
/// </list>
/// Both calls are idempotent and both roll back to Vanilla Fire Modes' own patches on failure.
/// </summary>
public sealed class VanillaFireModesPerfFixMod : Mod
{
    public const string HarmonyId = "whitegiver.vfm.perffix";

    internal const string LogPrefix = "[VFM Perf Fix]";

    public VanillaFireModesPerfFixMod(ModContentPack content)
        : base(content)
    {
        TryApply("Mod constructor");
    }

    internal static int TryApply(string phase)
    {
        try
        {
            return PatchInstaller.Apply(phase);
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} {phase} install failed; Vanilla Fire Modes keeps running unmodified.\n{exception}");
            return 0;
        }
    }

    /// <summary>Labels of what the installer changed (used by the offline harness and the log).</summary>
    public static System.Collections.Generic.IReadOnlyList<string> InstalledPatches => PatchInstaller.Steps;

    /// <summary>Public hook used by the offline validation harness.</summary>
    public static int ApplyPatches() => PatchInstaller.Apply("validation");

    /// <summary>Public hook used by the offline validation harness to return to Vanilla Fire Modes behaviour.</summary>
    public static void RemovePatches() => PatchInstaller.Remove();
}

/// <summary>Second, load-order independent install phase (runs after every <see cref="Mod"/> constructor).</summary>
[StaticConstructorOnStartup]
public static class VanillaFireModesPerfFixStartup
{
    static VanillaFireModesPerfFixStartup()
    {
        VanillaFireModesPerfFixMod.TryApply("StaticConstructorOnStartup");
    }
}
