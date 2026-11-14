using HarmonyLib;
using RimWorld;
using Unburned;
using Verse;
using Verse.AI;

namespace UnburnedDNAFPerfFix;

public static class WaitOptimization
{
    internal static void Install(Harmony h) => h.Patch(
        AccessTools.Method(typeof(Pretty_Patch), nameof(Pretty_Patch.Prefix)),
        prefix: new HarmonyMethod(typeof(WaitOptimization), nameof(Prefix)));

    public static bool Prefix(JobDriver_Wait __0, ref bool __result)
    {
        __result = false;
        Check(__0);
        return false;
    }

    // Same evaluation order and tie-breaking as Unburned. Only the two *local*
    // search flags are narrowed. Ranged targeting always retains the original flag.
    public static void Check(JobDriver_Wait driver)
    {
        Pawn pawn = driver.pawn;
        if (!pawn.kindDef.canMeleeAttack || pawn.Downed || pawn.stances.FullBodyBusy || pawn.IsCarryingPawn()
            || (!pawn.IsPlayerControlled && pawn.IsPsychologicallyInvisible()) || pawn.IsShambler) return;
        driver.collideWithPawns = false;
        bool violent = !pawn.WorkTagIsDisabled(WorkTags.Violent);
        bool firefighting = pawn.RaceProps.ToolUser && pawn.Faction == Faction.OfPlayer
            && !pawn.WorkTagIsDisabled(WorkTags.Firefighting);
        if (!(violent | firefighting)) return;
        Map map = pawn.Map;
        MapSnapshot snapshot = MapSnapshot.Get(map);
        bool melee = violent && snapshot.MayMelee(pawn);
        bool fireCheck = firefighting && snapshot.FireNear(pawn.Position);
        Fire? fire = null;
        if (melee | fireCheck)
        {
            for (int i = 0; i < 9; i++)
            {
                IntVec3 c = pawn.Position + GenAdj.AdjacentCellsAndInside[i];
                if (!c.InBounds(map)) continue;
                List<Thing> things = c.GetThingList(map);
                for (int j = 0; j < things.Count; j++)
                {
                    if (melee && pawn.kindDef.canMeleeAttack && things[j] is Pawn other
                        && !other.ThreatDisabled(pawn) && pawn.HostileTo(other))
                    {
                        CompActivity comp = other.GetComp<CompActivity>();
                        if ((comp == null || comp.IsActive) && !pawn.ThreatDisabledBecauseNonAggressiveRoamer(other)
                            && GenHostility.IsActiveThreatTo(other, pawn.Faction, false, false))
                        {
                            pawn.meleeVerbs.TryMeleeAttack(other, null, false);
                            driver.collideWithPawns = true;
                            return;
                        }
                    }
                    if (fireCheck && things[j] is Fire candidate
                        && (fire == null || candidate.fireSize < fire.fireSize || i == 8)
                        && (candidate.parent == null || candidate.parent != pawn)) fire = candidate;
                }
            }
        }
        if (fire != null && Pretty_Patch.PawnShouldBeatFire(pawn)) pawn.natives.TryBeatFire(fire);
        else
        {
            if (!violent || !driver.job.canUseRangedWeapon || driver.job.def != JobDefOf.Wait_Combat
                || (pawn.drafter != null && !pawn.drafter.FireAtWill)) return;
            Verb verb = pawn.CurrentEffectiveVerb;
            if (verb != null && !verb.verbProps.IsMeleeAttack)
            {
                TargetScanFlags flags = TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable;
                if (verb.IsIncendiary_Ranged()) flags |= TargetScanFlags.NeedNonBurning;
                Thing target = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(pawn, flags, null, 0f, 9999f);
                if (target != null) { pawn.TryStartAttack(target); driver.collideWithPawns = true; }
            }
        }
    }
}
