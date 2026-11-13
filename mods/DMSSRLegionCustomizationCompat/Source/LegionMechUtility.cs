using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSSRLegionCustomizationCompat
{
    /// <summary>
    /// The only pawns this compatibility layer opts into DMSSR's mech customization system are
    /// DMS Legion's <c>DMSL_Mech_*</c> mechanoids. The drone <c>DMSL_Drone_CommandingBird</c>
    /// is deliberately excluded: upstream DMSSR also excludes its own <c>DMS_Drone_*</c> drones,
    /// and a drone has no chassis parameters to tune.
    /// </summary>
    internal static class LegionMechUtility
    {
        internal const string LegionMechPrefix = "DMSL_Mech_";
        internal const string CustomizationHediffDefName = "DMS_MechCustomization";

        internal static bool IsLegionMech(Pawn? pawn)
        {
            return pawn?.def?.defName is { } defName && IsLegionMechDefName(defName);
        }

        internal static bool IsLegionMechDefName(string? defName)
        {
            return !string.IsNullOrEmpty(defName)
                && defName!.StartsWith(LegionMechPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Mirrors <c>Patch_MechCustomization_UnlockOnResearch.ApplyToSpawnedMechs</c> for
        /// DMS Legion mechs. This runs only after the exact same research completion event that
        /// upstream uses, so a saved game behaves identically to an upstream DMS_Mech_ save.
        /// Returns the number of hediffs granted (used by the offline validation harness).
        /// </summary>
        internal static int GrantCustomizationToSpawnedLegionMechs()
        {
            HediffDef? hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(CustomizationHediffDefName);
            if (hediffDef == null)
            {
                SafeLog.Warning("DMS_MechCustomization is not loaded; spawned Legion mechs were not upgraded.");
                return 0;
            }

            List<Map>? maps = Find.Maps;
            if (maps == null)
            {
                return 0;
            }

            int granted = 0;
            for (int mapIndex = 0; mapIndex < maps.Count; mapIndex++)
            {
                Map? map = maps[mapIndex];
                IReadOnlyList<Pawn>? pawns = map?.mapPawns?.AllPawnsSpawned;
                if (pawns == null)
                {
                    continue;
                }

                for (int pawnIndex = 0; pawnIndex < pawns.Count; pawnIndex++)
                {
                    Pawn? pawn = pawns[pawnIndex];
                    if (pawn == null || pawn.Dead || pawn.RaceProps?.IsMechanoid != true || !IsLegionMech(pawn))
                    {
                        continue;
                    }

                    HediffSet? hediffSet = pawn.health?.hediffSet;
                    if (hediffSet == null || hediffSet.HasHediff(hediffDef, false))
                    {
                        continue;
                    }

                    pawn.health!.AddHediff(hediffDef);
                    granted++;
                }
            }

            return granted;
        }
    }
}
