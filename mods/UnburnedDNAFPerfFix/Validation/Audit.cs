using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Validation;

internal static class Audit
{
    internal static readonly Harmony Hooks = new("unburned.dnaf.validation");
    internal static int Run(string[] args)
    {
        Hooks.Patch(AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Audit), nameof(LogMessage)));
        Hooks.Patch(AccessTools.Method(typeof(Log), nameof(Log.Warning), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Audit), nameof(LogMessage)));
        Hooks.Patch(AccessTools.Method(typeof(Log), nameof(Log.Message), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Audit), nameof(LogMessage)));
        AccessTools.Field(typeof(DefOfHelper), "bindingNow").SetValue(null, true);
        new Harmony("Andromeda.Unburned").CreateClassProcessor(Program.Unburned.GetType("Unburned.Pretty_Patch", true)!).Patch();
        Hooks.Patch(AccessTools.Method(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelChanged)), prefix: new HarmonyMethod(typeof(Audit), nameof(Skip)));
        Hooks.Patch(AccessTools.Method(typeof(Thing), "get_Graphic"), prefix: new HarmonyMethod(typeof(Audit), nameof(Skip)));
        Hooks.Patch(AccessTools.Method(typeof(RestUtility), nameof(RestUtility.Awake)), prefix: new HarmonyMethod(typeof(Audit), nameof(Awake)));
        if (Program.Dnaf != null && !args.Contains("--sidecar-first"))
            RuntimeHelpers.RunClassConstructor(Program.Dnaf.GetType("DoNotAvoidFire.HarmonyInit", true)!.TypeHandle);
        foreach ((Type type, string name) in new[] {
                     (typeof(Fire), "SpawnSetup"), (typeof(Fire), "DeSpawn"),
                     (typeof(PersistentDangerSource), "ComputeAll"), (typeof(PersistentDangerSource), "UpdateIncrementally"),
                     (typeof(PathGrid), "CalculatedCostAt"), (typeof(PathFinder), "ParameterizePathJob") })
        {
            MethodInfo m = AccessTools.Method(type, name);
            var lines = new List<string> { m.ToString()!, "PARAMETERS: " + string.Join(", ", m.GetParameters().Select(p => $"{p.Position + 1}: {p.ParameterType} {p.Name}")), "LOCALS: " + string.Join(", ", m.GetMethodBody()!.LocalVariables) };
            lines.Add("ORIGINAL:");
            lines.AddRange(PatchProcessor.GetOriginalInstructions(m).Select((c, i) => $"{i:D4}: {c}"));
            lines.Add("PATCHED:");
            lines.AddRange(PatchProcessor.GetCurrentInstructions(m).Select((c, i) => $"{i:D4}: {c}"));
            File.WriteAllLines(Path.Combine(Program.Logs, (args.Contains("red") ? "red-" : "audit-") + type.Name + "." + name + ".il.txt"), lines);
            Console.WriteLine($"AUDIT {type.Name}.{name}: {Harmony.GetPatchInfo(m)?.Owners.Join()}");
            Console.WriteLine(lines[1]); Console.WriteLine(lines[2]);
        }
        if (args.Contains("red")) return Tests.Run(false);
        if (args.Contains("green"))
        {
            Assembly sidecar = Assembly.LoadFrom(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../1.7/Assemblies/UnburnedDNAFPerfFix.dll")));
            RuntimeHelpers.RunClassConstructor(sidecar.GetType("UnburnedDNAFPerfFix.Bootstrap", true)!.TypeHandle);
            if (Program.Dnaf != null) RuntimeHelpers.RunClassConstructor(Program.Dnaf.GetType("DoNotAvoidFire.HarmonyInit", true)!.TypeHandle);
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains("whitegiver.unburned.dnaf.perffix")))
                File.WriteAllLines(Path.Combine(Program.Logs, "green-" + original.DeclaringType!.Name + "." + original.Name + ".il.txt"),
                    PatchProcessor.GetCurrentInstructions(original).Select((c, i) => $"{i:D4}: {c}"));
            int result = Tests.Run(true);
            Tests.Check(BootstrapDnaf(sidecar) == (Program.Dnaf != null), "optional DNAF runtime presence matches installation");
            return result;
        }
        return 0;
    }
    private static bool LogMessage(string text) { Console.WriteLine("GAME: " + text); return false; }
    private static bool Skip() => false;
    private static bool Awake(ref bool __result) { __result = true; return false; }
    private static bool BootstrapDnaf(Assembly a) => (bool)a.GetType("UnburnedDNAFPerfFix.Bootstrap")!.GetProperty("DnafActive")!.GetValue(null)!;
}
