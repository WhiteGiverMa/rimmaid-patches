using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Verse;

namespace WhiteGiver.NewRatkinDrawScaleCacheFix;

public sealed class NewRatkinDrawScaleCacheFixMod : Mod
{
    public NewRatkinDrawScaleCacheFixMod(ModContentPack content) : base(content)
    {
        DrawScaleCacheThreadSafety.Apply();
    }
}

/// <summary>
/// NewRatkinPlus 1.7 keeps draw-scale values in a plain Dictionary&lt;Pawn, float&gt;.
/// RimWorld 1.7 builds pawn draw requests on Waylith job threads, so two ratkins can
/// enter Get/Recalculate at the same time and corrupt that dictionary. The runtime
/// then throws InvalidOperationException and the static cache may stay corrupt.
///
/// Only the two methods that touch the dictionary need one shared monitor. Get may
/// call Recalculate(Pawn, out float) while already holding it; Monitor is reentrant,
/// so the nested call is safe and the cache algorithm itself is untouched.
/// </summary>
public static class DrawScaleCacheThreadSafety
{
    public const string HarmonyId = "whitegiver.newratkin.drawscalecachefix";
    private const string LogPrefix = "[NewRatkin Draw Scale Cache Fix]";
    private static readonly object Gate = new object();
    private static bool applied;

    public static void Apply()
    {
        if (applied) return;
        applied = true;

        try
        {
            Type? cache = AccessTools.TypeByName("NewRatkin.GeneDrawScaleCache");
            if (cache == null)
            {
                Log.Message($"{LogPrefix} NewRatkinPlus is not loaded; patch skipped.");
                return;
            }

            MethodInfo? get = AccessTools.Method(cache, "Get", new[] { typeof(Pawn) });
            MethodInfo? recalculate = AccessTools.Method(
                cache, "Recalculate", new[] { typeof(Pawn), typeof(float).MakeByRefType() });
            if (get == null || recalculate == null)
            {
                Log.Error($"{LogPrefix} Expected GeneDrawScaleCache.Get(Pawn) and Recalculate(Pawn, out float) are missing; patch not applied.");
                return;
            }

            Harmony harmony = new Harmony(HarmonyId);
            harmony.Patch(get,
                prefix: new HarmonyMethod(typeof(DrawScaleCacheThreadSafety), nameof(Enter)),
                finalizer: new HarmonyMethod(typeof(DrawScaleCacheThreadSafety), nameof(Exit)));
            harmony.Patch(recalculate,
                prefix: new HarmonyMethod(typeof(DrawScaleCacheThreadSafety), nameof(Enter)),
                finalizer: new HarmonyMethod(typeof(DrawScaleCacheThreadSafety), nameof(Exit)));
            Log.Message($"{LogPrefix} Patched GeneDrawScaleCache.Get and Recalculate; dictionary access is serialized.");
        }
        catch (Exception exception)
        {
            Log.Error($"{LogPrefix} Could not apply draw-scale cache thread-safety patch: {exception}");
        }
    }

    private static void Enter() => Monitor.Enter(Gate);

    private static Exception? Exit(Exception? __exception)
    {
        Monitor.Exit(Gate);
        return __exception;
    }
}
