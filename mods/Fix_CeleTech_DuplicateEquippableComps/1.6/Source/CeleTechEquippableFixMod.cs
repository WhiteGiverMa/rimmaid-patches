using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Meidocho.CeleTechDuplicateEquippableFix;

public sealed class CeleTechEquippableFixMod : Mod
{
    private const string HarmonyId = "meidocho.celetech.duplicateequippablecompsfix";

    // Only the two defs whose comps node the XML patch cut from the vanilla BaseWeapon
    // chain, leaving CompLegendaryWeapons as the single equippable comp.
    private static readonly HashSet<string> ScopedDefs = new()
    {
        "TangDaoPersona",
        "CMC_EMPsword_sevenstars",
    };

    public CeleTechEquippableFixMod(ModContentPack content) : base(content)
    {
        Apply();
    }

    // Public so the callback harness can exercise the real patch source without a ModContentPack.
    public static void Apply()
    {
        Harmony harmony = new(HarmonyId);

        // Reflection target: no compile-time dependency on CeleTech.Base.dll.
        var target = AccessTools.TypeByName("CeleTech.Base.CompLegendaryWeapons");
        var original = AccessTools.Method(target, "Notify_Unequipped");
        if (target == null || original == null)
        {
            Log.Error("[CeleTech Equippable Fix] Could not resolve CeleTech.Base.CompLegendaryWeapons.Notify_Unequipped; patch not applied.");
            return;
        }

        harmony.Patch(
            original,
            prefix: new HarmonyMethod(typeof(CeleTechEquippableFixMod), nameof(FireEquipmentLost)));
        Log.Message("[CeleTech Equippable Fix] CompLegendaryWeapons.Notify_Unequipped verb-loss prefix applied.");
    }

    // CompLegendaryWeapons.Notify_Unequipped overrides without calling base, so the
    // CompEquippable.Notify_Unequipped verb callbacks (Verb.Notify_EquipmentLost:
    // cancels warmup stance / AttackStatic job) never fire once this comp is the sole
    // equippable comp. Void prefix runs before the original ability removal.
    // Deliberately does NOT invoke the base method via reflection: it is a virtual
    // dispatch on this instance and would re-enter the override (recursion).
    private static void FireEquipmentLost(CompEquippable __instance)
    {
        if (__instance?.parent?.def == null || !ScopedDefs.Contains(__instance.parent.def.defName))
        {
            return;
        }

        List<Verb> verbs = __instance.AllVerbs;
        for (int i = 0; i < verbs.Count; i++)
        {
            verbs[i].Notify_EquipmentLost();
        }
    }
}
