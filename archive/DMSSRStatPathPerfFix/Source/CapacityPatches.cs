using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DMSSR;
using DMSSR.Patches;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    internal static class CapacityPatches
    {
        private static readonly Func<HediffWithComps, bool> ProviderDefCheck =
            (Func<HediffWithComps, bool>)AccessTools.Method(typeof(Patch_PawnCapacity_Calc), "HediffDefHasProvider")
                .CreateDelegate(typeof(Func<HediffWithComps, bool>));
        private static readonly AccessTools.FieldRef<int> Depth =
            AccessTools.StaticFieldRefAccess<int>(AccessTools.Field(typeof(Patch_PawnCapacity_Calc), "_capCallDepth"));

        internal static bool DefHasProvider(HediffWithComps hediff) => ProviderDefCheck(hediff);
        internal static bool Installed { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Patch_MechCustomization_ApplyCapacity), "Postfix"),
                prefix: new HarmonyMethod(typeof(CapacityPatches), nameof(CustomizationGate)));
            harmony.Patch(AccessTools.Method(typeof(Patch_Schizophrenia_ConsciousnessFloor), "Postfix"),
                prefix: new HarmonyMethod(typeof(CapacityPatches), nameof(SchizophreniaGate)),
                transpiler: new HarmonyMethod(typeof(CapacityPatches), nameof(LinkedLookupTranspiler)));
            // Retain DMSSR's real prefix and thread-static field. Its postfix is still registered on
            // CalculateCapacityLevel, in the same order relative to other mods' postfixes.
            harmony.Patch(AccessTools.Method(typeof(Patch_PawnCapacity_Calc), "Postfix"),
                prefix: new HarmonyMethod(typeof(CapacityPatches), nameof(ProviderPostfixPrefix)));
            Installed = true;
        }

        private static bool CustomizationGate(HediffSet __0, PawnCapacityDef __1)
        {
            if (__1 == null || (__1 != PawnCapacityDefOf.Consciousness
                && __1 != PawnCapacityDefOf.Manipulation && __1 != PawnCapacityDefOf.Moving)) return false;
            return __0?.pawn?.RaceProps?.IsMechanoid == true;
        }

        private static bool SchizophreniaGate(PawnCapacityDef __1) => __1 != null
            && (__1 == PawnCapacityDefOf.Consciousness || __1 == PawnCapacityDefOf.Sight || __1 == PawnCapacityDefOf.Hearing);

        private static IEnumerable<CodeInstruction> LinkedLookupTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.GetFirstHediffOfDef));
            MethodInfo cached = AccessTools.Method(typeof(CapacityPatches), nameof(CachedLinkedLookup));
            int count = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = cached;
                    count++;
                }
                yield return instruction;
            }
            if (count != 1) throw new InvalidOperationException("Expected one DMSSR hediff lookup, found " + count);
        }

        private static Hediff? CachedLinkedLookup(HediffSet set, HediffDef def, bool mustBeVisible)
            => HediffFlagCache.GetSchizophreniaLinked(set.pawn);

        private static bool ProviderPostfixPrefix(HediffSet __0, PawnCapacityDef __1, ref float __2)
        {
            try
            {
                if (Depth() != 1) return false;
                Pawn? pawn = __0?.pawn;
                if (pawn?.health?.hediffSet == null) return false;
                IDynamicCapacityProvider[] providers = HediffFlagCache.GetCapacityProviders(pawn);
                float mul = 1f;
                float add = 0f;
                foreach (IDynamicCapacityProvider provider in providers)
                {
                    provider.GetCapacityAdjustments(__1, out float factor, out float offset);
                    mul *= factor;
                    add += offset;
                }
                __2 = __2 * mul + add;
                return false;
            }
            finally
            {
                if (Depth() > 0) Depth()--;
            }
        }
    }
}
