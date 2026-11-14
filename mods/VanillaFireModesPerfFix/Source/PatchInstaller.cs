using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace WhiteGiver.VanillaFireModesPerfFix;

/// <summary>One Vanilla Fire Modes patch that this mod took over, kept so it can be restored verbatim.</summary>
internal sealed class ReplacedPatch
{
    internal MethodBase Target = null!;
    internal HarmonyPatchType Type;
    internal MethodInfo PatchMethod = null!;
    internal int Priority;
}

/// <summary>
/// Takes over Vanilla Fire Modes' burst-length patches.
///
/// The sequence is: prove that VFM's patches really are installed on the three expected methods, remove exactly
/// those three patch methods, install the replacements, prove that the replacements are live, and restore VFM's
/// patches byte-for-byte if anything in that sequence fails. Nothing is removed when VFM's patches are absent
/// (VFM not loaded, VFM updated, or another mod already took over), so this mod can never be the reason a save
/// gets two conflicting burst-length authorities.
///
/// Both of VFM's load phases are handled: VFM patches in its <see cref="Mod"/> constructor, this mod installs in
/// its own <see cref="Mod"/> constructor (guaranteed after VFM by <c>loadAfter</c>) and again from
/// <c>[StaticConstructorOnStartup]</c> (guaranteed after every mod constructor, even if the load order is broken).
/// <see cref="Apply"/> is idempotent in every phase.
/// </summary>
internal static class PatchInstaller
{
    internal const string VfmPatchTypeName = "VFM_VanillaFireModes.Patches.Patch_BurstShotCount";

    private const string VfmLockCount = "LockCount";
    private const string VfmGetterPostfix = "BurstShotCountPostFix";
    private const string VfmTickPostfix = "Postfix_Cleanup";

    /// <summary>Number of Vanilla Fire Modes patch methods this mod replaces.</summary>
    internal const int ReplacementCount = 3;

    private static readonly List<string> steps = new();
    private static bool installed;

    internal static IReadOnlyList<string> Steps => steps;

    internal static bool Installed => installed;

    /// <summary>Installs (or re-verifies) the replacement patches. Returns the number of installed patch methods.</summary>
    internal static int Apply(string phase)
    {
        Harmony harmony = new(VanillaFireModesPerfFixMod.HarmonyId);

        MethodInfo? warmup = AccessTools.Method(typeof(Verb), "WarmupComplete");
        MethodInfo? getter = AccessTools.PropertyGetter(typeof(Verb), "BurstShotCount");
        MethodInfo? tick = AccessTools.Method(typeof(Verb), "VerbTick");
        if (warmup == null || getter == null || tick == null)
        {
            throw new MissingMethodException(
                "Verse.Verb no longer exposes WarmupComplete, VerbTick and BurstShotCount; this RimWorld version is unsupported.");
        }

        Type? vfmPatches = AccessTools.TypeByName(VfmPatchTypeName);
        if (vfmPatches == null)
        {
            Log.Warning($"{VanillaFireModesPerfFixMod.LogPrefix} could not resolve {VfmPatchTypeName}; is Vanilla Fire Modes loaded? Nothing was changed.");
            return 0;
        }

        MethodInfo? vfmLock = AccessTools.Method(vfmPatches, VfmLockCount, new[] { typeof(Verb) });
        MethodInfo? vfmGetter = AccessTools.Method(vfmPatches, VfmGetterPostfix);
        MethodInfo? vfmTick = AccessTools.Method(vfmPatches, VfmTickPostfix, new[] { typeof(Verb) });
        if (vfmLock == null || vfmGetter == null || vfmTick == null)
        {
            Log.Warning($"{VanillaFireModesPerfFixMod.LogPrefix} Vanilla Fire Modes changed its patch method names; nothing was changed.");
            return 0;
        }

        List<ReplacedPatch> replaced = new(3);
        Collect(replaced, warmup, HarmonyPatchType.Prefix, vfmLock);
        Collect(replaced, getter, HarmonyPatchType.Postfix, vfmGetter);
        Collect(replaced, tick, HarmonyPatchType.Postfix, vfmTick);

        if (replaced.Count == 0)
        {
            if (installed)
            {
                Verify(warmup, getter);
                return ReplacementCount;
            }

            Log.Warning(
                $"{VanillaFireModesPerfFixMod.LogPrefix} Vanilla Fire Modes' burst-length patches are not installed " +
                "(another mod may already replace them); nothing was changed.");
            return 0;
        }

        foreach (ReplacedPatch patch in replaced)
        {
            harmony.Unpatch(patch.Target, patch.PatchMethod);
        }

        try
        {
            harmony.Patch(
                warmup,
                prefix: new HarmonyMethod(typeof(BurstShotCountPatches), nameof(BurstShotCountPatches.LockCount))
                {
                    priority = Priority.First,
                },
                finalizer: new HarmonyMethod(typeof(BurstShotCountPatches), nameof(BurstShotCountPatches.UnlockWarmup))
                {
                    priority = Priority.First,
                });

            harmony.Patch(
                getter,
                postfix: new HarmonyMethod(typeof(BurstShotCountPatches), nameof(BurstShotCountPatches.ApplyLockedCount))
                {
                    priority = Priority.First,
                });

            Verify(warmup, getter);
        }
        catch (Exception exception)
        {
            Rollback(harmony, warmup, getter, replaced);
            throw new InvalidOperationException(
                $"{VanillaFireModesPerfFixMod.LogPrefix} could not install its replacement patches; " +
                "Vanilla Fire Modes' own patches were restored.", exception);
        }

        installed = true;
        steps.Clear();
        steps.Add($"replaced {replaced.Count} Vanilla Fire Modes patch method(s)");
        steps.Add("Verb.WarmupComplete -> LockCount prefix + UnlockWarmup finalizer");
        steps.Add("Verb.get_BurstShotCount -> ApplyLockedCount postfix");
        steps.Add("Verb.VerbTick -> hook deleted (validity is derived from Verb.state, so no tick probe is needed)");
        Log.Message(
            $"{VanillaFireModesPerfFixMod.LogPrefix} {phase}: replaced {replaced.Count} Vanilla Fire Modes patch " +
            "method(s); VerbTick no longer performs any burst-table lookup.");
        return ReplacementCount;
    }

    /// <summary>Removes this mod's patches (used by the offline harness to return to VFM behaviour).</summary>
    internal static void Remove()
    {
        Harmony harmony = new(VanillaFireModesPerfFixMod.HarmonyId);
        harmony.UnpatchAll(VanillaFireModesPerfFixMod.HarmonyId);
        installed = false;
        steps.Clear();
    }

    private static void Collect(
        List<ReplacedPatch> target,
        MethodBase original,
        HarmonyPatchType type,
        MethodInfo vfmMethod)
    {
        Patches? info = Harmony.GetPatchInfo(original);
        if (info == null)
        {
            return;
        }

        IEnumerable<Patch> patches = type == HarmonyPatchType.Prefix ? info.Prefixes : info.Postfixes;
        foreach (Patch patch in patches)
        {
            if (patch.owner == BurstShotCountPatches.VfmHarmonyId && patch.PatchMethod == vfmMethod)
            {
                target.Add(new ReplacedPatch
                {
                    Target = original,
                    Type = type,
                    PatchMethod = vfmMethod,
                    Priority = patch.priority,
                });
            }
        }
    }

    private static void Verify(MethodBase warmup, MethodBase getter)
    {
        MethodInfo lockCount = AccessTools.Method(typeof(BurstShotCountPatches), nameof(BurstShotCountPatches.LockCount))!;
        MethodInfo unlock = AccessTools.Method(typeof(BurstShotCountPatches), nameof(BurstShotCountPatches.UnlockWarmup))!;
        MethodInfo apply = AccessTools.Method(typeof(BurstShotCountPatches), nameof(BurstShotCountPatches.ApplyLockedCount))!;

        Patches warmupInfo = Harmony.GetPatchInfo(warmup)
            ?? throw new InvalidOperationException("Verb.WarmupComplete reports no patch info after patching.");
        Patches getterInfo = Harmony.GetPatchInfo(getter)
            ?? throw new InvalidOperationException("Verb.get_BurstShotCount reports no patch info after patching.");

        Require(warmupInfo.Prefixes.Any(p => p.PatchMethod == lockCount && p.owner == VanillaFireModesPerfFixMod.HarmonyId),
            "the WarmupComplete burst lock prefix is not active");
        Require(warmupInfo.Finalizers.Any(p => p.PatchMethod == unlock && p.owner == VanillaFireModesPerfFixMod.HarmonyId),
            "the WarmupComplete warm-up-window finalizer is not active");
        Require(getterInfo.Postfixes.Any(p => p.PatchMethod == apply && p.owner == VanillaFireModesPerfFixMod.HarmonyId),
            "the BurstShotCount postfix is not active");

        Require(!warmupInfo.Prefixes.Any(p => p.owner == BurstShotCountPatches.VfmHarmonyId),
            "a Vanilla Fire Modes prefix is still attached to Verb.WarmupComplete");
        Require(!getterInfo.Postfixes.Any(p => p.owner == BurstShotCountPatches.VfmHarmonyId),
            "a Vanilla Fire Modes postfix is still attached to Verb.BurstShotCount");
    }

    private static void Rollback(Harmony harmony, MethodBase warmup, MethodBase getter, List<ReplacedPatch> replaced)
    {
        try
        {
            harmony.Unpatch(warmup, AccessTools.Method(typeof(BurstShotCountPatches), nameof(BurstShotCountPatches.LockCount))!);
            harmony.Unpatch(warmup, AccessTools.Method(typeof(BurstShotCountPatches), nameof(BurstShotCountPatches.UnlockWarmup))!);
            harmony.Unpatch(getter, AccessTools.Method(typeof(BurstShotCountPatches), nameof(BurstShotCountPatches.ApplyLockedCount))!);

            foreach (ReplacedPatch patch in replaced)
            {
                HarmonyMethod method = new(patch.PatchMethod) { priority = patch.Priority };
                if (patch.Type == HarmonyPatchType.Prefix)
                {
                    harmony.Patch(patch.Target, prefix: method);
                }
                else
                {
                    harmony.Patch(patch.Target, postfix: method);
                }
            }

            Log.Warning($"{VanillaFireModesPerfFixMod.LogPrefix} rolled back and restored {replaced.Count} Vanilla Fire Modes patch method(s).");
        }
        catch (Exception exception)
        {
            Log.Error($"{VanillaFireModesPerfFixMod.LogPrefix} rollback failed; restart RimWorld to restore Vanilla Fire Modes.\n{exception}");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
