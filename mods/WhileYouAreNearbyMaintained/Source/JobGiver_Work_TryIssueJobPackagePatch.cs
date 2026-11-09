using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace MjRimMods.WhileYouAreNearby;

[HarmonyPatch(typeof(JobGiver_Work), nameof(JobGiver_Work.TryIssueJobPackage))]
internal static class JobGiver_Work_TryIssueJobPackagePatch
{
    private static readonly Func<JobGiver_Work, Pawn, WorkGiver, bool> PawnCanUseWorkGiver =
        AccessTools.Method(typeof(JobGiver_Work), "PawnCanUseWorkGiver", new[] { typeof(Pawn), typeof(WorkGiver) })
            .CreateDelegate<Func<JobGiver_Work, Pawn, WorkGiver, bool>>();

    [HarmonyPostfix]
    private static void After_TryIssueJobPackage(ref ThinkResult __result, ref JobGiver_Work __instance, Pawn pawn, JobIssueParams jobParams)
    {
        if (!ShouldTrigger(__instance, __result, pawn)) return;

        SimpleSettings settings = Utils.settings;
        Job oldJob = __result.Job;
        bool fromQueue = __result.FromQueue;
        int distanceSquared = FindDistanceSquared(pawn, oldJob);
        List<WorkGiver> workGivers = pawn.workSettings.WorkGiversInOrderNormal;
        int start = workGivers.FindIndex(giver => giver.def == oldJob.workGiverDef);
        if (start < 0 || workGivers[start] is not WorkGiver_Scanner) return;

        int priority = pawn.workSettings.GetPriority(oldJob.workGiverDef.workType);
        int radius = Math.Min(settings.maxSearchRadius, (int)Math.Sqrt(distanceSquared));
        int bestDistanceSquared = radius * radius;
        WorkGiver_Scanner? selectedScanner = null;
        Thing? selectedThing = null;
        IntVec3? selectedCell = null;
        for (int i = start; i < workGivers.Count; i++)
        {
            if (!PawnCanUseWorkGiver(__instance, pawn, workGivers[i])) continue;
            if (workGivers[i] is not WorkGiver_Scanner scanner) continue;
            if (pawn.workSettings.GetPriority(scanner.def.workType) > priority + settings.priorityBoost) break;
            if (settings.excludedNearbyWorkTypes.Contains(scanner.def.workType)) continue;

            bool allowUnreachable = scanner.AllowUnreachable;
            Danger maxDanger = scanner.MaxPathDanger(pawn);
            try
            {
                // ponytail: retain the original nearest-first scanner policy; priority-aware selection is a separate behavior change.
                if (scanner.def.scanThings)
                {
                    IEnumerable<Thing> candidates = scanner.PotentialWorkThingsGlobal(pawn);
                    selectedThing = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, scanner.PotentialWorkThingRequest,
                        scanner.PathEndMode, TraverseParms.For(pawn, maxDanger), radius,
                        thing => !thing.IsForbidden(pawn) && scanner.HasJobOnThing(pawn, thing), candidates,
                        0, scanner.MaxRegionsToScanBeforeGlobalSearch, candidates != null);
                    if (selectedThing != null)
                    {
                        selectedScanner = scanner;
                        break;
                    }
                }
                else if (scanner.def.scanCells)
                {
                    int improvements = 0;
                    // Keep the original single-pass enumerator: growers maintain per-zone state while yielding cells.
                    foreach (IntVec3 cell in scanner.PotentialWorkCellsGlobal(pawn))
                    {
                        int cellDistanceSquared = cell.DistanceToSquared(pawn.Position);
                        if (cellDistanceSquared >= bestDistanceSquared) continue;
                        if (cell.IsForbidden(pawn) || !scanner.HasJobOnCell(pawn, cell)) continue;
                        if (!allowUnreachable && !pawn.CanReach(cell, scanner.PathEndMode, maxDanger)) continue;
                        selectedCell = cell;
                        bestDistanceSquared = cellDistanceSquared;
                        selectedScanner = scanner;
                        improvements++;
                    }
                    if (selectedCell.HasValue)
                    {
                        bestDistanceSquared -= (1 + improvements) * radius;
                        if (bestDistanceSquared <= 0) break;
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Error($"[While You Are Nearby Maintained] {pawn} threw exception in WorkGiver {scanner.def.defName}: {exception}");
            }
        }

        if (selectedScanner == null) return;
        Job? nearbyJob = selectedThing != null
            ? selectedScanner.JobOnThing(pawn, selectedThing)
            : selectedCell.HasValue ? selectedScanner.JobOnCell(pawn, selectedCell.Value) : null;
        if (nearbyJob == null) return;

        nearbyJob.workGiverDef = selectedScanner.def;
        __result = new ThinkResult(nearbyJob, __instance, selectedScanner.def.tagToGive);
        // Match vanilla ownership: never clear a current, driver-owned, queued, or reused Job.
        if (!fromQueue && !ReferenceEquals(oldJob, nearbyJob)
            && !ReferenceEquals(pawn.jobs.curJob, oldJob)
            && !ReferenceEquals(pawn.jobs.curDriver?.job, oldJob)
            && !pawn.jobs.jobQueue.Contains(oldJob))
        {
            JobMaker.ReturnToPool(oldJob);
        }
        Utils.Debug("Nearby job found.");
    }

    private static bool ShouldTrigger(JobGiver_Work giver, ThinkResult result, Pawn pawn)
    {
        if (result == ThinkResult.NoJob || giver.emergency) return false;
        if ((!pawn.IsColonist && !pawn.IsColonyMechPlayerControlled)
            || pawn.mindState.priorityWork.IsPrioritized || pawn.InMentalState) return false;

        SimpleSettings settings = Utils.settings;
        if (Rand.Chance(settings.vanillaChancePercentage / 100f)) return false;
        Job job = result.Job;
        if (job.workGiverDef == null || settings.excludedLongDistanceWorkTypes.Contains(job.workGiverDef.workType)) return false;
        return FindDistanceSquared(pawn, job) >= settings.longDistanceThreshold * settings.longDistanceThreshold;
    }

    private static int FindDistanceSquared(Pawn pawn, Job job)
    {
        IntVec3 position = pawn.Position;
        return Math.Max(position.DistanceToSquared(job.targetA.IsValid ? job.targetA.Cell : position),
            Math.Max(position.DistanceToSquared(job.targetB.IsValid ? job.targetB.Cell : position),
                position.DistanceToSquared(job.targetC.IsValid ? job.targetC.Cell : position)));
    }
}
