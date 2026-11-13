using System;
using DMSSR.Patches;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DMSSRStatPathPerfFix.Validation;

/// <summary>
/// Applies DMSSR's three relevant Harmony patches exactly the way its own PatchAll would, because
/// the offline harness never runs RimWorld's mod loader. The real patch methods from the real
/// DMSSR.dll are used; nothing is re-implemented here.
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

    internal static void Apply()
    {
        if (harmony != null)
        {
            return;
        }

        harmony = new Harmony("whitegiver.dmssr.statpathperffix.upstream-fixture");

        InstanceMethod = new PatchTarget(
            AccessTools.Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(IntVec3) })!,
            AccessTools.Method(typeof(Patch_Ability_PathCost_Instance), "Postfix")!);
        StaticMethod = new PatchTarget(
            AccessTools.Method(typeof(Pawn_PathFollower), "CostToMoveIntoCell", new[] { typeof(Pawn), typeof(IntVec3) })!,
            AccessTools.Method(typeof(Patch_Ability_PathCost_Static), "Postfix")!);
        StatMethod = new PatchTarget(
            AccessTools.Method(typeof(StatWorker), "GetValueUnfinalized", new[] { typeof(StatRequest), typeof(bool) })!,
            AccessTools.Method(typeof(Patch_MechCustomization_ApplyStatPreFinalize), "Postfix")!);

        harmony.Patch(InstanceMethod.Method, postfix: new HarmonyMethod(InstanceMethod.Postfix));
        harmony.Patch(StaticMethod.Method, postfix: new HarmonyMethod(StaticMethod.Postfix));
        harmony.Patch(StatMethod.Method, postfix: new HarmonyMethod(StatMethod.Postfix));
    }

    internal static void Remove()
    {
        harmony?.UnpatchAll("whitegiver.dmssr.statpathperffix.upstream-fixture");
        harmony = null;
    }
}
