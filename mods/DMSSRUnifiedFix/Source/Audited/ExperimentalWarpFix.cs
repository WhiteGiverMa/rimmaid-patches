using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DMSSR.Content;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// The Frame Shift device destroys its user on the success branch. <see cref="Pawn.Destroy"/> is
/// patched by several mods (Milira installs two prefixes and a finalizer), and with those loaded
/// the call throws before any bookkeeping happens, leaving the item consumed, research granted and
/// no success letter. Keep the authored flow and replace only that one call with a guarded removal.
/// </summary>
internal static class ExperimentalWarpFix
{
    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        MethodInfo target = AccessTools.Method(
            typeof(CompUseEffect_ExperimentalWarp), nameof(CompUseEffect_ExperimentalWarp.DoEffect), new[] { typeof(Pawn) })
            ?? throw new MissingMethodException("DMSSR.Content.CompUseEffect_ExperimentalWarp.DoEffect");
        MethodInfo destroy = AccessTools.Method(typeof(Thing), nameof(Thing.Destroy), new[] { typeof(DestroyMode) })
            ?? throw new MissingMethodException("Verse.Thing.Destroy");
        Validate(PatchProcessor.GetOriginalInstructions(target), destroy);
        MethodInfo transpiler = AccessTools.Method(typeof(ExperimentalWarpFix), nameof(Transpiler));
        harmony.Patch(target, transpiler: new HarmonyMethod(transpiler));
        Installed = true;
    }

    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = new(instructions);
        MethodInfo destroy = AccessTools.Method(typeof(Thing), nameof(Thing.Destroy), new[] { typeof(DestroyMode) });
        List<int> matches = Validate(result, destroy);
        CodeInstruction replacement = new(OpCodes.Call, AccessTools.Method(typeof(ExperimentalWarpFix), nameof(RemoveUser)));
        replacement.labels.AddRange(result[matches[1]].labels);
        result[matches[1]] = replacement;
        return result;
    }

    private static List<int> Validate(IEnumerable<CodeInstruction> instructions, MethodInfo destroy)
    {
        List<int> matches = new();
        int index = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(destroy)) matches.Add(index);
            index++;
        }
        if (matches.Count != 2)
            throw new InvalidOperationException("CompUseEffect_ExperimentalWarp.DoEffect: expected two Thing.Destroy calls, found " + matches.Count + ".");
        return matches;
    }

    private static void RemoveUser(Thing thing, DestroyMode mode)
    {
        if (thing is not Pawn pawn)
        {
            thing.Destroy(mode);
            return;
        }
        try
        {
            pawn.Destroy(mode);
        }
        catch (Exception exception)
        {
            SafeLog.Warning("experimental Frame Shift user removal failed, falling back to Kill. " + exception.GetType().Name + ": " + exception.Message);
            if (pawn.Destroyed) return;
            if (!pawn.Dead) pawn.Kill(null);
            if (pawn.Corpse is { Destroyed: false } corpse) corpse.Destroy(DestroyMode.Vanish);
        }
    }
}
