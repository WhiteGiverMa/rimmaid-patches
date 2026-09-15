using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace BugFix_CE_BodyShapeFallback
{
    [StaticConstructorOnStartup]
    public static class BodyShapeFallbackPatch
    {
        private static Type _racePropsExtType;
        private static FieldInfo _bodyShapeField;

        static BodyShapeFallbackPatch()
        {
            // Resolve CE types dynamically to avoid compile-time dependency
            _racePropsExtType = AccessTools.TypeByName("CombatExtended.RacePropertiesExtensionCE");
            if (_racePropsExtType != null)
            {
                _bodyShapeField = AccessTools.Field(_racePropsExtType, "bodyShape");
            }

            var target = AccessTools.Method("CombatExtended.CE_Utility:GetCollisionBodyFactors");
            if (target != null)
            {
                var harmony = new Harmony("com.local.bugfix.ce.bodyshapefallback");
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(BodyShapeFallbackPatch), "Prefix"));
                Log.Message("[BugFix_CE_BodyShapeFallback] Patched CE_Utility.GetCollisionBodyFactors");
            }
            else
            {
                Log.Warning("[BugFix_CE_BodyShapeFallback] Combat Extended not loaded, patch skipped");
            }
        }

        /// <summary>
        /// If the pawn has no bodyShape defined in RacePropertiesExtensionCE,
        /// compute a reasonable fallback and skip the original (which would log an error).
        /// </summary>
        public static bool Prefix(Pawn pawn, ref Vector2 __result)
        {
            if (pawn == null) return true;

            // Check if bodyShape is defined
            if (_racePropsExtType != null && pawn.def != null && pawn.def.modExtensions != null)
            {
                foreach (var ext in pawn.def.modExtensions)
                {
                    if (_racePropsExtType.IsInstanceOfType(ext))
                    {
                        if (_bodyShapeField != null && _bodyShapeField.GetValue(ext) != null)
                        {
                            return true; // bodyShape exists, let CE handle normally
                        }
                        break;
                    }
                }
            }

            // Fallback: compute body factors based on pawn body size
            // These approximate CE's default humanoid body shape values
            float bodySize = pawn.BodySize;
            __result = new Vector2(bodySize * 0.4f, bodySize * 1.25f);
            return false; // skip original
        }
    }
}
