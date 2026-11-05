using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace WhiteGiver.StartCarryReentrancyFix;

public sealed class StartCarryReentrancyFixMod : Mod
{
    public StartCarryReentrancyFixMod(ModContentPack content) : base(content) => CarryReentrancyFix.Apply();
}

public static class CarryReentrancyFix
{
    public const string HarmonyId = "whitegiver.hauling.startcarryreentrancyfix";
    private const string LogPrefix = "[StartCarry Reentrancy Fix]";
    private static readonly MethodInfo Carry = AccessTools.Method(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryStartCarry),
        new[] { typeof(Thing), typeof(int), typeof(bool) });
    private static readonly MethodInfo EndJob = AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob));

    public static void Apply()
    {
        var harmony = new Harmony(HarmonyId);
        try
        {
            MethodInfo[] candidates = typeof(Toils_Haul).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                .SelectMany(type => AccessTools.GetDeclaredMethods(type))
                .Where(method => method.Name.StartsWith("<StartCarryThing>b__", StringComparison.Ordinal) &&
                    method.ReturnType == typeof(void) && method.GetParameters().Length == 0).ToArray();
            if (candidates.Length != 1) throw new InvalidOperationException("Expected one StartCarryThing initAction.");
            MethodInfo target = candidates[0];
            Validate(PatchProcessor.GetOriginalInstructions(target), target);
            harmony.Patch(target, transpiler: new HarmonyMethod(typeof(CarryReentrancyFix), nameof(Transpiler)));
            Log.Message($"{LogPrefix} Patched StartCarryThing initAction (failed/replaced-job early exit).");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(HarmonyId);
            Log.Error($"{LogPrefix} Expected callback changed; no patch retained: {exception}");
        }
    }

    private static int Validate(List<CodeInstruction> codes, MethodBase method)
    {
        MethodBody? body = method.GetMethodBody();
        if (body == null || body.ExceptionHandlingClauses.Count != 0 || body.LocalVariables.Count < 2 ||
            body.LocalVariables[0].LocalType != typeof(Pawn) || body.LocalVariables[1].LocalType != typeof(Job))
            throw new InvalidOperationException("Unexpected StartCarryThing actor/job locals or exception regions.");
        int[] carries = codes.Select((code, index) => (code, index)).Where(pair => pair.code.Calls(Carry)).Select(pair => pair.index).ToArray();
        int[] endings = codes.Select((code, index) => (code, index)).Where(pair => pair.code.Calls(EndJob)).Select(pair => pair.index).ToArray();
        if (carries.Length != 1 || endings.Length != 1) throw new InvalidOperationException("Expected one carry and one failure EndCurrentJob call.");
        int carry = carries[0], end = endings[0];
        if (carry + 4 >= codes.Count || end <= carry + 4 || end + 1 >= codes.Count ||
            !codes[carry + 1].IsStloc() || !codes[carry + 2].IsLdloc() ||
            (codes[carry + 3].opcode != OpCodes.Brtrue && codes[carry + 3].opcode != OpCodes.Brtrue_S) ||
            codes.Skip(carry + 4).Take(end - carry - 3).Any(code => code.opcode == OpCodes.Ret) ||
            codes[end + 1].opcode == OpCodes.Ret)
            throw new InvalidOperationException("The unsafe zero-carry continuation is missing or changed; review before applying.");
        return carry;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
    {
        List<CodeInstruction> codes = instructions.ToList();
        int index = Validate(codes, __originalMethod);
        LocalBuilder generation = generator.DeclareLocal(typeof(int));
        LocalBuilder driver = generator.DeclareLocal(typeof(JobDriver));
        var capture = new List<CodeInstruction>
        {
            new(OpCodes.Ldloc_1),
            new(OpCodes.Ldfld, AccessTools.Field(typeof(Job), nameof(Job.loadID))),
            new(OpCodes.Stloc, generation),
            new(OpCodes.Ldloc_0),
            new(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn), nameof(Pawn.jobs))),
            new(OpCodes.Ldfld, AccessTools.Field(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.curDriver))),
            new(OpCodes.Stloc, driver)
        };
        capture[0].labels.AddRange(codes[index].labels);
        codes[index].labels.Clear();
        capture[0].blocks.AddRange(codes[index].blocks);
        codes[index].blocks.Clear();
        Label proceed = generator.DefineLabel();
        codes[index + 2].labels.Add(proceed);
        var guard = new List<CodeInstruction>
        {
            new(OpCodes.Ldloc_0), new(OpCodes.Ldloc_1), new(OpCodes.Ldloc, generation), new(OpCodes.Ldloc, driver),
            new(codes[index + 2].opcode, codes[index + 2].operand),
            new(OpCodes.Call, AccessTools.Method(typeof(CarryReentrancyFix), nameof(ShouldAbort))),
            new(OpCodes.Brfalse, proceed), new(OpCodes.Ret)
        };
        codes.InsertRange(index + 2, guard);
        codes.InsertRange(index, capture);
        return codes;
    }

    public static bool ShouldAbort(Pawn actor, Job job, int generation, JobDriver driver, int carriedCount)
    {
        // ponytail: pooled Jobs can retain reference identity while acquiring a new loadID; do not touch that new task.
        if (!ReferenceEquals(actor.jobs.curJob, job) || job.loadID != generation ||
            !ReferenceEquals(actor.jobs.curDriver, driver) || driver.ended) return true;
        if (carriedCount != 0) return false;
        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
        return true;
    }
}
