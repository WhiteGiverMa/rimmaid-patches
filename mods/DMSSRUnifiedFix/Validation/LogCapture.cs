using HarmonyLib;
using RimWorld;
using System.Reflection;
using Verse;

namespace DMSSRUnifiedFix.Validation;

internal static class LogCapture
{
    internal static readonly List<string> Errors = new();
    internal static readonly List<string> Warnings = new();
    internal static Action<string>? ErrorObserved;
    internal static readonly List<string> ExpectedWarningFragments = new();
    internal static void Install()
    {
        Harmony harmony = new(Program.ValidationOwner + ".logs");
        foreach (string name in new[] { "Error", "Warning", "Message" })
            harmony.Patch(AccessTools.Method(typeof(Log), name, new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(LogCapture), name));
        AccessTools.Field(typeof(UnityDataInitializer), "initializing").SetValue(null, true);
        AccessTools.Field(typeof(UnityData), "mainThreadId").SetValue(null, Environment.CurrentManagedThreadId);
        AccessTools.Field(typeof(DefOfHelper), "bindingNow").SetValue(null, true);
        foreach (Type type in new[] { typeof(BillRepeatModeDefOf), typeof(BillStoreModeDefOf), typeof(ThingDefOf),
            typeof(StatDefOf), typeof(PawnCapacityDefOf), typeof(FleshTypeDefOf), typeof(BodyPartTagDefOf),
            typeof(DMSSR.Hediffs.DMS_HediffDefOf), typeof(DMSSR.Jobs.DMS_JobDefOf) })
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
        AccessTools.Field(typeof(Prefs), "data").SetValue(null, new PrefsData());
        foreach (Type type in new[] { typeof(MaterialPool), typeof(SolidColorMaterials) })
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type).Where(m => m.Name is "MatFrom" or "NewSolidColorTexture" or "SimpleSolidColorMaterial"))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(LogCapture), nameof(Skip)));
        harmony.Patch(AccessTools.Method(typeof(ContentFinder<UnityEngine.Texture2D>), "Get"), prefix: new HarmonyMethod(typeof(LogCapture), nameof(Skip)));
        MethodInfo graphic = AccessTools.GetDeclaredMethods(typeof(GraphicDatabase)).Single(m =>
            m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters() is [{ ParameterType: var parameter }] && parameter == typeof(string));
        harmony.Patch(graphic.MakeGenericMethod(typeof(Graphic_Single)), prefix: new HarmonyMethod(typeof(LogCapture), nameof(Skip)));
    }
    private static bool Error(string text) { Errors.Add(text); Console.WriteLine("LOG.ERROR " + text); ErrorObserved?.Invoke(text); return false; }
    private static bool Warning(string text) { Warnings.Add(text); Console.WriteLine("LOG.WARNING " + text); return false; }
    private static bool Message(string text) { Console.WriteLine("LOG.MESSAGE " + text); return false; }
    private static bool Skip() => false;
    internal static void AssertClean()
    {
        bool expected = Warnings.Count == ExpectedWarningFragments.Count
            && ExpectedWarningFragments.All(fragment => Warnings.Count(warning => warning.Contains(fragment,StringComparison.Ordinal)) == 1);
        if (Errors.Count != 0 || !expected)
            throw new InvalidOperationException($"Unexpected logs: errors={Errors.Count} warnings={Warnings.Count} expectedWarnings={ExpectedWarningFragments.Count}");
        Console.WriteLine($"PASS logs: 0 unexpected errors / 0 unexpected warnings (expected warnings={ExpectedWarningFragments.Count}).");
    }
}
