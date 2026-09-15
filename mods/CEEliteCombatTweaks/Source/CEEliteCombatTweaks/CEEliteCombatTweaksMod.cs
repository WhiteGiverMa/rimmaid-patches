using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CEEliteCombatTweaks;

public class CEEliteCombatTweaksMod : Mod
{
    public CEEliteCombatTweaksMod(ModContentPack content) : base(content)
    {
        var harmony = new Harmony("WhiteGiverMa.CEEliteCombatTweaks");
        harmony.PatchAll();
        Log.Message("[CE Elite Combat Tweaks] Initialized");
        LongEventHandler.ExecuteWhenFinished(ValidateStatPatches);
    }

    private static void ValidateStatPatches()
    {
        bool aimingDelayCurve = Math.Abs(StatDefOf.AimingDelayFactor.postProcessCurve.Evaluate(0.01f) - 0.01f) < 0.001f;
        bool rangedCooldownPart = StatDefOf.RangedCooldownFactor.parts?.Exists(part => part is StatPart_EliteRangedCooldownOffset) == true;

        if (aimingDelayCurve && rangedCooldownPart)
        {
            Log.Message("[CE Elite Combat Tweaks] Stat XML patches active: AimingDelayFactor min=1%, RangedCooldownFactor elite offset explanation active.");
            return;
        }

        Log.Warning(
            "[CE Elite Combat Tweaks] Stat XML patch validation failed. "
            + $"AimingDelayFactor@0.01={StatDefOf.AimingDelayFactor.postProcessCurve.Evaluate(0.01f):0.###}, "
            + $"RangedCooldownFactor.elitePart={rangedCooldownPart}. "
            + "Load order or CE stat definitions may have changed.");
    }
}
