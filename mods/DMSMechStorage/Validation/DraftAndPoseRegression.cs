using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fortified;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using WhiteGiver.DMSMechStorage;

internal static class DraftAndPoseRegression
{
    internal static void Run()
    {
        var failures = new List<string>();
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        Driver.Set(typeof(Pawn_DraftController), "draftedInt", mech.drafter, true);
        mech.jobs.curJob = JobMaker.MakeJob(JobDefOf.Wait_Combat, mech.Position);
        Driver.Check(mech.Drafted && mech.IsFighting() && !mech.IsAttacking(), "Fixture does not represent native drafted idle.");
        if (!MechStorage.CanStore(actor, mech).Accepted) failures.Add("Drafted idle was rejected as fighting.");
        mech.jobs.curJob = JobMaker.MakeJob(JobDefOf.AttackMelee, actor);
        Driver.Check(mech.IsAttacking() && !MechStorage.CanStore(actor, mech).Accepted, "Actual melee attack was allowed.");
        mech.jobs.curJob = JobMaker.MakeJob(JobDefOf.Wait_Combat, mech.Position);
        mech.stances.curStance = new Stance_Warmup(10, actor, null);
        Driver.Check(mech.IsAttacking() && !MechStorage.CanStore(actor, mech).Accepted, "Busy ranged-attack stance was allowed.");

        foreach (bool downed in new[] { false, true })
        {
            Driver.ResetMap();
            mech = Driver.NewPawn();
            mech.jobs.posture = PawnPosture.LayingOnGroundNormal;
            if (downed) Driver.Set(typeof(Pawn_HealthTracker), "healthState", mech.health, PawnHealthState.Down);
            var capsule = MechCapsuleUtility.DeactivateMech(mech);
            Driver.Check(capsule.Spawned && capsule.Mech == mech, "Could not create an existing native FFF capsule for pose regression.");
            PawnPosture posture = mech.GetPosture();
            float angle = mech.Drawer.renderer.BodyAngle(PawnRenderFlags.None);
            var location = new Vector3(10.5f, 4f, 10.5f);
            object[] arguments = { location, posture, false };
            var bodyPosition = (Vector3)AccessTools.DeclaredMethod(typeof(PawnRenderer), "GetBodyPos").Invoke(mech.Drawer.renderer, arguments)!;
            if (posture != PawnPosture.Standing || angle != 0f || bodyPosition != location)
                failures.Add($"Stored {(downed ? "downed" : "sleeping")} pose remained {posture}, angle={angle}, y={bodyPosition.y}.");
            Driver.Check(mech.jobs.posture == PawnPosture.LayingOnGroundNormal && mech.Downed == downed,
                "Capsule display correction mutated the retained pawn's posture or health.");
            capsule.innerContainer.Remove(mech);
            Driver.Check(mech.GetPosture().Laying(), "Posture correction escaped the capsule scope.");
        }

        Driver.ResetMap();
        Pawn foreign = Driver.NewPawn("other.mod");
        foreign.jobs.posture = PawnPosture.LayingOnGroundNormal;
        var foreignCapsule = MechCapsuleUtility.DeactivateMech(foreign);
        Driver.Check(foreignCapsule.Spawned && foreign.GetPosture().Laying(), "Foreign FFF capsule display was changed.");

        Driver.ResetMap();
        Pawn spawnActor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn spawnMech = Driver.NewPawn();
        Driver.Check(MechStorage.Store(spawnActor, spawnMech), "Spawned-desync posture setup failed.");
        var spawnCapsule = (Building_MechCapsule)spawnMech.holdingOwner.Owner;
        spawnMech.jobs.posture = PawnPosture.LayingOnGroundNormal;
        Driver.Set(typeof(Thing), "positionInt", spawnMech, spawnCapsule.Position);
        Driver.Register(spawnMech, Driver.Map);
        Driver.Check(spawnMech.Spawned && spawnMech.GetPosture().Laying(),
            "A spawned mech was still forced to standing by the capsule posture rule.");
        Driver.Check(failures.Count == 0, string.Join("\n", failures));
        Console.WriteLine("PASS: drafted idle accepted, actual attacks rejected; real native posture/BodyAngle/GetBodyPos corrected for existing sleeping/downed DMS capsules only, without state mutation.");
    }
}
