using System.Collections.Generic;
using DMS_Legion;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSL.CommanderPerfFix;

/// <summary>Behaviour-preserving patch bodies. None allocates or mutates save state on warm paths.</summary>
internal static class CommanderPatches
{
    internal static bool PawnHasMarkerPrefix(Pawn? pawn, ref bool __result)
    {
        __result = CommanderMarkerCache.PawnHasMarker(pawn);
        return false;
    }

    internal static bool IsAutonomousPrefix(Pawn? pawn, ref bool __result)
    {
        __result = IsAutonomousFast(pawn);
        return false;
    }

    internal static bool FindOverseerPrefix(Pawn pawn, ref Pawn? __result)
    {
        __result = FindOverseerFast(pawn);
        return false;
    }

    internal static bool HasAssignmentPrefix(Pawn owner, Pawn subject, ref bool __result)
    {
        __result = HasAssignmentFast(owner, subject);
        return false;
    }

    internal static bool GetWorkModePrefix(Pawn pawn, ref MechWorkModeDef __result)
    {
        __result = CommanderMarkerCache.GetMarker(pawn)?.PersonalWorkMode ?? MechWorkModeDefOf.Work;
        return false;
    }

    internal static bool GetRechargeThresholdsPrefix(Pawn pawn, ref FloatRange __result)
    {
        __result = CommanderMarkerCache.GetMarker(pawn)?.PersonalRechargeThresholds
            ?? MechanitorControlGroup.DefaultMechRechargeThresholds;
        return false;
    }

    internal static bool SubjectStatePrefix(CompOverseerSubject __instance, ref OverseerSubjectState __result)
    {
        Pawn pawn = __instance.Parent;
        if (CommanderMarkerCache.GetMarker(pawn) == null || !IsAutonomousFast(pawn))
        {
            return true;
        }

        __result = OverseerSubjectState.Overseen;
        return false;
    }

    internal static bool SubjectOverseerPrefix(CompOverseerSubject __instance, ref Pawn? __result)
    {
        if (!ModsConfig.BiotechActive)
        {
            return true;
        }

        Pawn pawn = __instance.Parent;
        if (CommanderMarkerCache.GetMarker(pawn) == null)
        {
            return true;
        }

        __result = IsAutonomousFast(pawn) ? null : FindOverseerFast(pawn);
        return false;
    }

    internal static bool MechanitorGetOverseerPrefix(Pawn pawn, ref Pawn? __result)
    {
        if (!ModsConfig.BiotechActive || CommanderMarkerCache.GetMarker(pawn) == null)
        {
            return true;
        }

        __result = IsAutonomousFast(pawn) ? null : FindOverseerFast(pawn);
        return false;
    }

    internal static bool IsAutonomousFast(Pawn? pawn)
    {
        DMSL_ModSettings? settings = DMSL_ModSettings.settings;
        if (settings != null
            && settings.enableCommanderAutonomy
            && ModsConfig.BiotechActive
            && pawn != null
            && !pawn.Destroyed
            && !pawn.Dead
            && pawn.IsColonyMech)
        {
            return CommanderMarkerCache.PawnHasMarker(pawn);
        }

        return false;
    }

    internal static Pawn? FindOverseerFast(Pawn pawn)
    {
        if (pawn.relations == null)
        {
            return null;
        }

        List<DirectPawnRelation> relations = pawn.relations.DirectRelations;
        for (int i = 0; i < relations.Count; i++)
        {
            DirectPawnRelation relation = relations[i];
            Pawn? otherPawn = relation.otherPawn;
            if (relation.def == PawnRelationDefOf.Overseer
                && otherPawn != pawn
                && otherPawn != null
                && !otherPawn.Destroyed
                && !otherPawn.Dead
                && HasAssignmentFast(otherPawn, pawn))
            {
                return otherPawn;
            }
        }

        return null;
    }

    internal static bool HasAssignmentFast(Pawn owner, Pawn subject)
    {
        Pawn_MechanitorTracker? mechanitor = owner.mechanitor;
        if (mechanitor == null)
        {
            return false;
        }

        List<MechanitorControlGroup>? controlGroups = mechanitor.controlGroups;
        if (controlGroups == null)
        {
            return false;
        }

        for (int i = 0; i < controlGroups.Count; i++)
        {
            List<AssignedMech> assignedMechs = controlGroups[i].AssignedMechs;
            for (int j = 0; j < assignedMechs.Count; j++)
            {
                if (assignedMechs[j].pawn == subject)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
