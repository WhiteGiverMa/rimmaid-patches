using HarmonyLib;
using Verse;

namespace WhiteGiver.Wolfein.RestoreWorkPrioritiesFix;

public static class RestoreWorkPrioritiesFix
{
    public const string HarmonyId = "whitegiver.wolfein.restoreworkprioritiesfix";
    private const string LogPrefix = "[Wolfein RestoreWorkPriorities Fix]";

    public static void Apply()
    {
        Harmony harmony = new(HarmonyId);
        HarmonyMethod prefix = new(typeof(RestoreWorkPrioritiesFix), nameof(Prefix));

        PatchOrReport(harmony, "Wolfein.HediffComp_RestoreWorkPriorities:CompPostPostAdd", prefix);
        PatchOrReport(harmony, "Wolfein.HediffComp_RestoreWorkPriorities:CompPostPostRemoved", prefix);

        Log.Message(LogPrefix + " active: skips priority bookkeeping while work settings are uninitialized.");
    }

    private static void PatchOrReport(Harmony harmony, string signature, HarmonyMethod prefix)
    {
        var target = AccessTools.Method(signature);
        if (target == null)
        {
            Log.Error(LogPrefix + " " + signature + " not found; patch skipped. Wolfein may have fixed or renamed it.");
            return;
        }

        harmony.Patch(target, prefix: prefix);
    }

    // Wolfein reads/saves work priorities in CompPostPostAdd/Removed. During pawn generation
    // (PawnTechHediffsGenerator) non-player pawns still have an uninitialized workSettings;
    // GetPriority logs an error and force-initializes it mid-generation. Skip the bookkeeping
    // instead, leaving the vanilla initialization to run at the normal point.
    public static bool Prefix(HediffComp __instance)
    {
        Pawn? pawn = __instance.parent?.pawn;
        if (pawn == null || !pawn.RaceProps.Humanlike || pawn.workSettings == null)
        {
            return true;
        }

        return pawn.workSettings.Initialized;
    }
}

public sealed class WolfeinRestoreWorkPrioritiesFixMod : Mod
{
    public WolfeinRestoreWorkPrioritiesFixMod(ModContentPack content) : base(content)
    {
        RestoreWorkPrioritiesFix.Apply();
    }
}
