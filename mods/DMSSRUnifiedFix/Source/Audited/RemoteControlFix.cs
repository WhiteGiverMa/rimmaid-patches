using System;
using System.Reflection;
using DMSSR.Hediffs;
using HarmonyLib;
using RimWorld;
using Verse;
using WhiteGiver.DMSSRUnifiedFix.StatPathPerf;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// A remote-control implant lets a non-mechlink colonist take the Overseer relation of a mech.
/// Vanilla only builds <see cref="Pawn_MechanitorTracker"/> from <c>MechlinkImplant</c>, so
/// <c>InMechanitorCommandRange</c>, <c>CanDraftMech</c> and the command gizmos dereference
/// <c>overseer.mechanitor == null</c>. Treat an installed remote controller as a mechanitor, build
/// the tracker when the implant is added, and derive just enough bandwidth/control-group capacity
/// for the mechs this pawn actually oversees.
/// </summary>
internal static class RemoteControlFix
{
    private static readonly AccessTools.FieldRef<Pawn_RelationsTracker, Pawn> RelationsPawn =
        AccessTools.FieldRefAccess<Pawn_RelationsTracker, Pawn>("pawn");

    private static HediffDef remoteDef = null!;

    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        remoteDef = DefDatabase<HediffDef>.GetNamedSilentFail("DMS_RemoteController")!;
        if (remoteDef == null)
        {
            return;
        }
        HediffFlagCache.SetRemoteControllerDef(remoteDef);

        MethodInfo shouldBeMechanitor = AccessTools.Method(
            typeof(MechanitorUtility), nameof(MechanitorUtility.ShouldBeMechanitor))
            ?? throw new MissingMethodException("RimWorld.MechanitorUtility.ShouldBeMechanitor");
        MethodInfo addHediff = AccessTools.Method(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.AddHediff),
            new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })
            ?? throw new MissingMethodException("Verse.Pawn_HealthTracker.AddHediff(Hediff,...)");
        MethodInfo removeHediff = AccessTools.Method(
            typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))
            ?? throw new MissingMethodException("Verse.Pawn_HealthTracker.RemoveHediff");
        MethodInfo totalBandwidth = AccessTools.PropertyGetter(
            typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.TotalBandwidth))
            ?? throw new MissingMethodException("Pawn_MechanitorTracker.TotalBandwidth");
        MethodInfo totalGroups = AccessTools.PropertyGetter(
            typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.TotalAvailableControlGroups))
            ?? throw new MissingMethodException("Pawn_MechanitorTracker.TotalAvailableControlGroups");
        MethodInfo relationCreated = AccessTools.Method(
            typeof(Pawn_RelationsTracker),
            nameof(Pawn_RelationsTracker.AddDirectRelation),
            new[] { typeof(PawnRelationDef), typeof(Pawn) })
            ?? throw new MissingMethodException("Pawn_RelationsTracker.AddDirectRelation");

        harmony.Patch(shouldBeMechanitor, postfix: new HarmonyMethod(
            typeof(RemoteControlFix), nameof(ShouldBeMechanitorPostfix)));
        harmony.Patch(addHediff, postfix: new HarmonyMethod(
            typeof(RemoteControlFix), nameof(AddHediffPostfix)));
        harmony.Patch(removeHediff, postfix: new HarmonyMethod(
            typeof(RemoteControlFix), nameof(RemoveHediffPostfix)));
        harmony.Patch(totalBandwidth, postfix: new HarmonyMethod(
            typeof(RemoteControlFix), nameof(TotalBandwidthPostfix)));
        harmony.Patch(totalGroups, postfix: new HarmonyMethod(
            typeof(RemoteControlFix), nameof(TotalGroupsPostfix)));
        harmony.Patch(relationCreated, postfix: new HarmonyMethod(
            typeof(RemoteControlFix), nameof(RelationCreatedPostfix)));
        Installed = true;
    }

    private static void ShouldBeMechanitorPostfix(Pawn pawn, ref bool __result)
    {
        if (__result || !ModsConfig.BiotechActive || !HasRemoteController(pawn))
        {
            return;
        }
        __result = true;
    }

    private static void AddHediffPostfix(Pawn_HealthTracker __instance, Hediff hediff)
    {
        if (hediff?.def != remoteDef)
        {
            return;
        }

        Pawn? pawn = hediff.pawn;
        if (pawn == null || !pawn.health.hediffSet.hediffs.Contains(hediff))
        {
            return;
        }

        try
        {
            PawnComponentsUtility.AddAndRemoveDynamicComponents(pawn);
        }
        catch (Exception exception)
        {
            SafeLog.Warning("remote controller could not create its mechanitor tracker: " + exception.Message);
        }
    }

    private static void RemoveHediffPostfix(Pawn_HealthTracker __instance, Hediff hediff)
    {
        if (hediff?.def != remoteDef)
        {
            return;
        }

        Pawn? pawn = hediff.pawn;
        if (pawn?.health?.hediffSet == null
            || pawn.health.hediffSet.HasHediff(remoteDef)
            || pawn.health.hediffSet.HasHediff(HediffDefOf.MechlinkImplant)
            || pawn.relations == null
            || pawn.mechanitor == null)
        {
            return;
        }

        pawn.mechanitor.Notify_MechlinkRemoved();
    }

    private static void TotalBandwidthPostfix(Pawn_MechanitorTracker __instance, ref int __result)
    {
        Pawn? pawn = __instance.Pawn;
        if (pawn == null || !IsRemoteOnly(pawn) || pawn.relations == null)
        {
            return;
        }

        int needed = __instance.UsedBandwidthFromSubjects;
        if (needed > __result)
        {
            __result = needed;
        }
    }

    private static void TotalGroupsPostfix(Pawn_MechanitorTracker __instance, ref int __result)
    {
        Pawn? pawn = __instance.Pawn;
        if (pawn == null || !IsRemoteOnly(pawn))
        {
            return;
        }
        if (__result < 1)
        {
            __result = 1;
        }
    }

    private static void RelationCreatedPostfix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn)
    {
        // The relation is added after OnRelationCreated returns, so the bandwidth notification
        // performed inside AssignPawnControlGroup cannot see it yet. Run it once more after the
        // direct relation exists; this is redundant (and harmless) for a real mechanitor.
        if (Current.ProgramState != ProgramState.Playing || def != PawnRelationDefOf.Overseer)
        {
            return;
        }
        Pawn? owner = RelationsPawn(__instance);
        if (IsRemoteOnly(owner))
        {
            owner!.mechanitor?.Notify_BandwidthChanged();
        }
        if (IsRemoteOnly(otherPawn))
        {
            otherPawn.mechanitor?.Notify_BandwidthChanged();
        }
    }

    private static bool IsRemoteOnly(Pawn? pawn)
    {
        return pawn != null
            && ModsConfig.BiotechActive
            && HasRemoteController(pawn)
            && pawn.health?.hediffSet?.HasHediff(HediffDefOf.MechlinkImplant) != true;
    }

    private static bool HasRemoteController(Pawn? pawn)
    {
        return pawn != null
            && pawn.Faction != null
            && pawn.Faction.IsPlayerSafe()
            && HediffFlagCache.HasRemoteController(pawn);
    }
}
