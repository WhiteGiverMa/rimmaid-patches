using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Meidocho.DubsPerformanceAnalyzerFix;

public sealed class DubsPerformanceAnalyzerFixMod : Mod
{
    private const string HarmonyId = "meidocho.dubsperformanceanalyzerfix";
    private const string TickProfilerTypeName = "Analyzer.Profiling.H_TickListTick";
    private static readonly HashSet<int> reportedMalformedThingIds = new HashSet<int>();

    public DubsPerformanceAnalyzerFixMod(ModContentPack content) : base(content)
    {
        Type profilerType = AccessTools.TypeByName(TickProfilerTypeName);
        if (profilerType == null)
        {
            Log.Error("[DPA NullDef Fix] Could not resolve the DPA Tick Things profiler type.");
            return;
        }

        MethodInfo getName = AccessTools.DeclaredMethod(profilerType, "GetName", new[] { typeof(Thing) });
        MethodInfo doTick = AccessTools.DeclaredMethod(typeof(Thing), nameof(Thing.DoTick), Type.EmptyTypes);
        MethodInfo getNamePrefix = AccessTools.DeclaredMethod(typeof(DubsPerformanceAnalyzerFixMod), nameof(PrefixGetName));
        MethodInfo doTickPrefix = AccessTools.DeclaredMethod(typeof(DubsPerformanceAnalyzerFixMod), nameof(PrefixThingDoTick));

        if (getName == null || doTick == null || getNamePrefix == null || doTickPrefix == null)
        {
            Log.Error("[DPA NullDef Fix] Could not resolve the required DPA or Thing.DoTick methods.");
            return;
        }

        Harmony harmony = new Harmony(HarmonyId);
        harmony.Patch(getName, prefix: new HarmonyMethod(getNamePrefix));
        harmony.Patch(doTick, prefix: new HarmonyMethod(doTickPrefix));
        Log.Message("[DPA NullDef Fix] Tick Things and def-less tick guards applied.");
    }

    private static bool PrefixGetName(Thing __0, ref string __result)
    {
        ThingDef def = __0?.def;
        if (def?.thingClass != null)
        {
            return true;
        }

        int logKey = (__0?.thingIDNumber ?? 0) ^ 0x2D7A3103;
        if (reportedMalformedThingIds.Add(logKey))
        {
            string runtimeType = __0?.GetType().FullName ?? "<null Thing>";
            string defName = def?.defName ?? "<null Def>";
            string packageId = def?.modContentPack?.PackageId ?? "<unknown package>";
            Log.Warning(
                $"[DPA NullDef Fix] Excluding an unprofileable Tick Things entry: " +
                $"runtimeType={runtimeType}, defName={defName}, thingClass=<null>, packageId={packageId}, " +
                $"thingIDNumber={__0?.thingIDNumber ?? 0}, spawned={__0?.Spawned ?? false}.");
        }
        __result = null;
        return false;
    }

    private static bool PrefixThingDoTick(Thing __instance)
    {
        if (__instance?.def != null)
        {
            return true;
        }

        if (__instance == null)
        {
            Log.ErrorOnce("[DPA NullDef Fix] Thing.DoTick was called with a null instance.", 0x2D7A3101);
            return false;
        }

        int logKey = __instance.thingIDNumber ^ 0x2D7A3102;
        if (reportedMalformedThingIds.Add(logKey))
        {
            Type runtimeType = __instance.GetType();
            Log.Warning(
                $"[DPA NullDef Fix] Skipping a truly def-less ticking Thing that could not complete Thing.DoTick: " +
                $"runtimeType={runtimeType.FullName}, assembly={runtimeType.Assembly.GetName().Name}, " +
                $"thingIDNumber={__instance.thingIDNumber}, spawned={__instance.Spawned}.");
        }
        return false;
    }
}
