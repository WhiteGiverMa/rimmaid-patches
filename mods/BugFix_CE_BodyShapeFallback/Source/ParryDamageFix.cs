using HarmonyLib;
using Verse;

namespace BugFix_CE_ParryDamageError
{
    [StaticConstructorOnStartup]
    public static class ParryDamageFix
    {
        static ParryDamageFix()
        {
            var target = AccessTools.Method("CombatExtended.ArmorUtilityCE:TryPenetrateArmor");
            if (target != null)
            {
                var harmony = new Harmony("com.local.bugfix.ce.parrydamage");
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(ParryDamageFix), "Prefix"));
                Log.Message("[BugFix_CE_ParryDamage] Patched ArmorUtilityCE.TryPenetrateArmor");
            }
            else
            {
                Log.Warning("[BugFix_CE_ParryDamage] Combat Extended not loaded, patch skipped");
            }
        }

        /// <summary>
        /// Suppresses CE's false positive error when a non-armor thing (weapon, etc.)
        /// is checked with zero penAmount or armorAmount.
        /// Weapons use ToughnessRating, not ArmorRating; all three armor stats
        /// are naturally zero - this is not an error condition.
        ///
        /// CE's error fires on: (penAmount==0 || armorAmount==0) AND all armor stats are 0.
        /// We intercept when the thing is not a Pawn and not Apparel (i.e., a weapon/equipment).
        /// </summary>
        public static bool Prefix(Thing armor, DamageDef def, float penAmount, float armorAmount, ref bool __result)
        {
            // Only intervene for weapons/equipment (non-pawn, non-apparel)
            if (armor != null && !(armor is Pawn) && !armor.def.IsApparel)
            {
                // CE fires error when either pen or armor is zero AND armor stats are all zero.
                // For weapons, armor stats are always zero by design.
                if (penAmount == 0f || armorAmount == 0f)
                {
                    // Compute what the original would return:
                    // deflected = isSharp && armorAmount > penAmount (always false for blunt)
                    // dmgAmount stays unchanged (dmgMult=1 when penAmount==0)
                    // For blunt damage, always not deflected. For sharp, only deflected
                    // if armorAmount > penAmount (extremely rare for weapons).
                    bool isSharp = def.armorCategory != null && def.armorCategory.defName == "Sharp";
                    bool deflected = isSharp && armorAmount > penAmount;
                    __result = !deflected;
                    return false; // skip original (which would log error)
                }
            }
            return true;
        }
    }
}
