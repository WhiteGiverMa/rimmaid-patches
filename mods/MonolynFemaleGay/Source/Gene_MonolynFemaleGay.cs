using RimWorld;
using Verse;

namespace WhiteGiver.MonolynFemaleGay;

public sealed class Gene_MonolynFemaleGay : Gene
{
    private Gender originalGender = Gender.None;

    public override void PostAdd()
    {
        base.PostAdd();
        if (pawn == null) return;
        if (originalGender == Gender.None && pawn.gender != Gender.None && pawn.gender != Gender.Female)
            originalGender = pawn.gender;
        EnsureFemale();
    }

    internal void EnsureFemale()
    {
        if (pawn == null || pawn.gender == Gender.Female) return;
        pawn.gender = Gender.Female;
        if (pawn.style?.beardDef != null && pawn.style.beardDef != BeardDefOf.NoBeard)
            pawn.style.beardDef = BeardDefOf.NoBeard;
        if (pawn.story != null && pawn.story.bodyType == BodyTypeDefOf.Male)
            pawn.story.bodyType = BodyTypeDefOf.Thin;
        pawn.Drawer?.renderer?.SetAllGraphicsDirty();
    }

    public override void PostRemove()
    {
        base.PostRemove();
        if (pawn == null || originalGender == Gender.None || originalGender == pawn.gender) return;
        pawn.gender = originalGender;
        pawn.Drawer?.renderer?.SetAllGraphicsDirty();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref originalGender, "originalGender", Gender.None);
    }
}
