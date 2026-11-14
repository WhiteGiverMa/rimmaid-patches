using System;
using System.Reflection;
using DMSSR.Patches;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSSRStatPathPerfFix
{
    /// <summary>
    /// DMSSR patches BOTH overloads of <c>Pawn_PathFollower.CostToMoveIntoCell</c>:
    ///
    ///   private float CostToMoveIntoCell(IntVec3 c)              -> Postfix ClampPathCostForAbility(___pawn, c, ref __result)
    ///   private static float CostToMoveIntoCell(Pawn pawn, IntVec3 c) -> Postfix ClampPathCostForAbility(pawn, c, ref __result)
    ///
    /// Vanilla 1.7's instance overload is exactly <c>return CostToMoveIntoCell(pawn, c);</c>, so every
    /// single step of every pawn runs ClampPathCostForAbility twice, and each run walks every Hediff of
    /// that pawn twice over (mod extension + comp).
    ///
    /// It is also a correctness bug, not just a cost bug: ClampPathCostForAbility computes
    /// <c>pathCost = max(pathCost - terrain.pathCost, ticksPerMove)</c>. Running it twice subtracts
    /// terrain.pathCost twice whenever a cell costs more than terrain + the pawn's own move ticks
    /// (doors, terrain tags, locomotion urgency, other postfixes). The result is below the intended
    /// single-clamp cost, though the ticksPerMove floor is still enforced.
    ///
    /// Fix: when the vanilla instance overload still delegates to the static one, drop DMSSR's
    /// instance postfix (one clamp per step, exactly as the author intended) and replace the clamp's
    /// hediff scan with the cached answer. If a future game version stops delegating, we keep both
    /// postfixes and only cache the scan, so behaviour stays bit-for-bit upstream.
    /// </summary>
    internal static class PathCostPatches
    {
        internal static bool DelegationVerified { get; private set; }

        internal static bool SingleClampInstalled { get; private set; }

        internal static bool CacheOnlyFallbackInstalled { get; private set; }

        internal static void Apply(Harmony harmony)
        {
            MethodInfo? clamp = AccessTools.Method(
                typeof(AbilityPathCostUtility),
                "ClampPathCostForAbility",
                new[] { typeof(Pawn), typeof(IntVec3), typeof(float).MakeByRefType() });
            if (clamp == null)
            {
                SafeLog.Error("AbilityPathCostUtility.ClampPathCostForAbility was not found; path-cost fix skipped.");
                return;
            }

            DelegationVerified = InstanceOverloadDelegatesToStatic();
            bool staticPostfixPresent = HasStaticPostfix();

            // Install the replacement clamp first: if Harmony refuses it we have changed nothing yet
            // and the fallback below still applies. Unpatching before installing would risk ending up
            // with no clamp at all for pawns that ignore terrain penalties.
            if (DelegationVerified && staticPostfixPresent)
            {
                try
                {
                    harmony.Patch(clamp, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(PathCostPatches), nameof(ClampPrefix))));
                }
                catch (Exception exception)
                {
                    SafeLog.Warning("the replacement clamp could not be installed.\n" + exception);
                }
            }

            if (HasOurClampPrefix(clamp))
            {
                if (TryRemoveInstancePostfix(harmony))
                {
                    SingleClampInstalled = true;
                    SafeLog.Message(
                        "path cost: vanilla instance overload still delegates to the static overload, so DMSSR's "
                        + "duplicate instance postfix was removed. One clamp and one cached hediff lookup per step.");
                }
                else
                {
                    CacheOnlyFallbackInstalled = true;
                    SafeLog.Warning(
                        "path cost: the replacement clamp is installed but DMSSR's duplicate instance postfix could "
                        + "not be removed. Both postfixes stay active (upstream double-subtraction preserved) and only "
                        + "the hediff scan is cached.");
                }

                return;
            }

            // Conservative path: leave DMSSR's patch set untouched and only make its scan O(1).
            MethodInfo? hasIgnore = AccessTools.Method(
                typeof(AbilityPathCostUtility),
                "HasIgnoreTerrainMovePenalty",
                new[] { typeof(Pawn) });
            if (hasIgnore != null)
            {
                harmony.Patch(hasIgnore, prefix: new HarmonyMethod(
                    AccessTools.Method(typeof(PathCostPatches), nameof(HasIgnorePrefix))));
                CacheOnlyFallbackInstalled = true;
                SafeLog.Warning(
                    "path cost: could not prove the single-clamp shape (vanilla delegation verified="
                    + DelegationVerified + ", DMSSR static postfix present=" + staticPostfixPresent
                    + "). Both DMSSR postfixes were kept, so its double-subtraction behaviour is preserved; "
                    + "only the hediff scan is now cached.");
                return;
            }

            SafeLog.Error("AbilityPathCostUtility.HasIgnoreTerrainMovePenalty was not found; path-cost fix skipped.");
        }

        private static bool HasOurClampPrefix(MethodInfo clamp)
        {
            Patches? info = Harmony.GetPatchInfo(clamp);
            if (info == null)
            {
                return false;
            }

            MethodInfo prefix = AccessTools.Method(typeof(PathCostPatches), nameof(ClampPrefix));
            foreach (Patch patch in info.Prefixes)
            {
                if (patch.PatchMethod == prefix)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasStaticPostfix()
        {
            MethodInfo? original = AccessTools.Method(
                typeof(Pawn_PathFollower),
                "CostToMoveIntoCell",
                new[] { typeof(Pawn), typeof(IntVec3) });
            MethodInfo? postfix = AccessTools.Method(typeof(Patch_Ability_PathCost_Static), "Postfix");
            if (original == null || postfix == null)
            {
                return false;
            }

            Patches? info = Harmony.GetPatchInfo(original);
            if (info == null)
            {
                return false;
            }

            foreach (Patch patch in info.Postfixes)
            {
                if (patch.PatchMethod == postfix)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Replaces DMSSR's ClampPathCostForAbility with the identical arithmetic plus a cached
        /// "does this pawn ignore terrain move penalty" answer. Returning false skips the original,
        /// which is safe because the only difference is where the flag comes from.
        /// </summary>
        private static bool ClampPrefix(Pawn pawn, IntVec3 c, ref float pathCost)
        {
            if (!HediffFlagCache.HasIgnoreTerrainMovePenalty(pawn))
            {
                return false;
            }

            Map? map = pawn.Map;
            if (map == null || !pawn.Spawned || !GenGrid.InBounds(c, map) || !GenGrid.Walkable(c, map))
            {
                return false;
            }

            IntVec3 position = pawn.Position;
            float num = position.AdjacentToCardinal(c) ? pawn.TicksPerMoveCardinal : pawn.TicksPerMoveDiagonal;
            if (num <= 0f || float.IsNaN(num))
            {
                return false;
            }

            int num2 = c.GetTerrain(map)?.pathCost ?? 0;
            float num3 = pathCost - num2;
            pathCost = num3 < num ? num : num3;
            return false;
        }

        /// <summary>Fallback: identical bool, cached answer.</summary>
        private static bool HasIgnorePrefix(Pawn pawn, ref bool __result)
        {
            __result = HediffFlagCache.HasIgnoreTerrainMovePenalty(pawn);
            return false;
        }

        private static bool TryRemoveInstancePostfix(Harmony harmony)
        {
            MethodInfo? instance = AccessTools.Method(
                typeof(Pawn_PathFollower),
                "CostToMoveIntoCell",
                new[] { typeof(IntVec3) });
            MethodInfo? upstreamPostfix = AccessTools.Method(
                typeof(Patch_Ability_PathCost_Instance),
                "Postfix",
                new[] { typeof(Pawn), typeof(IntVec3), typeof(float).MakeByRefType() });
            if (instance == null || upstreamPostfix == null)
            {
                return false;
            }

            try
            {
                harmony.Unpatch(instance, upstreamPostfix);
            }
            catch (Exception exception)
            {
                SafeLog.Error("removing DMSSR's duplicate CostToMoveIntoCell postfix failed.", exception);
                return false;
            }

            Patches? info = Harmony.GetPatchInfo(instance);
            if (info == null)
            {
                return true;
            }

            foreach (Patch patch in info.Postfixes)
            {
                if (patch.PatchMethod == upstreamPostfix)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when <c>CostToMoveIntoCell(IntVec3)</c> is literally
        /// <c>ldarg.0; ldfld Pawn_PathFollower.pawn; ldarg.1; call CostToMoveIntoCell(Pawn, IntVec3); ret</c>.
    /// This proves both postfixes process the same path step. Removing the duplicate deliberately
    /// changes flagged costs where two terrain subtractions differed from one.
        /// </summary>
        private static bool InstanceOverloadDelegatesToStatic()
        {
            MethodInfo? instance = AccessTools.Method(
                typeof(Pawn_PathFollower),
                "CostToMoveIntoCell",
                new[] { typeof(IntVec3) });
            MethodInfo? staticOverload = AccessTools.Method(
                typeof(Pawn_PathFollower),
                "CostToMoveIntoCell",
                new[] { typeof(Pawn), typeof(IntVec3) });
            if (instance == null || staticOverload == null)
            {
                return false;
            }

            byte[]? il = instance.GetMethodBody()?.GetILAsByteArray();
            if (il == null)
            {
                return false;
            }

            // Opcodes: 0x02 ldarg.0, 0x7B ldfld <token>, 0x03 ldarg.1, 0x28 call <token>, 0x2A ret.
            int index = 0;
            if (!TryReadByte(il, ref index, 0x02))
            {
                return false;
            }

            if (!TryReadByte(il, ref index, 0x7B))
            {
                return false;
            }

            if (!TryReadToken(il, ref index, out int fieldToken))
            {
                return false;
            }

            FieldInfo? field;
            try
            {
                field = instance.Module.ResolveField(fieldToken);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (field == null || field.Name != "pawn" || field.DeclaringType != typeof(Pawn_PathFollower))
            {
                return false;
            }

            if (!TryReadByte(il, ref index, 0x03))
            {
                return false;
            }

            if (!TryReadByte(il, ref index, 0x28))
            {
                return false;
            }

            if (!TryReadToken(il, ref index, out int methodToken))
            {
                return false;
            }

            MethodBase? target;
            try
            {
                target = instance.Module.ResolveMethod(methodToken);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (target != staticOverload)
            {
                return false;
            }

            return index == il.Length - 1 && il[index] == 0x2A;
        }

        private static bool TryReadByte(byte[] il, ref int index, byte expected)
        {
            if (index >= il.Length || il[index] != expected)
            {
                return false;
            }

            index++;
            return true;
        }

        private static bool TryReadToken(byte[] il, ref int index, out int token)
        {
            token = 0;
            if (index + 4 > il.Length)
            {
                return false;
            }

            token = BitConverter.ToInt32(il, index);
            index += 4;
            return true;
        }
    }
}
