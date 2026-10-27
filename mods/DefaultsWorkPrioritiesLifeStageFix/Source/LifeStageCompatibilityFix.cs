using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.DefaultsWorkPrioritiesLifeStageFix;

public sealed class DefaultsLifeStageFixMod : Mod
{
    public DefaultsLifeStageFixMod(ModContentPack content) : base(content)
    {
        LifeStageCompatibilityFix.Apply();
    }
}

public static class LifeStageCompatibilityFix
{
    public const string HarmonyId = "whitegiver.defaults.workprioritieslifestagefix";
    private const string LogPrefix = "[Defaults Life Stage Fix]";
    private static readonly MethodInfo OriginalFirst = typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(Enumerable.First) && method.GetParameters().Length == 2)
        .MakeGenericMethod(typeof(LifeStageAge));
    private static readonly MethodInfo OriginalMinAge = AccessTools.DeclaredMethod(typeof(Pawn_AgeTracker),
        nameof(Pawn_AgeTracker.LifeStageMinAge), new[] { typeof(LifeStageDef) });
    private static readonly FieldInfo AgePawn = AccessTools.Field(typeof(Pawn_AgeTracker), "pawn");
    private static readonly LifeStageAge NoChildLifeStage = new() { minAge = -1f };
    private static bool applied;

    public static void Apply()
    {
        if (applied) return;
        Harmony harmony = new(HarmonyId);
        try
        {
            Type? workType = AccessTools.TypeByName("Defaults.WorkPriorities.Patch_Pawn");
            Type? policyType = AccessTools.TypeByName("Defaults.Policies.Patch_Pawn_AgeTracker");
            if (workType == null && policyType == null)
            {
                Log.Message($"{LogPrefix} Defaults is not loaded; skipped.");
                return;
            }

            MethodInfo? workPostfix = workType == null ? null : AccessTools.DeclaredMethod(workType, "Postfix", new[] { typeof(Pawn) });
            MethodInfo? policyPostfix = policyType == null ? null : AccessTools.DeclaredMethod(policyType, "Postfix",
                new[] { typeof(Pawn_AgeTracker), typeof(Pawn), typeof(int) });
            if (!ValidPostfix(workPostfix, typeof(Pawn)) || !ValidPolicyPostfix(policyPostfix) || AgePawn == null)
            {
                Log.Error($"{LogPrefix} Expected Defaults Postfix signatures or Pawn_AgeTracker.pawn are missing; patch group not applied.");
                return;
            }

            int firstCalls = PatchProcessor.GetOriginalInstructions(workPostfix).Count(instruction => instruction.Calls(OriginalFirst));
            int minAgeCalls = PatchProcessor.GetOriginalInstructions(policyPostfix).Count(instruction => instruction.Calls(OriginalMinAge));
            if (firstCalls != 1 || minAgeCalls != 1)
            {
                Log.Error($"{LogPrefix} Expected one First<LifeStageAge> and one LifeStageMinAge call, found {firstCalls} and {minAgeCalls}; patch group not applied.");
                return;
            }

            harmony.Patch(workPostfix, transpiler: new HarmonyMethod(typeof(LifeStageCompatibilityFix), nameof(WorkPrioritiesTranspiler)));
            harmony.Patch(policyPostfix, transpiler: new HarmonyMethod(typeof(LifeStageCompatibilityFix), nameof(PoliciesTranspiler)));
            applied = true;
            Log.Message($"{LogPrefix} Patched Defaults work-priority and policy life-stage lookups (two call sites).");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(HarmonyId);
            Log.Error($"{LogPrefix} Could not apply life-stage compatibility patch group: {exception}");
        }
    }

    private static bool ValidPostfix(MethodInfo? method, Type parameterType) =>
        method != null && method.IsStatic && method.ReturnType == typeof(void) &&
        method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { parameterType });

    private static bool ValidPolicyPostfix(MethodInfo? method) =>
        method != null && method.IsStatic && method.ReturnType == typeof(void) &&
        method.GetParameters().Select(parameter => parameter.ParameterType)
            .SequenceEqual(new[] { typeof(Pawn_AgeTracker), typeof(Pawn), typeof(int) });

    private static IEnumerable<CodeInstruction> WorkPrioritiesTranspiler(IEnumerable<CodeInstruction> instructions) =>
        ReplaceOne(instructions, OriginalFirst, nameof(FirstChildLifeStage), "First<LifeStageAge>");

    private static IEnumerable<CodeInstruction> PoliciesTranspiler(IEnumerable<CodeInstruction> instructions) =>
        ReplaceOne(instructions, OriginalMinAge, nameof(ChildMinAge), "LifeStageMinAge");

    private static IEnumerable<CodeInstruction> ReplaceOne(IEnumerable<CodeInstruction> instructions, MethodInfo original,
        string replacementName, string expectedCall)
    {
        List<CodeInstruction> codes = instructions.ToList();
        if (codes.Count(instruction => instruction.Calls(original)) != 1)
        {
            throw new InvalidOperationException($"{LogPrefix} Expected exactly one {expectedCall} call.");
        }

        MethodInfo replacement = AccessTools.Method(typeof(LifeStageCompatibilityFix), replacementName);
        foreach (CodeInstruction instruction in codes)
        {
            if (!instruction.Calls(original)) continue;
            instruction.opcode = OpCodes.Call;
            instruction.operand = replacement;
        }
        return codes;
    }

    public static LifeStageAge FirstChildLifeStage(IEnumerable<LifeStageAge> lifeStages, Func<LifeStageAge, bool> originalPredicate)
    {
        // 单点替换保留原 IL 的 minAge 读取；-1 在非负生物年龄域内与上游 nullable 缺值比较等价为 false。
        return lifeStages.FirstOrDefault(stage => stage.def.developmentalStage.Child()) ?? NoChildLifeStage;
    }

    public static float ChildMinAge(Pawn_AgeTracker ageTracker, LifeStageDef originalDef)
    {
        Pawn pawn = (Pawn)AgePawn.GetValue(ageTracker)!;
        LifeStageAge? child = pawn.RaceProps.lifeStageAges.Find(stage => stage.def.developmentalStage.Child());
        // NaN 与上游 nullable 比较一样恒为 false，同时避免 LifeStageMinAge 的缺项红字和 0 岁误判。
        return child?.minAge ?? float.NaN;
    }
}
