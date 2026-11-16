using System;
using DMSSR.Hediffs;
using DMSSR.Patches;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    internal static class MechLookupPatches
    {
        internal static int HelperCount { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            Type[] helpers =
            {
                typeof(Patch_MechCustomization_ApplyCapacity), typeof(Patch_MechCustomization_BodySize),
                typeof(Patch_MechCustomization_MechEnergyMax), typeof(Patch_MechCustomization_MechEnergyThresholdMin),
                typeof(Patch_MechCustomization_MechEnergyThresholdMax),
            };
            foreach (Type type in helpers)
                harmony.Patch(AccessTools.Method(type, "TryGetCompFast"),
                    prefix: new HarmonyMethod(typeof(MechLookupPatches), nameof(TryGetCompPrefix)));
            HelperCount = helpers.Length;
            foreach (Type type in new[] { typeof(Patch_MechCustomization_SkillBonus_GetLevel), typeof(Patch_MechCustomization_SkillBonus_GetLevelForUI) })
                harmony.Patch(AccessTools.Method(type, "ApplyBonus"),
                    prefix: new HarmonyMethod(typeof(MechLookupPatches), nameof(SkillBonusPrefix)));
        }

        private static bool TryGetCompPrefix(Pawn pawn, ref HediffComp_MechCustomization? comp, ref bool __result)
        {
            comp = HediffFlagCache.GetMechCustomization(pawn);
            __result = comp != null;
            return false;
        }

        private static bool SkillBonusPrefix(Pawn pawn, ref int level)
        {
            if (pawn?.RaceProps?.IsMechanoid == true)
            {
                int bonus = HediffFlagCache.GetMechCustomization(pawn)?.protocolSkillBonusLevels ?? 0;
                if (bonus > 0) level = Mathf.Clamp(level + bonus, 0, 20);
            }
            return false;
        }
    }
}
