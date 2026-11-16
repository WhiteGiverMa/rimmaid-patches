using System;
using System.Collections.Generic;
using System.Reflection;
using DMSSR.Hediffs;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace WhiteGiver.DMSSRUnifiedFix.Audited;

/// <summary>
/// PR protocol (<c>DMS_DeathCheat</c>) is authored as a repeatable stock of charges, but the
/// Fortified modification pipeline treated it as an unknown, non-mergeable hediff. This keeps the
/// framework wiring, enforces the 20-charge cap before any item is consumed, and refuses installs
/// while oppressive restructuring would silently swallow the charge.
/// </summary>
internal static class DeathCheatProtocolFix
{
    private const int MaxCharges = HediffComp_DeathCheatCharges.MaxCharges;

    private const string NoRestructuring =
        "压迫性重构生效期间无法安装 PR 协议 / Cannot install the PR protocol during oppressive restructuring.";

    private const string ChargesFull =
        "PR 协议已达到最大 20 层充能 / The PR protocol is already at its 20-charge cap.";

    private static readonly MethodInfo MergedComp =
        AccessTools.Method(typeof(HediffComp_Modification), nameof(HediffComp_Modification.CompPostMerged))!;

    internal static bool Installed { get; private set; }

    internal static void Apply(Harmony harmony)
    {
        EnsureDefWiring();

        MethodInfo canInstall = AccessTools.Method(
            typeof(ModificationInstallValidator), nameof(ModificationInstallValidator.CanInstall))
            ?? throw new MissingMethodException("Fortified.ModificationInstallValidator.CanInstall");
        MethodInfo modificationForPawn = AccessTools.Method(
            typeof(JobDriver_ApplyModification), "ModificationForPawn")
            ?? throw new MissingMethodException("Fortified.JobDriver_ApplyModification.ModificationForPawn");
        MethodInfo addDirect = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.AddDirect))
            ?? throw new MissingMethodException("Verse.HediffSet.AddDirect");
        MethodInfo exposeData = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.ExposeData))
            ?? throw new MissingMethodException("Verse.HediffSet.ExposeData");

        harmony.Patch(canInstall, postfix: new HarmonyMethod(
            typeof(DeathCheatProtocolFix), nameof(CanInstallPostfix)));
        harmony.Patch(modificationForPawn, prefix: new HarmonyMethod(
            typeof(DeathCheatProtocolFix), nameof(ModificationForPawnPrefix)));
        harmony.Patch(addDirect, prefix: new HarmonyMethod(
            typeof(DeathCheatProtocolFix), nameof(AddDirectPrefix)));
        harmony.Patch(exposeData, postfix: new HarmonyMethod(
            typeof(DeathCheatProtocolFix), nameof(ExposeDataPostfix)));

        Installed = true;
    }

    /// <summary>
    /// XML patch first; this is the runtime fallback for load orders where another patch replaced
    /// the comps list. <see cref="HediffWithComps.InitializeComps"/> rebuilds comps from the current
    /// def on load, so save hediffs created before 1.1.0 also receive the modification comp.
    /// </summary>
    internal static void EnsureDefWiring()
    {
        HediffDef? def = DMS_HediffDefOf.DMS_DeathCheat;
        if (def == null)
        {
            return;
        }

        def.comps ??= new List<HediffCompProperties>();
        for (int i = 0; i < def.comps.Count; i++)
        {
            if (def.comps[i] is HediffCompProperties_Modification)
            {
                return;
            }
        }

        def.comps.Add(new HediffCompProperties_Modification
        {
            applyJob = DefDatabase<JobDef>.GetNamedSilentFail("FFF_ModificationRemove"),
        });
    }

    private static void CanInstallPostfix(
        Pawn pawn,
        ThingDef itemDef,
        BodyPartRecord part,
        IReadOnlyList<MechModificationQueueEntry> queue,
        bool checkInventory,
        bool allowEquivalentPart,
        ref bool __result,
        ref string reason)
    {
        ModificationProfile? profile = ModificationProfileDatabase.Get(itemDef);
        if (profile?.hediffDef != DMS_HediffDefOf.DMS_DeathCheat || pawn?.health?.hediffSet == null)
        {
            return;
        }

        if (HasOppressiveRestructuring(pawn))
        {
            __result = false;
            reason = NoRestructuring;
            return;
        }

        int charges = GetCharges(pawn);
        int queued = CountQueuedInstalls(queue, itemDef);
        if (charges + queued >= MaxCharges)
        {
            __result = false;
            reason = ChargesFull;
            return;
        }

        if (__result)
        {
            return;
        }

        // Only clear the framework's unique-stack rejection. A missing part, wrong race, missing
        // inventory or a different blocking reason must keep its own rejection text.
        if (!ModificationUtility.IsValidTargetPart(pawn, profile.properties, part, allowEquivalentPart))
        {
            return;
        }
        if (checkInventory && queued >= ModificationUtility.CountAvailable(pawn.Map, itemDef, pawn))
        {
            return;
        }

        __result = true;
        reason = null!;
    }

    /// <summary>
    /// Handles both the first and every later PR protocol install, so the item is only consumed
    /// after a charge was actually added. The original merge path cannot merge this hediff because
    /// <see cref="HediffComp_DeathCheatCharges"/> is not an <see cref="IModificationMergeParticipant"/>.
    /// </summary>
    private static bool ModificationForPawnPrefix(
        JobDriver_ApplyModification __instance,
        Pawn target,
        Thing item,
        ref string reason,
        ref bool __result)
    {
        if (item == null)
        {
            return true;
        }

        ModificationProfile? profile = ModificationProfileDatabase.Get(item.def);
        ThingDef itemDef = item.def;
        if (profile?.hediffDef != DMS_HediffDefOf.DMS_DeathCheat || target?.health?.hediffSet == null)
        {
            return true;
        }

        if (HasOppressiveRestructuring(target))
        {
            reason = NoRestructuring;
            __result = false;
            return false;
        }

        if (!ModificationInstallValidator.TryFindInstallPart(
                target, itemDef, null, out BodyPartRecord part, out reason, checkInventory: false)
            || !ModificationInstallValidator.CanInstall(
                target, itemDef, part, null, out reason, checkInventory: false))
        {
            __result = false;
            return false;
        }

        Hediff? existing = FindExisting(target);
        if (existing != null)
        {
            HediffComp_DeathCheatCharges? charges = HediffUtility.TryGetComp<HediffComp_DeathCheatCharges>(existing);
            if (charges == null)
            {
                // Not our authored hediff shape; let the framework report its own rejection.
                return true;
            }
            if (charges.charges >= MaxCharges)
            {
                reason = ChargesFull;
                __result = false;
                return false;
            }

            charges.AddCharges(charges.Props?.addCharges ?? 1);
            MergeModificationComp(existing, profile, item, target);
            FinishInstall(target, item);
            __result = true;
            return false;
        }

        Hediff created = HediffMaker.MakeHediff(profile.hediffDef, target, part);
        HediffUtility.TryGetComp<HediffComp_Modification>(created)?.SetSource(item.def);
        HediffComp_DeathCheatCharges? createdCharges = HediffUtility.TryGetComp<HediffComp_DeathCheatCharges>(created);
        if (createdCharges == null)
        {
            return true;
        }
        createdCharges.AddCharges(createdCharges.Props?.addCharges ?? 1);

        target.health.AddHediff(created, null, null, null);
        if (!target.health.hediffSet.hediffs.Contains(created))
        {
            reason = "无法安装到该部位 / Could not attach the modification to that body part.";
            __result = false;
            return false;
        }

        FinishInstall(target, item);
        __result = true;
        return false;
    }

    private static void MergeModificationComp(Hediff existing, ModificationProfile profile, Thing item, Pawn target)
    {
        HediffComp_Modification? comp = HediffUtility.TryGetComp<HediffComp_Modification>(existing);
        if (comp == null)
        {
            return;
        }

        Hediff incoming = HediffMaker.MakeHediff(profile.hediffDef, target, existing.Part);
        HediffUtility.TryGetComp<HediffComp_Modification>(incoming)?.SetSource(item.def);
        MergedComp.Invoke(comp, new object[] { incoming });
    }

    private static void FinishInstall(Pawn target, Thing item)
    {
        CompTargetable_AddHediffOnTarget? targetable = ThingCompUtility.TryGetComp<CompTargetable_AddHediffOnTarget>(item);
        if (targetable?.Props?.soundDef != null)
        {
            SoundStarter.PlayOneShot(targetable.Props.soundDef, SoundInfo.InMap(new TargetInfo(target)));
        }
        Messages.Message(
            "已安装 PR 协议 / Installed the PR protocol on " + target.LabelShort + ".",
            new LookTargets(target),
            MessageTypeDefOf.PositiveEvent,
            true);
    }

    private static Hediff? FindExisting(Pawn target)
    {
        List<Hediff> hediffs = target.health.hediffSet.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            if (hediffs[i]?.def == DMS_HediffDefOf.DMS_DeathCheat)
            {
                return hediffs[i];
            }
        }
        return null;
    }

    private static bool HasOppressiveRestructuring(Pawn pawn)
    {
        return DMS_HediffDefOf.DMS_OppressiveRestructuring != null
            && pawn.health.hediffSet.HasHediff(DMS_HediffDefOf.DMS_OppressiveRestructuring);
    }

    private static int GetCharges(Pawn pawn)
    {
        Hediff? hediff = FindExisting(pawn);
        HediffComp_DeathCheatCharges? comp = hediff == null
            ? null
            : HediffUtility.TryGetComp<HediffComp_DeathCheatCharges>(hediff);
        return comp?.charges ?? 0;
    }

    private static int CountQueuedInstalls(IReadOnlyList<MechModificationQueueEntry>? queue, ThingDef itemDef)
    {
        if (queue == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < queue.Count; i++)
        {
            MechModificationQueueEntry entry = queue[i];
            if (entry != null
                && entry.kind == MechModificationOperationKind.Install
                && entry.itemDef == itemDef)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// The initial Scourge/shell charge is added with a whole-body <c>Part == null</c> hediff, while
    /// the modification job installs on the core part. Normalize on the low-level add and after a
    /// save load so uninstall and the modification window address the same hediff.
    /// </summary>
    private static void AddDirectPrefix(HediffSet __instance, Hediff hediff)
    {
        if (hediff?.def != DMS_HediffDefOf.DMS_DeathCheat || hediff.Part != null)
        {
            return;
        }

        BodyPartRecord? core = __instance.pawn?.RaceProps?.body?.corePart;
        if (core != null)
        {
            hediff.Part = core;
        }
    }

    private static void ExposeDataPostfix(HediffSet __instance)
    {
        if (Scribe.mode != LoadSaveMode.ResolvingCrossRefs)
        {
            return;
        }

        BodyPartRecord? core = __instance.pawn?.RaceProps?.body?.corePart;
        if (core == null)
        {
            return;
        }

        bool changed = false;
        List<Hediff> hediffs = __instance.hediffs;
        for (int i = 0; i < hediffs.Count; i++)
        {
            Hediff hediff = hediffs[i];
            if (hediff?.def == DMS_HediffDefOf.DMS_DeathCheat && hediff.Part == null)
            {
                hediff.Part = core;
                changed = true;
            }
        }

        if (changed)
        {
            __instance.DirtyCache();
        }
    }
}
