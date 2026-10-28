using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using WhiteGiver.MechanitorBandwidthBar;

namespace BandwidthBarHarness;

internal static class AssemblyResolver
{
    internal static readonly string GameRoot = Environment.GetEnvironmentVariable("RIMWORLD_ROOT")
        ?? @"A:\SteamLibrary\steamapps\common\RimWorld";

    internal static readonly string HarmonyDirectory =
        Environment.GetEnvironmentVariable("RIMWORLD_HARMONY")
        ?? @"A:\SteamLibrary\steamapps\workshop\content\294100\2009463077\1.7\Assemblies";

    [ModuleInitializer]
    internal static void Initialize()
    {
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        foreach (string directory in new[] { GameRoot, HarmonyDirectory })
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }
}

internal static class Program
{
    private static int Main()
    {
        try
        {
            if (!MechanitorBandwidthBarMod.TryInstall(out string error))
            {
                Console.WriteLine("FAIL install: " + error);
                return 1;
            }

            MethodInfo? target = AccessTools.Method(
                typeof(MechanitorBandwidthGizmo),
                nameof(MechanitorBandwidthGizmo.GizmoOnGUI));
            if (target == null)
            {
                Console.WriteLine("FAIL: GizmoOnGUI not found after install.");
                return 1;
            }

            Patches? patches = Harmony.GetPatchInfo(target);
            bool registered = patches != null
                && patches.Prefixes.Any(prefix => prefix.owner == "whitegiver.mechanitor.bandwidthbar");
            Console.WriteLine(registered ? "ok   harmony prefix registered" : "FAIL harmony prefix missing");
            if (!registered)
            {
                return 1;
            }

            MechanitorBandwidthGizmo gizmo = new(null!);
            GizmoResult result = default;
            bool runOriginal = MechanitorBandwidthBarMod.DrawBandwidthGizmo(
                gizmo,
                default(Vector2),
                140f,
                ref result);
            Console.WriteLine(
                runOriginal
                    ? "ok   null tracker falls back to vanilla"
                    : "FAIL null tracker should fall back to vanilla");
            if (!runOriginal)
            {
                return 1;
            }

            Console.WriteLine("Harness checks passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine("FAIL harness: " + exception);
            return 1;
        }
    }
}
