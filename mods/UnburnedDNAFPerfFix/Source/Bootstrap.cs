using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace UnburnedDNAFPerfFix;

[StaticConstructorOnStartup]
public static class Bootstrap
{
    public const string Owner = "whitegiver.unburned.dnaf.perffix";
    public static bool DnafActive { get; private set; }
    static Bootstrap()
    {
        Harmony harmony = new(Owner);
        WaitOptimization.Install(harmony);
        SealedCache.Install(harmony);
        MapSnapshot.Install(harmony);
        // Do not introduce an AssemblyRef to the optional 1.6 assembly. Its startup can
        // run in either order relative to ours; explicitly finish it before replacing hooks.
        Assembly? dnaf = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "DoNotAvoidFire");
        if (dnaf != null)
        {
            RuntimeHelpers.RunClassConstructor(dnaf.GetType("DoNotAvoidFire.HarmonyInit", true)!.TypeHandle);
            DnafBridge.Install(harmony, dnaf);
            DnafActive = true;
        }
        Log.Message($"[UnburnedDNAFPerfFix] 1.0.0 installed; DNAF={DnafActive}");
    }
}
