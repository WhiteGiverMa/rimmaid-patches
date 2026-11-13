using System;
using System.Reflection;
using DMSSR.Verbs;
using HarmonyLib;
using Verse;

namespace WhiteGiver.DMSSRSolitonChargeDecay;

public sealed class DMSSRSolitonChargeDecayMod : Mod
{
    public DMSSRSolitonChargeDecayMod(ModContentPack content) : base(content)
    {
        SolitonChargeDecay.Apply();
    }
}

public static class SolitonChargeDecay
{
    public const string HarmonyId = "whitegiver.dmssr.solitonchargedecay";
    private const string LogPrefix = "[DMSSR Soliton Charge Decay]";
    private static bool applied;
    private static AccessTools.FieldRef<Verb_MechParticleBeam, int> burstStart = null!;
    private static Func<Verb_MechParticleBeam, int, float> computeCarry = null!;

    public static void Apply()
    {
        if (applied) return;
        Harmony harmony = new(HarmonyId);
        try
        {
            MethodInfo switchEnabled = Required("IsBeamSwitchFireEnabled", typeof(bool));
            MethodInfo store = Required("StoreWarmupOnStop", typeof(void));
            MethodInfo charge = Required("GetChargePercent", typeof(float));
            MethodInfo compute = Required("ComputeCarryTicks", typeof(float), typeof(int));
            MethodInfo next = AccessTools.DeclaredMethod(typeof(Verb), "TryCastNextBurstShot", Type.EmptyTypes)
                ?? throw new MissingMethodException(typeof(Verb).FullName, "TryCastNextBurstShot()");
            burstStart = AccessTools.FieldRefAccess<Verb_MechParticleBeam, int>("burstStartTick");
            computeCarry = AccessTools.MethodDelegate<Func<Verb_MechParticleBeam, int, float>>(compute);

            harmony.Patch(switchEnabled, postfix: Hook(nameof(SwitchEnabledPostfix)));
            harmony.Patch(store, prefix: Hook(nameof(StoreWarmupPrefix)));
            harmony.Patch(charge, postfix: Hook(nameof(ChargePercentPostfix)));
            harmony.Patch(next, postfix: Hook(nameof(NextBurstShotPostfix)));
            applied = true;
            Log.Message($"{LogPrefix} Active: native coldTime decay, idle activity display and interrupted-burst capture.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(HarmonyId);
            Log.Error($"{LogPrefix} Registration failed: {exception}");
        }
    }

    private static MethodInfo Required(string name, Type returnType, params Type[] parameters)
    {
        MethodInfo? method = AccessTools.DeclaredMethod(typeof(Verb_MechParticleBeam), name, parameters);
        if (method == null || method.ReturnType != returnType)
            throw new MissingMethodException(typeof(Verb_MechParticleBeam).FullName, name);
        return method;
    }

    private static HarmonyMethod Hook(string name) => new(typeof(SolitonChargeDecay), name);

    public static void SwitchEnabledPostfix(ref bool __result) => __result = true;

    public static bool StoreWarmupPrefix(Verb_MechParticleBeam __instance, int ___burstStartTick,
        ref float ___storedWarmupTicks, ref float ___warmupCarryTicks, ref int ___lastStopTick)
    {
        if (___burstStartTick >= 0)
        {
            int now = Find.TickManager.TicksGame;
            float configured = (__instance.verbProps as VerbProperties_MechParticleBeam)?.warmupExponentParam ?? 600f;
            if (configured <= 0f) configured = 600f;
            // Verb.TryCastNextBurstShot can set Idle before Reset captures the last firing interval.
            ___storedWarmupTicks = Math.Min(configured, Math.Max(0, now - ___burstStartTick) + ___warmupCarryTicks);
            ___lastStopTick = now;
            ___warmupCarryTicks = 0f;
        }
        return false;
    }

    public static void ChargePercentPostfix(Verb_MechParticleBeam __instance, ref float __result)
    {
        if (__instance.state == VerbState.Bursting) return;
        float configured = (__instance.verbProps as VerbProperties_MechParticleBeam)?.warmupExponentParam ?? 600f;
        if (configured <= 0f) configured = 600f;
        __result = Math.Clamp(computeCarry(__instance, Find.TickManager.TicksGame) / configured, 0f, 1f);
    }

    public static void NextBurstShotPostfix(Verb __instance)
    {
        if (__instance is Verb_MechParticleBeam beam && beam.state != VerbState.Bursting && burstStart(beam) >= 0)
        {
            // Preserve native Reset cleanup and safety cooldown's subsequent explicit zeroing.
            Action? callback = beam.castCompleteCallback;
            beam.Reset();
            beam.castCompleteCallback = callback;
        }
    }
}
