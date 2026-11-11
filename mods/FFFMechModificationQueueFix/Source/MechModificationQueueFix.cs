using System;
using System.Collections.Generic;
using System.Reflection;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.FFFMechModificationQueueFix;

public sealed class FFFMechModificationQueueFixMod : Mod
{
    public FFFMechModificationQueueFixMod(ModContentPack content) : base(content)
    {
        MechModificationQueueFix.Apply();
    }
}

public static class MechModificationQueueFix
{
    public const string HarmonyId = "whitegiver.fff.mechmodificationqueuefix";
    private const string LogPrefix = "[FFF Mech Modification Queue Fix]";
    private static bool applied;

    private static Type? dmssrApplyJobType;
    private static Type? dmssrRemoveJobType;
    private static Type? dmssrApplyDriverType;
    private static FieldInfo? dmssrRecipeDefNameField;

    public static void Apply()
    {
        if (applied) return;

        MethodInfo? jobIsSameAs = AccessTools.DeclaredMethod(typeof(Job), nameof(Job.JobIsSameAs), new[] { typeof(Pawn), typeof(Job) });
        MethodInfo? tryResolveAndReserve = AccessTools.DeclaredMethod(typeof(JobDriver_ApplyModification), "TryResolveAndReserveItem", new[] { typeof(bool) });
        MethodInfo? tryIssueEntry = AccessTools.DeclaredMethod(typeof(Window_MechModification), "TryIssueEntry", new[] { typeof(MechModificationQueueEntry), typeof(bool) });

        if (jobIsSameAs == null || tryResolveAndReserve == null || tryIssueEntry == null)
        {
            Log.Warning($"{LogPrefix} Target methods not found on Job / JobDriver_ApplyModification / Window_MechModification; patch skipped.");
            return;
        }

        try
        {
            Harmony harmony = new(HarmonyId);

            harmony.Patch(jobIsSameAs,
                postfix: new HarmonyMethod(typeof(MechModificationQueueFix), nameof(JobIsSameAsPostfix)));

            harmony.Patch(tryResolveAndReserve,
                prefix: new HarmonyMethod(typeof(MechModificationQueueFix), nameof(TryResolveAndReserveItemPrefix)));

            harmony.Patch(tryIssueEntry,
                prefix: new HarmonyMethod(typeof(MechModificationQueueFix), nameof(TryIssueEntryPrefix)));

            TryBindOptionalDmssr(harmony);

            applied = true;
            Log.Message($"{LogPrefix} Active: queued modification jobs preserve distinct job identity and per-item stack reservations.");
        }
        catch (Exception ex)
        {
            Log.Error($"{LogPrefix} Failed to apply patches: {ex}");
        }
    }

    private static void TryBindOptionalDmssr(Harmony harmony)
    {
        dmssrApplyJobType = AccessTools.TypeByName("DMSSR.Jobs.Job_ApplySyntheticBionic");
        dmssrRemoveJobType = AccessTools.TypeByName("DMSSR.Jobs.Job_RemoveSyntheticBionic");
        dmssrApplyDriverType = AccessTools.TypeByName("DMSSR.Jobs.JobDriver_ApplySyntheticBionic");

        if (dmssrApplyJobType != null)
        {
            dmssrRecipeDefNameField = AccessTools.Field(dmssrApplyJobType, "recipeDefName");
        }

        Type? windowCustType = AccessTools.TypeByName("DMSSR.UI.Window_MechCustomization");
        MethodInfo? tryCreateCustomJob = windowCustType != null
            ? AccessTools.DeclaredMethod(windowCustType, "TryCreateCustomJob", new[] { typeof(MechModificationQueueEntry), typeof(Job).MakeByRefType() })
            : null;
        if (tryCreateCustomJob != null)
        {
            harmony.Patch(tryCreateCustomJob,
                postfix: new HarmonyMethod(typeof(MechModificationQueueFix), nameof(TryCreateCustomJobPostfix)));
        }

        MethodInfo? returnToPool = AccessTools.DeclaredMethod(typeof(JobMaker), nameof(JobMaker.ReturnToPool), new[] { typeof(Job) });
        if (returnToPool != null && (dmssrApplyJobType != null || dmssrRemoveJobType != null))
        {
            harmony.Patch(returnToPool,
                prefix: new HarmonyMethod(typeof(MechModificationQueueFix), nameof(ReturnToPoolPrefix)));
        }

        MethodInfo? dmssrReserve = dmssrApplyDriverType != null
            ? AccessTools.DeclaredMethod(dmssrApplyDriverType, nameof(JobDriver.TryMakePreToilReservations), new[] { typeof(bool) })
            : null;
        if (dmssrReserve != null)
        {
            harmony.Patch(dmssrReserve,
                prefix: new HarmonyMethod(typeof(MechModificationQueueFix), nameof(DmssrTryMakePreToilReservationsPrefix)));
        }
    }

    public static bool IsModificationJob(Job? job)
    {
        if (job == null) return false;
        if (job is Job_Modification) return true;
        Type type = job.GetType();
        return (dmssrApplyJobType != null && dmssrApplyJobType.IsAssignableFrom(type))
            || (dmssrRemoveJobType != null && dmssrRemoveJobType.IsAssignableFrom(type));
    }

    public static void JobIsSameAsPostfix(Job __instance, Job? other, ref bool __result)
    {
        if (!__result) return;
        if (IsModificationJob(__instance) || IsModificationJob(other))
        {
            __result = ReferenceEquals(__instance, other);
        }
    }

    public static void TryIssueEntryPrefix(Pawn ___Mech, ref bool queue)
    {
        if (!queue && IsModificationJob(___Mech?.CurJob))
        {
            queue = true;
        }
    }

    public static void TryCreateCustomJobPostfix(Job? job)
    {
        if (job != null && job.loadID < 0 && Current.Game != null && Find.UniqueIDsManager != null)
        {
            job.loadID = Find.UniqueIDsManager.GetNextJobID();
        }
    }

    public static bool ReturnToPoolPrefix(Job? job)
    {
        if (job == null) return true;
        Type type = job.GetType();
        if ((dmssrApplyJobType != null && dmssrApplyJobType.IsAssignableFrom(type))
            || (dmssrRemoveJobType != null && dmssrRemoveJobType.IsAssignableFrom(type)))
        {
            return false;
        }
        return true;
    }

    public static bool TryResolveAndReserveItemPrefix(JobDriver_ApplyModification __instance, bool errorOnFailed, ref bool __result)
    {
        __result = ResolveAndReserveModificationItem(__instance, errorOnFailed);
        return false;
    }

    private static bool ResolveAndReserveModificationItem(JobDriver_ApplyModification driver, bool errorOnFailed)
    {
        Pawn? pawn = driver.pawn;
        Job? job = driver.job;
        Map? map = pawn?.Map;
        Thing? currentItem = job?.GetTarget(TargetIndex.B).Thing;
        ThingDef? expectedDef = GetExpectedItemDef(job, currentItem);
        if (pawn == null || job == null || map == null || expectedDef == null)
        {
            return false;
        }

        if (CanUseCandidate(pawn, job, currentItem, expectedDef, PathEndMode.Touch)
            && ReservationUtility.Reserve(pawn, currentItem!, job, 1, 1, null, errorOnFailed, false))
        {
            job.count = 1;
            return true;
        }

        ReleaseExistingItemReservationIfDifferent(map, pawn, job, currentItem);

        List<Thing> candidates = GetSortedCandidates(map, pawn, job, expectedDef, PathEndMode.Touch);
        for (int i = 0; i < candidates.Count; i++)
        {
            Thing candidate = candidates[i];
            if (ReservationUtility.Reserve(pawn, candidate, job, 1, 1, null, false, false))
            {
                job.SetTarget(TargetIndex.B, candidate);
                job.count = 1;
                return true;
            }
        }

        return false;
    }

    public static bool DmssrTryMakePreToilReservationsPrefix(JobDriver __instance, bool errorOnFailed, ref bool __result)
    {
        __result = ResolveAndReserveDmssrSyntheticItem(__instance, errorOnFailed);
        return false;
    }

    private static bool ResolveAndReserveDmssrSyntheticItem(JobDriver driver, bool errorOnFailed)
    {
        Pawn? pawn = driver.pawn;
        Job? job = driver.job;
        Map? map = pawn?.Map;
        Pawn? targetPawn = job?.GetTarget(TargetIndex.A).Thing as Pawn;
        if (pawn == null || job == null || map == null || targetPawn == null)
        {
            return false;
        }

        if (targetPawn != pawn && !ReservationUtility.Reserve(pawn, targetPawn, job, 1, -1, null, errorOnFailed, false))
        {
            return false;
        }

        Thing? currentItem = job.GetTarget(TargetIndex.B).Thing;
        ThingDef? expectedDef = currentItem?.def ?? ResolveDmssrExpectedDefFromRecipe(job);
        if (expectedDef == null)
        {
            return false;
        }

        if (CanUseCandidate(pawn, job, currentItem, expectedDef, PathEndMode.ClosestTouch)
            && ReservationUtility.Reserve(pawn, currentItem!, job, 1, 1, null, errorOnFailed, false))
        {
            job.count = 1;
            return true;
        }

        ReleaseExistingItemReservationIfDifferent(map, pawn, job, currentItem);

        List<Thing> candidates = GetSortedCandidates(map, pawn, job, expectedDef, PathEndMode.ClosestTouch);
        for (int i = 0; i < candidates.Count; i++)
        {
            Thing candidate = candidates[i];
            if (ReservationUtility.Reserve(pawn, candidate, job, 1, 1, null, false, false))
            {
                job.SetTarget(TargetIndex.B, candidate);
                job.count = 1;
                return true;
            }
        }

        return false;
    }

    private static ThingDef? GetExpectedItemDef(Job? job, Thing? currentItem)
    {
        if (job is Job_Modification modJob && !GenText.NullOrEmpty(modJob.itemDefName))
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail(modJob.itemDefName);
        }
        return currentItem?.def;
    }

    private static ThingDef? ResolveDmssrExpectedDefFromRecipe(Job job)
    {
        if (dmssrRecipeDefNameField == null) return null;
        string? recipeDefName = dmssrRecipeDefNameField.GetValue(job) as string;
        if (GenText.NullOrEmpty(recipeDefName)) return null;
        RecipeDef? recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeDefName);
        if (recipe?.ingredients == null) return null;
        for (int i = 0; i < recipe.ingredients.Count; i++)
        {
            ThingFilter? filter = recipe.ingredients[i]?.filter;
            if (filter == null) continue;
            foreach (ThingDef def in filter.AllowedThingDefs)
            {
                if (def != null && recipe.IsIngredient(def))
                {
                    return def;
                }
            }
        }
        return null;
    }

    public static int AvailableCountForJob(Thing? thing, Pawn? actor, Job? currentJob)
    {
        if (thing == null || thing.Destroyed || !thing.Spawned || thing.stackCount <= 0)
        {
            return 0;
        }
        int available = thing.stackCount;
        List<ReservationManager.Reservation>? reservations = actor?.Map?.reservationManager?.ReservationsReadOnly;
        if (reservations == null)
        {
            return available;
        }
        for (int i = 0; i < reservations.Count && available > 0; i++)
        {
            ReservationManager.Reservation r = reservations[i];
            if (r.Claimant == actor && r.Job != currentJob && r.Target.Thing == thing)
            {
                available -= (r.StackCount < 0) ? thing.stackCount : r.StackCount;
            }
        }
        return available > 0 ? available : 0;
    }

    private static bool CanUseCandidate(Pawn pawn, Job job, Thing? thing, ThingDef expectedDef, PathEndMode peMode)
    {
        if (thing == null || thing.Destroyed || !thing.Spawned || thing.def != expectedDef || thing.stackCount <= 0)
        {
            return false;
        }
        if (AvailableCountForJob(thing, pawn, job) <= 0)
        {
            return false;
        }
        if (ForbidUtility.IsForbidden(thing, pawn))
        {
            return false;
        }
        if (!ReachabilityUtility.CanReach(pawn, thing, peMode, Danger.Deadly))
        {
            return false;
        }
        return ReservationUtility.CanReserve(pawn, thing, 1, 1, null, false);
    }

    private static List<Thing> GetSortedCandidates(Map map, Pawn pawn, Job job, ThingDef expectedDef, PathEndMode peMode)
    {
        List<Thing> result = new();
        List<Thing>? raw = map.listerThings?.ThingsOfDef(expectedDef);
        if (raw == null)
        {
            return result;
        }
        for (int i = 0; i < raw.Count; i++)
        {
            Thing candidate = raw[i];
            if (CanUseCandidate(pawn, job, candidate, expectedDef, peMode))
            {
                result.Add(candidate);
            }
        }
        IntVec3 origin = pawn.Position;
        if (origin.IsValid && result.Count > 1)
        {
            result.Sort((left, right) =>
                (left.Position - origin).LengthHorizontalSquared.CompareTo((right.Position - origin).LengthHorizontalSquared));
        }
        return result;
    }

    private static void ReleaseExistingItemReservationIfDifferent(Map map, Pawn pawn, Job job, Thing? oldItem)
    {
        if (oldItem == null || oldItem.Destroyed || map.reservationManager == null)
        {
            return;
        }
        if (map.reservationManager.ReservedBy(oldItem, pawn, job))
        {
            map.reservationManager.Release(oldItem, pawn, job);
        }
    }
}
