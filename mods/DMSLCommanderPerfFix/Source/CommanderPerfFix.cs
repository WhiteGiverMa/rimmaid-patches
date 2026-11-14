using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMS_Legion;
using DMS_Legion.AXF12;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace WhiteGiver.DMSL.CommanderPerfFix;

/// <summary>Installs after Legion's StaticConstructorOnStartup PatchAll and replaces only exact known prefixes.</summary>
public static class CommanderPerfFix
{
    public const string HarmonyId = "whitegiver.dmsl.commanderperffix";
    internal const string LogPrefix = "[DMS Legion Commander Perf Fix]";

    private static readonly object ApplyLock = new();
    private static readonly List<string> installed = new();
    private static int appliedPatchCount = -1;

    public static IReadOnlyList<string> InstalledPatches => installed;

    public static int Apply(bool ensureUpstreamPatched = true)
    {
        lock (ApplyLock)
        {
            if (appliedPatchCount >= 0)
            {
                return appliedPatchCount;
            }

            try
            {
                if (ensureUpstreamPatched)
                {
                    Type upstreamBootstrap = AccessTools.TypeByName("DMS_Legion.HarmonyPatches")
                        ?? throw new InvalidOperationException("DMS_Legion.HarmonyPatches was not found.");
                    RuntimeHelpers.RunClassConstructor(upstreamBootstrap.TypeHandle);
                }

                appliedPatchCount = ApplyCore();
            }
            catch (Exception exception)
            {
                installed.Clear();
                appliedPatchCount = 0;
                Log.Error(LogPrefix + " inactive; upstream bootstrap or patch installation failed: " + exception);
            }

            return appliedPatchCount;
        }
    }

    private static int ApplyCore()
    {
        installed.Clear();
        Harmony harmony = new(HarmonyId);
        harmony.UnpatchAll(HarmonyId);

        Type? utility = AccessTools.TypeByName("DMS_Legion.CommanderAutonomyUtility");
        Type? lifecycle = AccessTools.TypeByName("DMS_Legion.CommanderAutonomyLifecycle");

        TryPatch(harmony, "PawnHasMarker def/comp cache",
            AccessTools.DeclaredMethod(typeof(CompMechCommanderMarker), "PawnHasMarker", new[] { typeof(Pawn) }),
            Prefix(nameof(CommanderPatches.PawnHasMarkerPrefix)));
        TryPatch(harmony, "IsAutonomous fast guard",
            Method(utility, "IsAutonomous", new[] { typeof(Pawn) }),
            Prefix(nameof(CommanderPatches.IsAutonomousPrefix)));
        TryPatch(harmony, "FindOverseer allocation-free loops",
            Method(utility, "FindOverseer", new[] { typeof(Pawn) }),
            Prefix(nameof(CommanderPatches.FindOverseerPrefix)));
        TryPatch(harmony, "HasAssignment allocation-free loops",
            Method(lifecycle, "HasAssignment", new[] { typeof(Pawn), typeof(Pawn) }),
            Prefix(nameof(CommanderPatches.HasAssignmentPrefix)));
        TryPatch(harmony, "personal work-mode comp cache",
            Method(utility, "GetWorkMode", new[] { typeof(Pawn) }),
            Prefix(nameof(CommanderPatches.GetWorkModePrefix)));
        TryPatch(harmony, "personal recharge-threshold comp cache",
            Method(utility, "GetRechargeThresholds", new[] { typeof(Pawn) }),
            Prefix(nameof(CommanderPatches.GetRechargeThresholdsPrefix)));

        ReplaceUpstreamPrefix(harmony, "lean CompOverseerSubject.State",
            AccessTools.DeclaredMethod(typeof(CompOverseerSubject), "get_State", Type.EmptyTypes),
            AccessTools.TypeByName("DMS_Legion.CommanderAutonomy_State"),
            Prefix(nameof(CommanderPatches.SubjectStatePrefix)));
        ReplaceUpstreamPrefix(harmony, "lean CompOverseerSubject.Overseer",
            AccessTools.DeclaredMethod(typeof(CompOverseerSubject), "get_Overseer", Type.EmptyTypes),
            AccessTools.TypeByName("DMS_Legion.CommanderAutonomy_SubjectOverseer"),
            Prefix(nameof(CommanderPatches.SubjectOverseerPrefix)));
        ReplaceUpstreamPrefix(harmony, "lean MechanitorUtility.GetOverseer",
            AccessTools.DeclaredMethod(typeof(MechanitorUtility), "GetOverseer", new[] { typeof(Pawn) }),
            AccessTools.TypeByName("DMS_Legion.CommanderAutonomy_Overseer"),
            Prefix(nameof(CommanderPatches.MechanitorGetOverseerPrefix)));

        TryPatch(harmony, "AXF12 manager per-Game cache",
            AccessTools.PropertyGetter(typeof(AXF12ReconMissionManager), "Instance"),
            new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ReconManagerCache), "GetInstancePrefix")));

        ReplaceUpstreamPrefix(harmony, "O(1) observed-map removal guard",
            AccessTools.DeclaredMethod(typeof(MapParent), "CheckRemoveMapNow", Type.EmptyTypes),
            AccessTools.TypeByName("DMS_Legion.AXF12.AXF12ObservedMap_CheckRemoveMapNow_Patch"),
            new HarmonyMethod(AccessTools.DeclaredMethod(typeof(ReconManagerCache), "CheckRemoveMapNowPrefix")));

        Log.Message(LogPrefix + " active (" + installed.Count + " patches): " + string.Join(", ", installed));
        return installed.Count;
    }

    private static HarmonyMethod Prefix(string name)
    {
        return new HarmonyMethod(AccessTools.DeclaredMethod(typeof(CommanderPatches), name));
    }

    private static MethodInfo? Method(Type? type, string name, Type[] arguments)
    {
        return type == null ? null : AccessTools.DeclaredMethod(type, name, arguments);
    }

    private static void TryPatch(Harmony harmony, string label, MethodInfo? original, HarmonyMethod? prefix)
    {
        if (original == null || prefix == null)
        {
            Log.Warning(LogPrefix + " skipped '" + label + "': target or patch method not found; upstream may have changed.");
            return;
        }

        try
        {
            harmony.Patch(original, prefix: prefix);
            installed.Add(label);
        }
        catch (Exception exception)
        {
            Log.Error(LogPrefix + " skipped '" + label + "': " + exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static void ReplaceUpstreamPrefix(
        Harmony harmony,
        string label,
        MethodInfo? original,
        Type? upstreamType,
        HarmonyMethod? replacement)
    {
        MethodInfo? upstream = upstreamType == null ? null : AccessTools.DeclaredMethod(upstreamType, "Prefix");
        if (original == null || upstream == null || replacement?.method == null)
        {
            Log.Warning(LogPrefix + " skipped '" + label + "': original/upstream/replacement method not found.");
            return;
        }

        // If an upstream update removed its patch, do not introduce behaviour that no longer exists.
        if (!HasPatch(original, upstream))
        {
            Log.Warning(LogPrefix + " skipped '" + label + "': Legion's expected prefix is not attached.");
            return;
        }

        bool added = false;
        try
        {
            harmony.Patch(original, prefix: replacement);
            added = true;
            if (!HasPatch(original, replacement.method))
            {
                throw new InvalidOperationException("replacement prefix was not attached");
            }

            harmony.Unpatch(original, upstream);
            if (HasPatch(original, upstream))
            {
                throw new InvalidOperationException("upstream prefix is still attached");
            }

            installed.Add(label);
        }
        catch (Exception exception)
        {
            // Failure before/during removal leaves Legion's original prefix active. Remove only our side.
            if (added)
            {
                harmony.Unpatch(original, replacement.method);
            }

            Log.Error(LogPrefix + " rolled back '" + label + "': " + exception.GetType().Name + ": " + exception.Message);
        }
    }

    private static bool HasPatch(MethodBase original, MethodInfo method)
    {
        Patches? info = Harmony.GetPatchInfo(original);
        if (info == null)
        {
            return false;
        }

        foreach (Patch patch in info.Prefixes)
        {
            if (patch.PatchMethod == method)
            {
                return true;
            }
        }

        return false;
    }
}

[StaticConstructorOnStartup]
public static class CommanderPerfFixBootstrap
{
    static CommanderPerfFixBootstrap()
    {
        CommanderPerfFix.Apply();
    }
}
