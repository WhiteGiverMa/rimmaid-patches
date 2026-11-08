using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UFRecipes;
using UnityEngine;
using Verse;

namespace WhiteGiver.UFLI.DropProductAtFeet;

public sealed class UFLIDropProductAtFeetMod : Mod
{
    public static DropProductAtFeetSettings? Settings { get; private set; }

    public UFLIDropProductAtFeetMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<DropProductAtFeetSettings>();
        DropProductAtFeet.Apply();
    }

    public override string SettingsCategory() => "UFLIDropAtFeet.SettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        if (Settings == null) return;

        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.CheckboxLabeled("UFLIDropAtFeet.EnabledLabel".Translate(), ref Settings.dropAtFeet,
            "UFLIDropAtFeet.EnabledTooltip".Translate());
        listing.Gap(12f);
        listing.Label("UFLIDropAtFeet.SettingsDescription".Translate());
        listing.End();
    }
}

public sealed class DropProductAtFeetSettings : ModSettings
{
    public bool dropAtFeet = true;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref dropAtFeet, "dropAtFeet", true);
    }
}

public static class DropProductAtFeet
{
    public const string HarmonyId = "whitegiver.ufli.dropproductatfeet";
    private const string LogPrefix = "[UFLI Drop Product At Feet]";

    private static Func<Building_SuperWorkbench, Thing, bool>? tryPlaceProductNear;
    private static bool applied;

    public static bool Enabled => UFLIDropProductAtFeetMod.Settings?.dropAtFeet ?? true;

    public static void Apply()
    {
        if (applied) return;
        applied = true;

        try
        {
            MethodInfo? deliver = AccessTools.Method(typeof(Building_SuperWorkbench), "DeliverProduct");
            MethodInfo? near = AccessTools.Method(typeof(Building_SuperWorkbench), "TryPlaceProductNear");
            if (!IsExpectedDeliveryMethod(deliver) || !IsExpectedDeliveryMethod(near))
            {
                Log.Error($"{LogPrefix} Expected private DeliverProduct(Thing) and TryPlaceProductNear(Thing) methods were not found; delivery patch not applied. UFLI may have changed; retire this sidecar.");
                return;
            }

            tryPlaceProductNear = AccessTools.MethodDelegate<Func<Building_SuperWorkbench, Thing, bool>>(near);
            Harmony harmony = new Harmony(HarmonyId);
            harmony.Patch(deliver, prefix: new HarmonyMethod(typeof(DropProductAtFeet), nameof(DeliverProductPrefix)));

            MethodInfo? gizmos = AccessTools.Method(typeof(Building_SuperWorkbench), "GetGizmos");
            if (gizmos == null)
            {
                Log.Warning($"{LogPrefix} GetGizmos was not found; the Mod settings toggle remains available.");
            }
            else
            {
                harmony.Patch(gizmos, postfix: new HarmonyMethod(typeof(DropProductAtFeet), nameof(GetGizmosPostfix)));
            }

            Log.Message($"{LogPrefix} Patched Building_SuperWorkbench.DeliverProduct; drop-at-feet={(Enabled ? "on" : "off")}.");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Could not apply patch: {exception}");
        }
    }

    public static bool DeliverProductPrefix(Building_SuperWorkbench __instance, Thing product, ref bool __result)
    {
        if (!Enabled) return true;

        // Keep UFLI's original guard exactly. In particular, unstorable products and
        // missing maps must still report a failed delivery to the caller.
        if (product == null || product.Destroyed || __instance.Map == null || !product.def.EverStorable(false))
        {
            __result = false;
            return false;
        }

        if (tryPlaceProductNear == null)
        {
            Log.ErrorOnce($"{LogPrefix} TryPlaceProductNear delegate is unavailable; delivery failed safely.", 365179991);
            __result = false;
            return false;
        }

        // TryPlaceProductNear uses ThingPlaceMode.Near at InteractionCell. Vanilla's
        // InteractionCell property falls back to Position when no interaction cell exists.
        // Its true result covers a spawned product, preventing both UFLI failure branches.
        __result = tryPlaceProductNear(__instance, product);
        return false;
    }

    public static IEnumerable<Gizmo> GetGizmosPostfix(IEnumerable<Gizmo> __result, Building_SuperWorkbench __instance)
    {
        foreach (Gizmo gizmo in __result) yield return gizmo;

        yield return new Command_Toggle
        {
            defaultLabel = "UFLIDropAtFeet.GizmoLabel".Translate(),
            defaultDesc = "UFLIDropAtFeet.GizmoDescription".Translate(),
            icon = TexButton.Drop,
            isActive = () => Enabled,
            toggleAction = Toggle,
        };
    }

    private static void Toggle()
    {
        DropProductAtFeetSettings? settings = UFLIDropProductAtFeetMod.Settings;
        if (settings == null) return;
        settings.dropAtFeet = !settings.dropAtFeet;
        settings.Write();
    }

    private static bool IsExpectedDeliveryMethod(MethodInfo? method)
    {
        if (method == null || !method.IsPrivate || method.ReturnType != typeof(bool)) return false;
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == typeof(Thing);
    }
}
