using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Meidocho.BossgroupConcurrentSummons;

public sealed class BossgroupConcurrentSummonsMod : Mod
{
    internal const string HarmonyId = "meidocho.bossgroupconcurrentsummons";

    public static BossgroupConcurrentSummonsMod Instance { get; private set; }

    internal BossgroupConcurrentSummonsSettings Settings { get; }

    public BossgroupConcurrentSummonsMod(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<BossgroupConcurrentSummonsSettings>();
        new Harmony(HarmonyId).PatchAll(Assembly.GetExecutingAssembly());
        Log.Message("[Bossgroup Concurrent Summons] Harmony patches applied.");
    }

    internal static bool Enabled => Instance?.Settings.AllowConcurrentBossSummons ?? false;

    public override string SettingsCategory() => "Bossgroup Concurrent Summons";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        base.DoSettingsWindowContents(inRect);

        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.CheckboxLabeled(
            "Allow concurrent boss summons",
            ref Settings.AllowConcurrentBossSummons,
            "Allow every bossgroup to be summoned immediately, even while another bossgroup is incoming.");
        listing.End();
    }
}

public sealed class BossgroupConcurrentSummonsSettings : ModSettings
{
    public bool AllowConcurrentBossSummons;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref AllowConcurrentBossSummons, "allowConcurrentBossSummons", false);
    }
}

[HarmonyPatch(typeof(BossgroupWorker), nameof(BossgroupWorker.CanResolve))]
internal static class BossgroupWorkerCanResolvePatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref AcceptanceReport __result)
    {
        if (!BossgroupConcurrentSummonsMod.Enabled)
        {
            return true;
        }

        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(Command_CallBossgroup), "IsDisabled")]
internal static class CommandCallBossgroupIsDisabledPatch
{
    private const int BossgroupCooldownTicks = 120000;

    [HarmonyPostfix]
    private static void Postfix(
        Pawn_MechanitorTracker ___mechanitor,
        ref bool __result,
        ref string __0)
    {
        if (!BossgroupConcurrentSummonsMod.Enabled || !__result)
        {
            return;
        }

        if (Faction.OfMechanoids == null || Faction.OfMechanoids.deactivated)
        {
            return;
        }

        if (___mechanitor?.Pawn?.health?.capacities?.CapableOf(PawnCapacityDefOf.Manipulation) != true)
        {
            return;
        }

        int ticksSinceLastCall = Find.TickManager.TicksGame - Find.BossgroupManager.lastBossgroupCalled;
        bool blockedByCooldown = ticksSinceLastCall < BossgroupCooldownTicks;
        bool blockedByIncomingWave = CallBossgroupUtility.GetPendingBossgroup() != null;
        if (!blockedByCooldown && !blockedByIncomingWave)
        {
            return;
        }

        __result = false;
        __0 = null;
    }
}
