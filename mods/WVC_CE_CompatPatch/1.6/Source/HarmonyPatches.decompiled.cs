// Recovered from WVC_CE_CompatPatch.dll with ILSpy 11.0.0 on 2026-09-15.
// This is a readable recovery copy, not the original source file.
using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace WVC_CE_CompatPatch;

[StaticConstructorOnStartup]
public static class HarmonyPatches
{
    static HarmonyPatches()
    {
        try
        {
            var harmony = new Harmony("meidocho.wvc.ce.compatpatch");
            MethodInfo threeParameter = AccessTools.Method(
                typeof(GenRadial),
                "RadialCellsAround",
                new[] { typeof(IntVec3), typeof(float), typeof(bool) });
            if (threeParameter != null)
            {
                harmony.Patch(threeParameter, prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(CapRadiusPrefix)));
                Log.Message("[WVC_CE_CompatPatch] Patched GenRadial.RadialCellsAround — radius capped to 118.");
            }
            else
            {
                Log.Error("[WVC_CE_CompatPatch] Could not find GenRadial.RadialCellsAround(IntVec3, float, bool).");
            }

            MethodInfo twoParameter = AccessTools.Method(
                typeof(GenRadial),
                "RadialCellsAround",
                new[] { typeof(IntVec3), typeof(float) });
            if (twoParameter != null)
                harmony.Patch(twoParameter, prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(CapRadiusPrefix)));
        }
        catch (Exception exception)
        {
            Log.Error($"[WVC_CE_CompatPatch] Patching failed: {exception}");
        }
    }

    public static void CapRadiusPrefix(ref float radius)
    {
        if (radius > 118f)
            radius = 118f;
    }
}
