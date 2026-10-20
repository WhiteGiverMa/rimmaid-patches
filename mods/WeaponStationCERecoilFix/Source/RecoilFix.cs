using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Meidocho.WeaponStationCERecoilFix;

public sealed class WeaponStationCERecoilFixMod : Mod
{
    public WeaponStationCERecoilFixMod(ModContentPack content) : base(content)
    {
        RecoilFix.Apply();
    }
}

public static class RecoilFix
{
    public const string HarmonyId = "meidocho.weaponstation.cerecoilfix";
    private const string LogPrefix = "[Weapon Station CE Recoil Fix]";

    private delegate void RecoilDelegate(ThingDef weaponDef, Verb shootVerb, out Vector3 drawOffset,
        out float angleOffset, float aimAngle, bool handheld);

    private static Type? ceProperties;
    private static MethodInfo? originalRecoil;
    private static RecoilDelegate? recoil;

    public static void Apply()
    {
        try
        {
            Type? station = AccessTools.TypeByName("WeaponStation_CE.Building_WeaponStationCE");
            if (station == null)
            {
                Log.Message($"{LogPrefix} CE weapon station is not loaded; skipped.");
                return;
            }

            Type? utility = AccessTools.TypeByName("CombatExtended.CE_Utility");
            Type? properties = AccessTools.TypeByName("CombatExtended.VerbPropertiesCE");
            MethodInfo? draw = AccessTools.DeclaredMethod(station, "DrawAt", new[] { typeof(Vector3), typeof(bool) });
            MethodInfo? original = utility == null ? null : AccessTools.Method(utility, "Recoil", new[]
            {
                typeof(ThingDef), typeof(Verb), typeof(Vector3).MakeByRefType(),
                typeof(float).MakeByRefType(), typeof(float), typeof(bool)
            });
            if (draw == null || original == null || properties == null)
            {
                Log.Error($"{LogPrefix} Expected DrawAt/Recoil signature or VerbPropertiesCE is missing; patch not applied.");
                return;
            }

            int calls = PatchProcessor.GetOriginalInstructions(draw).Count(instruction => instruction.Calls(original));
            if (calls != 1)
            {
                Log.Error($"{LogPrefix} Expected one CE Recoil call in {draw.DeclaringType}.{draw.Name}, found {calls}; patch not applied.");
                return;
            }

            ceProperties = properties;
            originalRecoil = original;
            recoil = (RecoilDelegate)original.CreateDelegate(typeof(RecoilDelegate));
            new Harmony(HarmonyId).Patch(draw,
                transpiler: new HarmonyMethod(typeof(RecoilFix), nameof(Transpiler)));
            Log.Message($"{LogPrefix} Patched Building_WeaponStationCE.DrawAt (one recoil call).");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Could not apply recoil compatibility patch: {exception}");
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> codes = instructions.ToList();
        if (codes.Count(instruction => instruction.Calls(originalRecoil)) != 1)
        {
            throw new InvalidOperationException($"{LogPrefix} DrawAt must contain exactly one original CE Recoil call.");
        }

        MethodInfo replacement = AccessTools.Method(typeof(RecoilFix), nameof(RecoilForStation));
        foreach (CodeInstruction instruction in codes)
        {
            if (instruction.Calls(originalRecoil)) instruction.operand = replacement;
        }
        return codes;
    }

    public static void RecoilForStation(ThingDef weaponDef, Verb shootVerb, out Vector3 drawOffset,
        out float angleOffset, float aimAngle, bool handheld)
    {
        drawOffset = Vector3.zero;
        angleOffset = 0f;
        // ponytail: 非 CE 射击类型没有 CE 后坐力数据；上游增加兼容后可退役此调用替换。
        if (shootVerb != null && ceProperties!.IsInstanceOfType(shootVerb.verbProps))
        {
            recoil!(weaponDef, shootVerb, out drawOffset, out angleOffset, aimAngle, handheld);
        }
    }
}
