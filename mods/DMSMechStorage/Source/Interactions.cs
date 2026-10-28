using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSMechStorage;

public sealed class FloatMenuOptionProvider_StoreDmsMech : FloatMenuOptionProvider
{
    protected override bool Drafted => true;
    protected override bool Undrafted => true;
    protected override bool Multiselect => false;
    protected override bool RequiresManipulation => true;

    public override bool SelectedPawnValid(Pawn pawn, FloatMenuContext context) =>
        base.SelectedPawnValid(pawn, context) && MechStorage.CanAct(pawn);

    protected override bool AppliesInt(FloatMenuContext context) => MechStorage.CanAct(context.FirstSelectedPawn);

    protected override FloatMenuOption? GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
    {
        if (!MechStorage.IsDmsMech(clickedPawn) || clickedPawn.Faction != Faction.OfPlayer) return null;
        Pawn actor = context.FirstSelectedPawn;
        AcceptanceReport report = MechStorage.CanStore(actor, clickedPawn);
        string label = "DMSMS_Store".Translate(clickedPawn.LabelShortCap);
        if (!report.Accepted) return new FloatMenuOption(label + ": " + report.Reason, null);
        return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, () =>
        {
            AcceptanceReport current = MechStorage.CanStore(actor, clickedPawn);
            if (!current.Accepted)
            {
                MechStorage.Reject(current.Reason, actor);
                return;
            }
            actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("DMSMS_StoreMech"), clickedPawn), JobTag.Misc);
        }) { revalidateClickTarget = clickedPawn }, actor, clickedPawn);
    }
}

public sealed class JobDriver_StoreMech : JobDriver
{
    private Pawn Mech => (Pawn)job.targetA.Thing;

    public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(Mech, job, 1, -1, null, errorOnFailed);

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedNullOrForbidden(TargetIndex.A);
        this.FailOn(() => !MechStorage.TargetValid(pawn, Mech) || Mech.IsAttacking());
        yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        yield return Toils_General.WaitWith(TargetIndex.A, 300, useProgressBar: true, maintainPosture: true, maintainSleep: true, face: TargetIndex.A);
        yield return Toils_General.Do(() => MechStorage.Store(pawn, Mech));
    }
}
