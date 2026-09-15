using System.Collections.Generic;
using CombatExtended;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace CEBreachingFix;

public static class BreachingFix
{
    private static readonly Dictionary<int, BreachingJobStamp> lastBreachingJobs = new Dictionary<int, BreachingJobStamp>();
    private static readonly List<int> staleBreachingJobKeys = new List<int>();
    private static int lastBreachingJobCleanupTick = -1;

    private readonly struct BreachingJobStamp
    {
        public readonly int tick;
        public readonly int targetId;
        public readonly IntVec3 firingPosition;

        public BreachingJobStamp(int tick, int targetId, IntVec3 firingPosition)
        {
            this.tick = tick;
            this.targetId = targetId;
            this.firingPosition = firingPosition;
        }
    }

    public static bool ShouldAllowHitWithoutLoS(Thing target)
    {
        if (target is not Building building)
            return false;

        return building.def.Fillage == FillCategory.Full || building.def.mineable;
    }

    public static bool TryAllowShotWithoutLoS(Verb_LaunchProjectileCE verb, IntVec3 root, LocalTargetInfo targ, out ShootLine line, out Vector3 targetPos)
    {
        line = default;
        targetPos = default;

        if (!targ.HasThing || !ShouldAllowHitWithoutLoS(targ.Thing))
            return false;

        Map map = verb.Caster?.Map;
        if (map == null || !root.InBounds(map) || !targ.Cell.InBounds(map))
            return false;

        float distSq = (root - targ.Cell).LengthHorizontalSquared;
        float maxRange = verb.EffectiveRange;
        float minRange = verb.verbProps.minRange;
        if (distSq > maxRange * maxRange || distSq < minRange * minRange)
            return false;

        targetPos = BreachingTargetPoint(targ.Thing);
        line = new ShootLine(root, targ.Cell);
        return true;
    }

    public static bool ShouldReplaceInvalidBreachingJob(Pawn pawn, Job job)
    {
        return IsBreachingUseVerbJob(pawn, job, null) && !job.targetB.Cell.IsValid;
    }

    public static bool ShouldThrottleBreachingStartJob(Pawn pawn, Job job, ThinkNode jobGiver)
    {
        if (!IsBreachingUseVerbJob(pawn, job, jobGiver))
            return false;

        int pawnId = pawn.thingIDNumber;
        int tick = Find.TickManager?.TicksGame ?? 0;
        CleanupStaleBreachingJobStamps(tick);
        if (!job.targetB.Cell.IsValid)
        {
            lastBreachingJobs[pawnId] = new BreachingJobStamp(tick, TargetId(job), IntVec3.Invalid);
            return true;
        }

        if (lastBreachingJobs.TryGetValue(pawnId, out var last) && last.tick == tick)
            return true;

        lastBreachingJobs[pawnId] = new BreachingJobStamp(tick, TargetId(job), job.targetB.Cell);
        return false;
    }

    public static bool TryStartKnownCastPosition(Pawn pawn, TargetIndex targetInd, TargetIndex castPositionInd)
    {
        Job job = pawn?.jobs?.curJob;
        if (pawn == null || job == null || job.def != JobDefOf.UseVerbOnThing || castPositionInd == TargetIndex.None)
            return false;

        LocalTargetInfo target = job.GetTarget(targetInd);
        if (!ShouldAllowHitWithoutLoS(target.Thing))
            return false;

        if (!TryRefreshBreachingVerb(pawn, job))
            return true;

        IntVec3 dest = job.GetTarget(castPositionInd).Cell;
        if (!dest.IsValid)
        {
            pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            return true;
        }

        Map map = pawn.Map;
        if (map == null || !dest.InBounds(map) || !dest.WalkableBy(map, pawn) || !pawn.CanReach(dest, PathEndMode.OnCell, Danger.Deadly))
        {
            pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
            return true;
        }

        pawn.pather.StartPath(dest, PathEndMode.OnCell);
        if (map.pawnDestinationReservationManager.CanReserve(dest, pawn))
        {
            map.pawnDestinationReservationManager.Reserve(pawn, job, dest);
        }
        return true;
    }

    private static void CleanupStaleBreachingJobStamps(int tick)
    {
        if (tick == lastBreachingJobCleanupTick || tick % 250 != 0)
            return;

        lastBreachingJobCleanupTick = tick;
        staleBreachingJobKeys.Clear();
        foreach (var pair in lastBreachingJobs)
        {
            if (tick - pair.Value.tick > 250)
            {
                staleBreachingJobKeys.Add(pair.Key);
            }
        }

        for (int i = 0; i < staleBreachingJobKeys.Count; i++)
        {
            lastBreachingJobs.Remove(staleBreachingJobKeys[i]);
        }
    }

    private static bool IsBreachingUseVerbJob(Pawn pawn, Job job, ThinkNode jobGiver)
    {
        if (pawn == null || job == null || job.def != JobDefOf.UseVerbOnThing)
            return false;

        if (jobGiver is JobGiver_AIBreaching)
            return true;

        Thing target = job.targetA.Thing ?? pawn.mindState?.breachingTarget?.target;
        return ShouldAllowHitWithoutLoS(target);
    }

    private static int TargetId(Job job)
    {
        return job.targetA.Thing?.thingIDNumber ?? 0;
    }

    private static bool TryRefreshBreachingVerb(Pawn pawn, Job job)
    {
        if (job.verbToUse?.Caster == pawn)
            return true;

        Verb verb = BreachingUtility.FindVerbToUseForBreaching(pawn);
        if (verb?.Caster == pawn)
        {
            job.verbToUse = verb;
            return true;
        }

        pawn.jobs.EndCurrentJob(JobCondition.Incompletable);
        return false;
    }

    private static Vector3 BreachingTargetPoint(Thing target)
    {
        Vector3 drawPos = target.DrawPos;
        float height = Mathf.Min(new CollisionVertical(target).Max, CollisionVertical.WallCollisionHeight);
        return new Vector3(drawPos.x, height, drawPos.z);
    }
}
