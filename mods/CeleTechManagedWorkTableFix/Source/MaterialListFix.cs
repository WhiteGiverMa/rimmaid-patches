using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Meidocho.CeleTechManagedWorkTableFix;

public sealed class CeleTechManagedWorkTableFixMod : Mod
{
    public CeleTechManagedWorkTableFixMod(ModContentPack content) : base(content)
    {
        MaterialListFix.Apply();
    }
}

public static class MaterialListFix
{
    public const string HarmonyId = "meidocho.celetech.managedworktablefix";
    private const string LogPrefix = "[CeleTech Managed WorkTable Fix]";
    private static readonly MethodInfo Original = AccessTools.Method(typeof(ListerThings),
        nameof(ListerThings.ThingsInGroup), new[] { typeof(ThingRequestGroup) });

    public static void Apply()
    {
        try
        {
            Type? hub = AccessTools.TypeByName("CeleTech.Base.Building_ManagedWorkTable");
            if (hub == null)
            {
                Log.Message($"{LogPrefix} CeleTech automation hub is not loaded; skipped.");
                return;
            }

            MethodInfo? consume = AccessTools.DeclaredMethod(hub, "TryConsumeGatheredMaterials", Type.EmptyTypes);
            if (consume == null || consume.IsStatic || consume.ReturnType != typeof(void))
            {
                Log.Error($"{LogPrefix} Expected TryConsumeGatheredMaterials signature is missing; patch not applied.");
                return;
            }

            int calls = PatchProcessor.GetOriginalInstructions(consume).Count(instruction => instruction.Calls(Original));
            if (calls != 1)
            {
                Log.Error($"{LogPrefix} Expected one ThingsInGroup call in TryConsumeGatheredMaterials, found {calls}; patch not applied.");
                return;
            }

            new Harmony(HarmonyId).Patch(consume,
                transpiler: new HarmonyMethod(typeof(MaterialListFix), nameof(Transpiler)));
            Log.Message($"{LogPrefix} Patched TryConsumeGatheredMaterials (one material-list snapshot).");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Could not apply material-list compatibility patch: {exception}");
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = instructions.ToList();
        if (codes.Count(instruction => instruction.Calls(Original)) != 1)
        {
            throw new InvalidOperationException($"{LogPrefix} TryConsumeGatheredMaterials must contain exactly one ThingsInGroup call.");
        }

        MethodInfo replacement = AccessTools.Method(typeof(MaterialListFix), nameof(SnapshotMaterials));
        foreach (CodeInstruction instruction in codes)
        {
            if (!instruction.Calls(Original)) continue;
            instruction.opcode = OpCodes.Call;
            instruction.operand = replacement;
        }
        return codes;
    }

    public static List<Thing> SnapshotMaterials(ListerThings lister, ThingRequestGroup group)
    {
        // ponytail: 只复制此处枚举，筛选和扣料仍由上游负责；上游改为安全枚举后退役。
        return new List<Thing>(lister.ThingsInGroup(group));
    }
}
