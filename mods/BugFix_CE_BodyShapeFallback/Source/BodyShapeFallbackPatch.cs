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

        private delegate bool TryGetCollisionBodyFactors(Pawn pawn, out Vector2 factors);

        /// <summary>
        /// CE_Utility.GetCollisionBodyFactors asks registered compatibility callbacks
        /// (e.g. VehiclesCompat: VehiclePawn -> (1, def.fillPercent)) before it reads bodyShape.
        /// The same query is used so the prefix never steals a pawn a callback owns.
        /// </summary>
        private static TryGetCollisionBodyFactors _collisionBodyFactorQuery;

        static BodyShapeFallbackPatch()
        {
            // Resolve CE types dynamically to avoid compile-time dependency
            _racePropsExtType = AccessTools.TypeByName("CombatExtended.RacePropertiesExtensionCE");
            if (_racePropsExtType != null)
            {
                _bodyShapeField = AccessTools.Field(_racePropsExtType, "bodyShape");
            }

            MethodInfo callbackQuery = AccessTools.Method("CombatExtended.Compatibility.Patches:GetCollisionBodyFactors");
            if (callbackQuery != null)
            {
                _collisionBodyFactorQuery = BindCollisionBodyFactorQuery(callbackQuery);
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

        private static TryGetCollisionBodyFactors BindCollisionBodyFactorQuery(MethodInfo method)
        {
            try
            {
                return (TryGetCollisionBodyFactors)Delegate.CreateDelegate(typeof(TryGetCollisionBodyFactors), method);
            }
            catch (Exception e)
            {
                Log.Warning("[BugFix_CE_BodyShapeFallback] Fast binding of the CE collision callback query failed, using reflection instead: " + e.Message);
                return delegate (Pawn pawn, out Vector2 factors)
                {
                    object[] args = { pawn, null };
                    bool handled = (bool)method.Invoke(null, args);
                    factors = (Vector2)args[1];
                    return handled;
                };
            }
        }

        /// <summary>
        /// Guards only what CE itself cannot handle: a pawn that is not standing (or is downed)
        /// and has no bodyShape defined, when no registered compatibility callback owns its
        /// factors. CE would fall back to the Invalid shape and log "CE returning BodyType
        /// Undefined". Standing pawns never reach bodyShape in CE, and callback-owned pawns
        /// (VehiclesCompat etc.) short-circuit before it, so both are left untouched.
        /// </summary>
        public static bool Prefix(Pawn pawn, ref Vector2 __result)
        {
            if (pawn == null) return true;

            // CE reads bodyShape only when the pawn is not standing or is downed.
            if (pawn.GetPosture() == PawnPosture.Standing && !pawn.Downed) return true;

            // A defined bodyShape means CE will not hit the undefined-shape branch.
            if (HasBodyShape(pawn)) return true;

            // Compatibility callbacks run before bodyShape in CE and own their result.
            if (_collisionBodyFactorQuery != null)
            {
                Vector2 callbackFactors;
                if (_collisionBodyFactorQuery(pawn, out callbackFactors)) return true;
            }

            // Missing bodyShape where CE would fall back to Invalid: approximate CE's default
            // humanoid collision factors from body size and skip the original error log.
            float bodySize = pawn.BodySize;
            __result = new Vector2(bodySize * 0.4f, bodySize * 1.25f);
            return false;
        }

        private static bool HasBodyShape(Pawn pawn)
        {
            if (_racePropsExtType == null || pawn.def == null || pawn.def.modExtensions == null)
            {
                return false;
            }

            foreach (var ext in pawn.def.modExtensions)
            {
                if (!_racePropsExtType.IsInstanceOfType(ext)) continue;
                return _bodyShapeField != null && _bodyShapeField.GetValue(ext) != null;
            }

            return false;
        }
    }
}
