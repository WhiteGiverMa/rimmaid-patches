using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.CeleTechManagedWorkTableFix;

/// <summary>
/// CeleTech passes a null dominant ingredient into GenRecipe.MakeRecipeProducts.
/// Vanilla dereferences it whenever a product is made from stuff, so the hub
/// completes the bill, destroys the materials and throws before yielding any
/// product. Supply the same default stuff CeleTech's own worker-less path gets.
/// </summary>
public static class MakeRecipeProductsFix
{
    private const string LogPrefix = "[CeleTech Managed WorkTable Fix]";
    private static Type? hubType;
    private static bool loggedFallback;

    public static void Apply()
    {
        try
        {
            hubType = AccessTools.TypeByName("CeleTech.Base.Building_ManagedWorkTable");
            if (hubType == null)
            {
                Log.Message($"{LogPrefix} CeleTech automation hub is not loaded; MakeRecipeProducts patch skipped.");
                return;
            }

            MethodInfo? original = AccessTools.Method(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts));
            if (original == null)
            {
                Log.Error($"{LogPrefix} Expected GenRecipe.MakeRecipeProducts is missing; patch not applied.");
                return;
            }

            new Harmony(MaterialListFix.HarmonyId).Patch(original,
                prefix: new HarmonyMethod(typeof(MakeRecipeProductsFix), nameof(Prefix)));
            Log.Message($"{LogPrefix} Patched MakeRecipeProducts (default stuff for automation-hub products).");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Could not apply MakeRecipeProducts compatibility patch: {exception}");
        }
    }

    private static void Prefix(RecipeDef recipeDef, ref Thing dominantIngredient, IBillGiver billGiver)
    {
        if (dominantIngredient != null || recipeDef?.products == null || billGiver == null)
        {
            return;
        }

        if (hubType == null || !hubType.IsInstanceOfType(billGiver))
        {
            return;
        }

        foreach (ThingDefCountClass product in recipeDef.products)
        {
            ThingDef? thingDef = product?.thingDef;
            if (thingDef == null || !thingDef.MadeFromStuff)
            {
                continue;
            }

            ThingDef? stuffDef;
            try
            {
                stuffDef = GenStuff.DefaultStuffFor(thingDef);
            }
            catch (Exception exception)
            {
                Log.ErrorOnce($"{LogPrefix} Could not choose default stuff for '{thingDef.defName}': {exception}",
                    thingDef.shortHash);
                return;
            }

            if (stuffDef == null || !stuffDef.IsStuff)
            {
                Log.ErrorOnce($"{LogPrefix} No valid default stuff for '{thingDef.defName}'.", thingDef.shortHash);
                return;
            }

            dominantIngredient = ThingMaker.MakeThing(stuffDef);
            if (!loggedFallback)
            {
                loggedFallback = true;
                Log.Message($"{LogPrefix} Automation-hub product '{thingDef.defName}' has no dominant ingredient; " +
                            $"using default stuff '{stuffDef.defName}'.");
            }
            return;
        }
    }
}
