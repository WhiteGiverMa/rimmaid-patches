using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.CeleTechTurretUpgradePerfFix;

/// <summary>
/// Entry point. Resolves the upstream types by name first so a missing or renamed CeleTech never makes
/// this mod throw during startup, then installs the four Harmony patches and reports a per-patch result.
/// </summary>
public static class TurretUpgradePerfFix
{
    public const string HarmonyId = "whitegiver.celetech.turretupgradeperffix";

    internal const string LogPrefix = "[CeleTech Turret Upgrade Perf Fix]";

    private static bool applied;

    public static void Apply()
    {
        if (applied)
        {
            return;
        }

        applied = true;

        Type? workGiverType = AccessTools.TypeByName("CeleTech.Base.WorkGiver_TurretUpgrade");
        Type? compType = AccessTools.TypeByName("CeleTech.Base.Comp_TurretUpgrade");

        if (workGiverType == null || compType == null)
        {
            Log.Error(LogPrefix + " could not resolve CeleTech.Base.WorkGiver_TurretUpgrade / Comp_TurretUpgrade. Is CeleTech Arsenal MKIII loaded? Patch is inactive.");
            return;
        }

        MethodInfo? pawnCanUse = AccessTools.Method(
            typeof(JobGiver_Work),
            "PawnCanUseWorkGiver",
            new[] { typeof(Pawn), typeof(WorkGiver) });

        MethodInfo? potentialThings = AccessTools.DeclaredMethod(workGiverType, "PotentialWorkThingsGlobal", new[] { typeof(Pawn) })
            ?? AccessTools.Method(typeof(WorkGiver_Scanner), nameof(WorkGiver_Scanner.PotentialWorkThingsGlobal), new[] { typeof(Pawn) });

        MethodInfo? hasJobOnThing = AccessTools.Method(
            workGiverType,
            "HasJobOnThing",
            new[] { typeof(Pawn), typeof(Thing), typeof(bool) });

        MethodInfo? postSpawnSetup = AccessTools.Method(
            compType,
            "PostSpawnSetup",
            new[] { typeof(bool) });

        FieldInfo? queuedField = AccessTools.Field(compType, "queued");

        // Registration is what makes the fast reject sound. If we cannot observe new comps, do not install
        // any fast path at all: the one-time lister scan alone would miss turrets spawned later.
        if (postSpawnSetup == null || postSpawnSetup.IsStatic || postSpawnSetup.DeclaringType != compType)
        {
            Log.Error(LogPrefix + " Comp_TurretUpgrade.PostSpawnSetup(bool) was not found on the expected type; patch not applied (tracker completeness cannot be guaranteed).");
            return;
        }

        if (queuedField == null || queuedField.IsStatic || !typeof(System.Collections.IList).IsAssignableFrom(queuedField.FieldType))
        {
            Log.Error(LogPrefix + " Comp_TurretUpgrade.queued is no longer a list field; patch not applied.");
            return;
        }

        Harmony harmony = new(HarmonyId);
        int patched = 0;

        if (Patch(
                harmony,
                pawnCanUse,
                postfix: new HarmonyMethod(typeof(TurretUpgradePatches), nameof(TurretUpgradePatches.PawnCanUseWorkGiverPostfix))))
        {
            patched++;
        }
        else
        {
            Log.Error(LogPrefix + " JobGiver_Work.PawnCanUseWorkGiver(Pawn, WorkGiver) was not found; the O(1) skip is disabled.");
        }

        if (potentialThings != null && potentialThings.ReturnType == typeof(IEnumerable<Thing>))
        {
            if (Patch(
                    harmony,
                    potentialThings,
                    prefix: new HarmonyMethod(typeof(TurretUpgradePatches), nameof(TurretUpgradePatches.PotentialWorkThingsGlobalPrefix))))
            {
                patched++;
            }
        }
        else
        {
            Log.Error(LogPrefix + " PotentialWorkThingsGlobal(Pawn) was not found with the expected return type; the exact search set is disabled.");
        }

        // Only patch the override declared by WorkGiver_TurretUpgrade. Patching the inherited base method
        // would add a per-call type check to every work giver in the game for no reason.
        if (hasJobOnThing != null && hasJobOnThing.DeclaringType == workGiverType)
        {
            if (Patch(
                    harmony,
                    hasJobOnThing,
                    prefix: new HarmonyMethod(typeof(TurretUpgradePatches), nameof(TurretUpgradePatches.HasJobOnThingPrefix))))
            {
                patched++;
            }
        }
        else
        {
            Log.Error(LogPrefix + " WorkGiver_TurretUpgrade.HasJobOnThing is no longer declared on that type; the direct-call fast reject is disabled.");
        }

        if (Patch(
                harmony,
                postSpawnSetup,
                postfix: new HarmonyMethod(typeof(TurretUpgradePatches), nameof(TurretUpgradePatches.PostSpawnSetupPostfix))))
        {
            patched++;
        }

        Log.Message(LogPrefix + " active (" + patched + "/4 patches): per-map queued-turret registry.");
    }

    private static bool Patch(Harmony harmony, MethodInfo? target, HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
    {
        if (target == null || target.IsStatic)
        {
            return false;
        }

        try
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            return true;
        }
        catch (Exception exception)
        {
            Log.Error(LogPrefix + " could not patch " + target.DeclaringType?.FullName + "." + target.Name + ": " + exception);
            return false;
        }
    }
}

public sealed class CeleTechTurretUpgradePerfFixMod : Mod
{
    public CeleTechTurretUpgradePerfFixMod(ModContentPack content) : base(content)
    {
        if (AccessTools.TypeByName("CeleTech.Base.WorkGiver_TurretUpgrade") == null
            || AccessTools.TypeByName("CeleTech.Base.Comp_TurretUpgrade") == null)
        {
            Log.Message(TurretUpgradePerfFix.LogPrefix + " CeleTech Arsenal MKIII is not loaded; skipped.");
            return;
        }

        try
        {
            TurretUpgradePerfFix.Apply();
        }
        catch (Exception exception)
        {
            Log.Error(TurretUpgradePerfFix.LogPrefix + " startup failed; patch is inactive: " + exception);
        }
    }
}
