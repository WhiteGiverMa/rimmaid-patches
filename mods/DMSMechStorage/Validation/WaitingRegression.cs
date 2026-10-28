using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using WhiteGiver.DMSMechStorage;

internal static class WaitingRegression
{
    private static Job? forcedJob;
    private static bool reserved;

    internal static void Run(Harmony qa)
    {
        Driver.ResetMap();
        var forceWait = AccessTools.DeclaredMethod(typeof(PawnUtility), nameof(PawnUtility.ForceWait));
        var startJob = AccessTools.DeclaredMethod(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob));
        var canBeAwake = AccessTools.PropertyGetter(typeof(PawnCapacitiesHandler), nameof(PawnCapacitiesHandler.CanBeAwake));
        var boundary = new Harmony(qa.Id + ".waiting");
        qa.Unpatch(forceWait, HarmonyPatchType.Prefix, qa.Id);
        boundary.Patch(startJob, prefix: new HarmonyMethod(typeof(WaitingRegression), nameof(StartJob)));
        boundary.Patch(canBeAwake, prefix: new HarmonyMethod(typeof(Driver), nameof(Driver.True)));
        try
        {
            var shutdown = new JobDef { defName = "SelfShutdown", driverClass = typeof(JobDriver_SelfShutdown) };
            Driver.Add(shutdown);
            Driver.Set(typeof(JobDefOf), nameof(JobDefOf.SelfShutdown), null, shutdown);
            Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
            Pawn mech = Driver.NewPawn();
            mech.needs = new Pawn_NeedsTracker(mech);
            var energy = (Need_MechEnergy)RuntimeHelpers.GetUninitializedObject(typeof(Need_MechEnergy));
            Driver.Set(typeof(Need), "pawn", energy, mech);
            mech.needs.energy = energy;
            mech.jobs.posture = PawnPosture.LayingOnGroundNormal;
            mech.jobs.curJob = JobMaker.MakeJob(shutdown, mech.Position);
            var sleeper = new JobDriver_SelfShutdown { pawn = mech, job = mech.jobs.curJob, asleep = true };
            Driver.Set(typeof(Pawn_JobTracker), "curDriver", mech.jobs, sleeper);
            var driver = new JobDriver_StoreMech { pawn = actor, job = JobMaker.MakeJob(Driver.StoreJob, mech) };
            actor.jobs.curJob = driver.job;
            Driver.Check(driver.TryMakePreToilReservations(true), "Waiting regression could not reserve the target mech.");

            Toil original = Toils_General.WaitWith(TargetIndex.A, 300, maintainPosture: true);
            original.actor = actor;
            original.initAction();
            Driver.Check(forcedJob?.def == shutdown && !forcedJob.targetA.IsValid && !reserved,
                "Native WaitWith did not reproduce SelfShutdown's missing reservation target.");
            Console.WriteLine("RED reproduced: actual ForceWait rebuilt sleeping SelfShutdown without a rest cell; actual shutdown driver rejected its reservation.");

            var toils = ((IEnumerable<Toil>)AccessTools.DeclaredMethod(typeof(JobDriver_StoreMech), "MakeNewToils").Invoke(driver, null)!).ToArray();
            forcedJob = null;
            toils[1].actor = actor;
            toils[1].initAction();
            Driver.Check(forcedJob?.def == shutdown && forcedJob.targetA.Cell == mech.Position && reserved,
                "Storage's wait rebuilt SelfShutdown without a reservable rest cell.");
            Driver.Check(mech.jobs.curJob == sleeper.job && mech.Spawned, "Waiting regression altered the original pawn/job outside its scheduler boundary.");
            Console.WriteLine("PASS: actual sidecar WaitWith and native ForceWait preserve sleeping rest-cell targeting; real SelfShutdown reservation succeeds.");
        }
        finally
        {
            boundary.Unpatch(startJob, HarmonyPatchType.Prefix, boundary.Id);
            boundary.Unpatch(canBeAwake, HarmonyPatchType.Prefix, boundary.Id);
            qa.Patch(forceWait, prefix: new HarmonyMethod(typeof(Driver), nameof(Driver.ForceWait)));
        }
    }

    public static bool StartJob(Pawn ___pawn, Job newJob)
    {
        forcedJob = newJob;
        var driver = (JobDriver)Activator.CreateInstance(newJob.def.driverClass)!;
        driver.pawn = ___pawn;
        driver.job = newJob;
        reserved = driver.TryMakePreToilReservations(false);
        return false;
    }
}
