using System;
using DMSSR.Patches;
using HarmonyLib;
using Verse;
using DMSSR.Production;
using System.Threading;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace DMSSRStatPathPerfFix.Validation;

/// <summary>
/// Exact work counters for the upstream code paths under test. The counters are Harmony patches
/// owned by the validation harness, never by the mod.
/// </summary>
internal static class Probe
{
    internal static int ClampCalls;
    internal static int HasIgnoreTerrainPenaltyCalls;
    internal static int MechCustomizationLookupCalls;
    internal static int MechCustomizationLookupScanLength;
    internal static int SchizophreniaLookupCalls;
    internal static int ProviderDefChecks;
    internal static int DisplayLookups;
    internal static int WingedLookups;
    internal static int SyntheticLookups;

    private static Harmony? harmony;

    internal static void Install()
    {
        if (harmony != null)
        {
            return;
        }

        harmony = new Harmony(Program.ValidationOwner);

        harmony.Patch(
            AccessTools.Method(typeof(AbilityPathCostUtility), "ClampPathCostForAbility")!,
            postfix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(ClampPostfix))));

        harmony.Patch(
            AccessTools.Method(typeof(AbilityPathCostUtility), "HasIgnoreTerrainMovePenalty")!,
            postfix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(HasIgnorePostfix))));

        harmony.Patch(
            AccessTools.Method(typeof(HediffSet), nameof(HediffSet.GetFirstHediffOfDef))!,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(GetFirstHediffOfDefPrefix))));

        // HediffSet.DirtyCache's vanilla body walks render caches and Unity texture atlases, none of
        // which exist in a headless host. Skipping the body keeps the invalidation signal (postfixes
        // still run) without dragging rendering into the fixture.
        harmony.Patch(
            AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache), Type.EmptyTypes)!,
            prefix: new HarmonyMethod(AccessTools.Method(typeof(Probe), nameof(DirtyCachePrefix))));

        harmony.Patch(AccessTools.Method(typeof(DMSSR.Patch_PawnCapacity_Calc), "HediffDefHasProvider"),
            prefix: new HarmonyMethod(typeof(Probe), nameof(ProviderCheck)));
        harmony.Patch(AccessTools.Method(typeof(Building_HumanoidModification), nameof(Building_HumanoidModification.TryGetUnfinishedDisplayOwner)),
            prefix: new HarmonyMethod(typeof(Probe), nameof(DisplayCheck)));
        harmony.Patch(AccessTools.Method(typeof(DMSSR.Mech.ModExtension_WingedMech).Assembly.GetType("DMSSR.Mech.HydraFlightRendering"), "TryGetTransform"),
            transpiler: new HarmonyMethod(typeof(Probe), nameof(ExtensionCounters)) { priority = Priority.Last, after = new[] { Program.Owner } });
        harmony.Patch(AccessTools.Method(typeof(DMSSR.Content.Patch_SyntheticPresentationScale), "Postfix"),
            transpiler: new HarmonyMethod(typeof(Probe), nameof(ExtensionCounters)) { priority = Priority.Last, after = new[] { Program.Owner } });
    }

    internal static void Reset()
    {
        ClampCalls = 0;
        HasIgnoreTerrainPenaltyCalls = 0;
        MechCustomizationLookupCalls = 0;
        MechCustomizationLookupScanLength = 0;
        SchizophreniaLookupCalls = 0;
        ProviderDefChecks = 0;
        DisplayLookups = 0;
        WingedLookups = 0;
        SyntheticLookups = 0;
    }

    internal static void Remove()
    {
        harmony?.UnpatchAll(Program.ValidationOwner);
        harmony = null;
    }

    private static void ClampPostfix()
    {
        ClampCalls++;
    }

    private static void HasIgnorePostfix(ref bool __result)
    {
        _ = __result;
        HasIgnoreTerrainPenaltyCalls++;
    }

    private static void GetFirstHediffOfDefPrefix(HediffSet __instance, HediffDef def)
    {
        if (ReferenceEquals(def, DMSSR.Hediffs.DMS_HediffDefOf.DMS_Schizophrenia_Linked)) SchizophreniaLookupCalls++;
        if (!ReferenceEquals(def, DMSSR.Hediffs.DMS_HediffDefOf.DMS_MechCustomization))
        {
            return;
        }

        MechCustomizationLookupCalls++;
        MechCustomizationLookupScanLength = __instance.hediffs.Count;
    }

    private static void ProviderCheck() => ProviderDefChecks++;
    private static void DisplayCheck() => Interlocked.Increment(ref DisplayLookups);
    private static void WingedCheck() => Interlocked.Increment(ref WingedLookups);
    private static void SyntheticCheck() => Interlocked.Increment(ref SyntheticLookups);

    // Instrument the concrete CALL SITES, not Def.GetModExtension<T>: CoreCLR shares generic
    // reference-type bodies and detouring a closed generic can corrupt a different instantiation.
    private static IEnumerable<CodeInstruction> ExtensionCounters(IEnumerable<CodeInstruction> instructions)
    {
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.Name == nameof(Def.GetModExtension) && method.IsGenericMethod)
            {
                Type type = method.GetGenericArguments()[0];
                string counter = type == typeof(DMSSR.Mech.ModExtension_WingedMech) ? nameof(WingedCheck) : nameof(SyntheticCheck);
                CodeInstruction call = new(OpCodes.Call, AccessTools.Method(typeof(Probe), counter));
                call.labels.AddRange(instruction.labels);
                call.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                yield return call;
            }
            yield return instruction;
        }
    }

    private static bool DirtyCachePrefix()
    {
        return false;
    }
}
