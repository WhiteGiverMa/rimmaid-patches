using System;
using System.Reflection;
using CombatExtended;
using CombatExtended.AI;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace CE_Fix_IsHunkering_NRE;

/// <summary>
/// Narrow compatibility net for CE builds that do not yet contain the upstream fixes.
/// It is inert when CE already contains them (the current local CE fork and future
/// upstream releases both do) and it never assigns jobs or replaces CE method bodies.
///
/// Fix 1: CompSuppressable.IsHunkering NRE when pawn.CurJob is null.
///   Applied as a Finalizer on the IsHunkering getter itself. With a fixed CE no
///   exception is thrown and the finalizer never runs; with an unfixed CE the
///   NullReferenceException degrades to the same "not hunkering" result the old
///   full-method replacement produced, while all other exceptions propagate untouched.
///   Patching the getter (instead of replacing CompTacticalManager.TryGiveTacticalJobs)
///   protects every call site and cannot shadow future CE logic.
///
/// Fix 2: CompTend.TryGiveTacticalJob assigning TendSelf for a pawn another pawn has
///   reserved (TendPatient), which makes JobDriver_TendPatient.TryMakePreToilReservations
///   fail with LogCouldNotReserveError spam.
///   Applied as a Postfix that only removes an already-built TendSelf result. Because it
///   runs after the original method, the flee-for-cover branch
///   (CompTend.cs: `return SuppressionUtility.GetRunForCoverJob(SelPawn);`) is evaluated
///   first and its non-TendSelf result always passes through untouched. With a fixed CE
///   the internal reservation guard already returns null and this postfix is inert.
///   It never calls StartJob/MakeJob, so it cannot duplicate job assignment.
/// </summary>
[StaticConstructorOnStartup]
public static class Patch_IsHunkering
{
    private const string HarmonyId = "meidocho.ce_fix_ishunkering_nre";
    private const int NreWarningKey = 147001;

    private static readonly FieldInfo _lastTendJobCheckedAtField =
        AccessTools.Field(typeof(CompTend), "lastTendJobCheckedAt");

    static Patch_IsHunkering()
    {
        try
        {
            var harmony = new Harmony(HarmonyId);
            harmony.Patch(
                original: AccessTools.PropertyGetter(typeof(CompSuppressable), nameof(CompSuppressable.IsHunkering)),
                finalizer: new HarmonyMethod(typeof(Patch_IsHunkering), nameof(Finalizer_IsHunkering))
            );
            harmony.Patch(
                original: AccessTools.Method(typeof(CompTend), "TryGiveTacticalJob"),
                postfix: new HarmonyMethod(typeof(Patch_IsHunkering), nameof(Postfix_CompTend_TryGiveTacticalJob))
            );
            Log.Message("[CE_Fix_IsHunkering_NRE] Patches applied (IsHunkering finalizer + CompTend postfix).");
        }
        catch (Exception ex)
        {
            Log.Error($"[CE_Fix_IsHunkering_NRE] Failed to apply patches: {ex}");
        }
    }

    /// <summary>
    /// Safety net for CE builds without the pawn.CurJob?.def fix: converts the
    /// NullReferenceException into false ("not hunkering"). Any other exception is
    /// returned unchanged so Harmony rethrows it.
    /// </summary>
    public static Exception? Finalizer_IsHunkering(Exception? __exception, ref bool __result)
    {
        if (__exception is NullReferenceException)
        {
            __result = false;
            Log.WarningOnce(
                "[CE_Fix_IsHunkering_NRE] CompSuppressable.IsHunkering threw NullReferenceException (CE lacks the CurJob null fix); degraded to false.",
                NreWarningKey);
            return null;
        }
        return __exception;
    }

    /// <summary>
    /// Removes only an already-built TendSelf result when another pawn reserves this
    /// pawn. Flee-cover (non-TendSelf) results pass through untouched, and no job is
    /// ever created or started here.
    /// </summary>
    public static void Postfix_CompTend_TryGiveTacticalJob(CompTend __instance, ref Job? __result)
    {
        if (__result == null || __result.def != CE_JobDefOf.TendSelf)
        {
            return;
        }
        Pawn? pawn = __instance?.SelPawn;
        var reservations = pawn?.Map?.reservationManager?.ReservationsReadOnly;
        if (reservations == null)
        {
            return;
        }
        for (int i = 0; i < reservations.Count; i++)
        {
            if (reservations[i].Target == pawn && reservations[i].Claimant != pawn)
            {
                __result = null;
                _lastTendJobCheckedAtField?.SetValue(__instance, GenTicks.TicksGame);
                return;
            }
        }
    }
}
