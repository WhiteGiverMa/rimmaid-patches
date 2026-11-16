using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using WhiteGiver.DMSSRUnifiedFix.Audited;
using WhiteGiver.DMSSRUnifiedFix.LegionCompat;
using WhiteGiver.DMSSRUnifiedFix.Neuroglue;
using WhiteGiver.DMSSRUnifiedFix.ScourgeDeath;
using WhiteGiver.DMSSRUnifiedFix.StatPathPerf;
using WhiteGiver.DMSSRUnifiedFix.StasisPodCorpse;

namespace WhiteGiver.DMSSRUnifiedFix;

public sealed class DMSSRUnifiedFixMod : Mod
{
    internal const string HarmonyId = "whitegiver.dmssr.unifiedfix";
    private static bool applied;

    // DMSSR's PatchAll runs in StaticConstructorOnStartup, not in its Mod constructor.
    public DMSSRUnifiedFixMod(ModContentPack content) : base(content) { }

    internal static void ApplyPatches()
    {
        if (applied) return;
        applied = true;
        Harmony harmony = new(HarmonyId);

        bool upstreamReady = false;
        Install("upstream startup", () =>
        {
            MethodInfo ensure = typeof(DMSSR.Patch_PawnCapacity_Calc).Assembly
                .GetType("DMSSR.Misc.HarmonyBootstrap")?.GetMethod("EnsurePatched", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("DMSSR.Misc.HarmonyBootstrap.EnsurePatched");
            ensure.Invoke(null, null);
            upstreamReady = true;
        });

        Install("shared health invalidation", () => HediffCachePatches.Apply(harmony));
        Install("stat/path performance", () =>
        {
            if (!upstreamReady || !HediffCachePatches.Installed)
                throw new InvalidOperationException("Requires upstream startup and shared health invalidation.");
            PathCostPatches.Apply(harmony);
            MechStatPatch.Apply(harmony);
            MechLookupPatches.Apply(harmony);
            CapacityPatches.Apply(harmony);
            RenderPatches.Apply(harmony);
        });
        Install("Legion customization", () => MechCustomizationPatches.Apply(harmony));
        Install("neuroglue", () =>
        {
            if (!HediffCachePatches.Installed)
                throw new InvalidOperationException("Requires shared health invalidation.");
            NeuroglueDefs.EnsureInitialized();
            UseNeurogluePatches.Apply(harmony);
        });
        Install("Scourge death", () => DeathHediffRemoval.Apply(harmony));
        Install("stasis-pod corpse carry", () => CorpseCarryPatches.Apply(harmony));

        Install("modification part defaults", ModificationPartDefaultsFix.Apply);
        Install("PR protocol", () => DeathCheatProtocolFix.Apply(harmony));
        Install("synthetic bionic missing limb", () => SyntheticBionicRestoreFix.Apply(harmony));
        Install("remote control mechanitor", () => RemoteControlFix.Apply(harmony));
        Install("cascade link cleanup", () => CascadeControlFix.Apply(harmony));
        Install("Skylark power charge safety", () => PowerChargeFix.Apply(harmony));
        Install("cephalopoda throw null guard", () => CephalopodaThrowFix.Apply(harmony));
        Install("emergency tend quality", () => EmergencyTendFix.Apply(harmony));
        Install("undying armor stat", () => UndyingArmorFix.Apply(harmony));
        Install("mechanite recognition", () => MechaniteRecognitionFix.Apply(harmony));
        Install("gene fitting pod entry", () => GeneFittingPodFix.Apply(harmony));
        Install("facedancer control gate", () => FacedancerControlFix.Apply(harmony));

        SafeLog.Message("installed: pathCostSingleClamp=" + PathCostPatches.SingleClampInstalled
            + " dirtyCacheHook=" + HediffCachePatches.Installed
            + " legion=" + MechCustomizationPatches.IsSupportedPawnInstalled
            + " neuroglue=" + UseNeurogluePatches.MakeNewToilsPatched
            + " corpseCarry=" + CorpseCarryPatches.Installed
            + " deathCheat=" + DeathCheatProtocolFix.Installed
            + " syntheticRestore=" + SyntheticBionicRestoreFix.Installed
            + " remoteControl=" + RemoteControlFix.Installed
            + " cascade=" + CascadeControlFix.Installed
            + " powerCharge=" + PowerChargeFix.Installed
            + " cephalopoda=" + CephalopodaThrowFix.Installed
            + " emergencyTend=" + EmergencyTendFix.Installed
            + " undyingArmor=" + UndyingArmorFix.Installed
            + " mechanite=" + MechaniteRecognitionFix.Installed
            + " genePod=" + GeneFittingPodFix.Installed
            + " facedancer=" + FacedancerControlFix.Installed);
    }

    private static void Install(string subsystem, Action install)
    {
        try { install(); }
        catch (Exception exception)
        {
            SafeLog.Warning(subsystem + " installation failed; other subsystems continue. " + exception);
        }
    }
}

[StaticConstructorOnStartup]
internal static class Startup
{
    static Startup() => DMSSRUnifiedFixMod.ApplyPatches();
}
