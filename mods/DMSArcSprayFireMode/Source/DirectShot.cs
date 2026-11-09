using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSArcSprayFireMode;

/// <summary>
/// A verified reverse copy of vanilla's TryCastShot, with a Verb receiver. There is no
/// cast between the unrelated spray and launcher classes, proxy shooter or copied hit math.
/// Only launcher-specific helpers and its incompatible charge policy are substituted.
/// </summary>
public static class DirectShot
{
    private static readonly ConditionalWeakTable<Verb, ForcedMissCache> ForcedMiss = new();
    private static readonly AccessTools.FieldRef<Verb, int> Remaining = AccessTools.FieldRefAccess<Verb, int>("burstShotsLeft");
    internal static int ShotsLeft(Verb source) => Remaining(source);
    internal static void ResetCache(Verb source) => ForcedMiss.Remove(source);

    internal static void Install(Harmony harmony)
    {
        MethodInfo original = ArcFireModes.Require(typeof(Verb_LaunchProjectile), "TryCastShot", typeof(bool), false);
        // Preflight the exact same transformation before installing any forward hooks.
        _ = Rewrite(PatchProcessor.GetOriginalInstructions(original)).ToArray();
        harmony.CreateReversePatcher(original, new HarmonyMethod(typeof(DirectShot), nameof(Fire))).Patch();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Fire(Verb source)
    {
        _ = Transpiler(Array.Empty<CodeInstruction>());
        throw new InvalidOperationException("Vanilla direct-shot reverse patch has not been installed.");
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Rewrite(instructions);
    }

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.Select(instruction => new CodeInstruction(instruction)).ToList();
        int projectile = 0, forced = 0, debug = 0, changeable = 0, charged = 0;
        int report = 0, wild = 0, cover = 0;
        foreach (CodeInstruction instruction in list)
        {
            if (instruction.operand is not MethodInfo method) continue;
            if (method.DeclaringType == typeof(Verb_LaunchProjectile))
            {
                string helper;
                switch (method.Name)
                {
                    case "get_Projectile": helper = nameof(ProjectileFor); projectile++; break;
                    case "GetForcedMissTarget": helper = nameof(ForcedTarget); forced++; break;
                    case "ThrowDebugText":
                        helper = method.GetParameters().Length == 1 ? nameof(DebugText) : nameof(DebugCell);
                        debug++;
                        break;
                    default: throw new InvalidOperationException("Unexpected launcher-only call: " + method);
                }
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.DeclaredMethod(typeof(DirectShot), helper)
                    ?? throw new MissingMethodException(typeof(DirectShot).FullName, helper);
            }
            else if (method.DeclaringType == typeof(CompChangeableProjectile) && method.Name == "Notify_ProjectileLaunched")
            {
                changeable++;
                instruction.opcode = OpCodes.Pop;
                instruction.operand = null;
            }
            else if (method.DeclaringType == typeof(CompApparelVerbOwner) && method.Name == "UsedOnce")
            {
                charged++;
                instruction.opcode = OpCodes.Pop;
                instruction.operand = null;
            }
            if (method.DeclaringType == typeof(ShotReport) && method.Name == "HitReportFor") report++;
            if (method.DeclaringType == typeof(ShootLine) && method.Name == "ChangeDestToMissWild") wild++;
            if (method.DeclaringType == typeof(ShotReport) && method.Name == "get_PassCoverChance") cover++;
        }
        if (projectile != 1 || forced != 1 || debug != 9 || changeable != 1 || charged != 1 || report != 1 || wild != 1 || cover != 1)
            throw new InvalidOperationException($"Vanilla shot IL drift: projectile={projectile}, forced={forced}, debug={debug}, changeable={changeable}, charged={charged}, report={report}, wild={wild}, cover={cover}.");
        foreach (CodeInstruction instruction in list)
            if (instruction.operand is MemberInfo member && member.DeclaringType == typeof(Verb_LaunchProjectile))
                throw new InvalidOperationException("Untranslated launcher-only member: " + member);
        return list;
    }

    public static ThingDef ProjectileFor(Verb source)
    {
        CompChangeableProjectile? comp = source.EquipmentSource?.GetComp<CompChangeableProjectile>();
        return comp?.Loaded == true ? comp.Projectile : source.verbProps.defaultProjectile;
    }

    public static IntVec3 ForcedTarget(Verb source, float radius) => ForcedMiss.GetOrCreateValue(source).Next(source, radius);

    // This object samples vanilla forced-miss cells only. It never casts, fires, owns a verb
    // tracker, emits UI or enters a save. Its own base fields are valid launcher fields.
    private sealed class ForcedMissCache : Verb_LaunchProjectile
    {
        internal IntVec3 Next(Verb source, float radius)
        {
            caster = source.caster;
            verbProps = source.verbProps;
            currentTarget = source.CurrentTarget;
            burstShotsLeft = ShotsLeft(source);
            return GetForcedMissTarget(radius);
        }
    }

    public static void DebugText(Verb source, string text)
    {
        if (DebugViewSettings.drawShooting) MoteMaker.ThrowText(source.caster.DrawPos, source.caster.Map, text);
    }
    public static void DebugCell(Verb source, string text, IntVec3 cell)
    {
        if (DebugViewSettings.drawShooting) MoteMaker.ThrowText(cell.ToVector3Shifted(), source.caster.Map, text);
    }
}
