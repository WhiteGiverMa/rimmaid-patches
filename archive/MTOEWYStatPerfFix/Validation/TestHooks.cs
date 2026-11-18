using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MiliraTheOneEntwinedWithYou.GameComponents;
using RimWorld;
using Verse;

namespace MTOEWYStatPerfFix.Validation;

/// <summary>
/// Engine boundary hooks needed to run RimWorld code outside the game loop:
/// deterministic log capture, DefOf binding skip, mod-list skip, and a counter for
/// <c>Game.GetComponent&lt;GameComponent_IdealTracker&gt;()</c> calls.
/// </summary>
internal static class TestHooks
{
    internal static readonly List<string> Errors = new();
    internal static readonly List<string> Warnings = new();
    internal static int GetComponentCalls;
    internal static bool CounterAvailable;

    internal static readonly Harmony Harmony = new("mtoewy.statperffix.validation");

    internal static void Install(string saveDataPath)
    {
        PatchLog(nameof(Log.Error), nameof(CaptureError));
        PatchLog(nameof(Log.Warning), nameof(CaptureWarning));

        MethodInfo? ensureDefOf = AccessTools.Method(typeof(DefOfHelper), "EnsureInitializedInCtor");
        if (ensureDefOf != null)
        {
            Harmony.Patch(ensureDefOf, prefix: new HarmonyMethod(AccessTools.Method(typeof(TestHooks), nameof(Skip))));
        }

        MethodInfo? rebuild = AccessTools.Method(typeof(ModLister), "RebuildModList");
        if (rebuild != null)
        {
            Harmony.Patch(rebuild, prefix: new HarmonyMethod(AccessTools.Method(typeof(TestHooks), nameof(Skip))));
        }

        // Prefs has no static constructor; RimWorld's own DirectXmlLoader reads Prefs.LogVerbose.
        FieldInfo? prefsData = typeof(Prefs).GetField("data", BindingFlags.Static | BindingFlags.NonPublic);
        prefsData?.SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(PrefsData)));

        // Keep ModsConfig's static constructor away from the real user profile.
        FieldInfo? saveDataPathField = typeof(GenFilePaths).GetField("saveDataPath", BindingFlags.Static | BindingFlags.NonPublic);
        saveDataPathField?.SetValue(null, saveDataPath);

        InstallGetComponentCounter();
    }

    private static void PatchLog(string name, string prefixName)
    {
        foreach (MethodInfo method in typeof(Log).GetMethods(BindingFlags.Static | BindingFlags.Public))
        {
            if (method.Name != name)
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
            {
                continue;
            }

            Harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(TestHooks), prefixName)));
        }
    }

    private static void InstallGetComponentCounter()
    {
        try
        {
            MethodInfo? definition = typeof(Game).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            if (definition == null)
            {
                return;
            }

            MethodInfo closed = definition.MakeGenericMethod(typeof(GameComponent_IdealTracker));
            Harmony.Patch(closed, prefix: new HarmonyMethod(AccessTools.Method(typeof(TestHooks), nameof(CountGetComponent))));
            CounterAvailable = true;
        }
        catch (Exception exception)
        {
            Warnings.Add("GetComponent counter unavailable: " + exception.Message);
        }
    }

    internal static bool Skip() => false;

    internal static bool CaptureError(string text)
    {
        Errors.Add(text);
        return false;
    }

    internal static bool CaptureWarning(string text)
    {
        Warnings.Add(text);
        return false;
    }

    internal static bool CountGetComponent()
    {
        GetComponentCalls++;
        return true;
    }
}
