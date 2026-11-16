using System;
using System.Linq;
using System.Reflection;
using DMSSR.Patches;
using DMSSR.Content;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DMSSRStatPathPerfFix.Validation;

/// <summary>
/// Runs the real DMSSR bootstrap with PatchAll restricted to the target classes in this offline
/// host. The legacy three-target fixture remains for the original path/stat A/B/C benchmark.
/// </summary>
internal static class UpstreamPatches
{
    internal sealed class PatchTarget
    {
        internal PatchTarget(System.Reflection.MethodInfo method, System.Reflection.MethodInfo postfix)
        {
            Method = method;
            Postfix = postfix;
        }

        internal System.Reflection.MethodInfo Method { get; }

        internal System.Reflection.MethodInfo Postfix { get; }
    }

    internal static PatchTarget InstanceMethod { get; private set; } = null!;

    internal static PatchTarget StaticMethod { get; private set; } = null!;

    internal static PatchTarget StatMethod { get; private set; } = null!;

    private static Harmony? harmony;

    internal static readonly Type[] HotspotTypes =
    {
        typeof(Patch_Ability_PathCost_Instance), typeof(Patch_Ability_PathCost_Static),
        typeof(Patch_MechCustomization_ApplyStatPreFinalize),
        typeof(Patch_MechCustomization_ApplyCapacity), typeof(Patch_MechCustomization_BodySize),
        typeof(Patch_MechCustomization_MechEnergyMax), typeof(Patch_MechCustomization_MechEnergyThresholdMin),
        typeof(Patch_MechCustomization_MechEnergyThresholdMax), typeof(Patch_MechCustomization_SkillBonus_GetLevel),
        typeof(Patch_MechCustomization_SkillBonus_GetLevelForUI), typeof(Patch_Schizophrenia_ConsciousnessFloor),
        typeof(DMSSR.Patch_PawnCapacity_Calc), typeof(Patch_EmbryteWeaver_UnfinishedDraw),
        typeof(Patch_EmbryteWeaver_UnfinishedPrint), typeof(Patch_SyntheticPresentationScale),
        typeof(Patch_Ability_PathCost_Static).Assembly.GetTypes().Single(t => t.Name == "Patch_HydraFlightRenderMatrix"),
    };

    // Execute the REAL EnsurePatched body, including its idempotence flag. Only PatchAll's scope
    // is restricted to the real hotspot classes: unrelated graphics/content bootstraps need Unity.
    internal static void EnsurePatched()
    {
        PrepareBootstrapScope();
        Type bootstrap = typeof(Patch_Ability_PathCost_Static).Assembly.GetType("DMSSR.Misc.HarmonyBootstrap", true)!;
        AccessTools.Method(bootstrap, "EnsurePatched").Invoke(null, null);
        if (!(bool)AccessTools.Field(bootstrap, "patched").GetValue(null)!)
            throw new InvalidOperationException("Real DMSSR EnsurePatched did not complete.");
    }

    internal static void PrepareBootstrapScope()
    {
        Harmony qa = new(Program.ValidationOwner);
        MethodInfo patchAll = AccessTools.Method(typeof(Harmony), nameof(Harmony.PatchAll), new[] { typeof(Assembly) });
        qa.Patch(patchAll, prefix: new HarmonyMethod(typeof(UpstreamPatches), nameof(ScopedPatchAll)));
        qa.Patch(AccessTools.Method(typeof(Log), nameof(Log.Message), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(UpstreamPatches), nameof(SkipLog)));
        qa.Patch(AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) }),
            prefix: new HarmonyMethod(typeof(UpstreamPatches), nameof(FailLog)));
    }

    private static bool ScopedPatchAll(Harmony __instance, Assembly assembly)
    {
        if (assembly != typeof(Patch_Ability_PathCost_Static).Assembly) return true;
        foreach (Type type in HotspotTypes) __instance.CreateClassProcessor(type).Patch();
        return false;
    }

    private static bool SkipLog(string text) { Console.WriteLine("GAME LOG " + text); return false; }
    private static bool FailLog(string text) => throw new InvalidOperationException(text);

    internal static void Apply(bool metadataOnly = false)
    {
        if (harmony != null)
        {
            return;
        }

        InstanceMethod = new PatchTarget(
            AccessTools.Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(IntVec3) })!,
            AccessTools.Method(typeof(Patch_Ability_PathCost_Instance), "Postfix")!);
        StaticMethod = new PatchTarget(
            AccessTools.Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(Pawn), typeof(IntVec3) })!,
            AccessTools.Method(typeof(Patch_Ability_PathCost_Static), "Postfix")!);
        StatMethod = new PatchTarget(
            AccessTools.Method(typeof(StatWorker), "GetValueUnfinalized", new[] { typeof(StatRequest), typeof(bool) })!,
            AccessTools.Method(typeof(Patch_MechCustomization_ApplyStatPreFinalize), "Postfix")!);

        if (metadataOnly) return;
        harmony = new Harmony("whitegiver.dmssr.statpathperffix.upstream-fixture");
        harmony.Patch(InstanceMethod.Method, postfix: new HarmonyMethod(InstanceMethod.Postfix));
        harmony.Patch(StaticMethod.Method, postfix: new HarmonyMethod(StaticMethod.Postfix));
        harmony.Patch(StatMethod.Method, postfix: new HarmonyMethod(StatMethod.Postfix));
    }

    internal static void Remove()
    {
        new Harmony("DMSSR").UnpatchAll("DMSSR");
        harmony?.UnpatchAll("whitegiver.dmssr.statpathperffix.upstream-fixture");
        harmony = null;
    }
}
