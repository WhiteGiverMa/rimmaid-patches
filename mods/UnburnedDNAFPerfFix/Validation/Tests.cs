using System.Reflection;
using HarmonyLib;
using RimWorld;
using Unity.Collections;
using Unity.Jobs;
using Verse;
using Verse.AI;

namespace Validation;

internal static class Tests
{
    internal static int Scans, Stats;
    internal static readonly List<Thing> Attacks = [];
    internal static readonly List<Fire> Beats = [];
    internal static float Flammability = 0f;
    internal static MethodInfo Pretty => Program.Unburned.GetType("Unburned.Pretty_Patch", true)!.GetMethod("Prefix")!;
    internal static void Wait(JobDriver_Wait driver) => AccessTools.Method(typeof(JobDriver_Wait), "CheckForAutoAttack").Invoke(driver, null);
    internal static MethodInfo Sealed => Program.Unburned.GetType("Unburned.StatWorker_Flammability", true)!.GetMethod("IsSealed")!;
    internal static int Run(bool green)
    {
        Audit.Hooks.Patch(AccessTools.Method(typeof(GridsUtility), nameof(GridsUtility.GetThingList)), prefix: new HarmonyMethod(typeof(Tests), nameof(Scan)));
        Audit.Hooks.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.WorkTagIsDisabled)), prefix: new HarmonyMethod(typeof(Tests), nameof(False)));
        Audit.Hooks.Patch(AccessTools.Method(typeof(Pawn_MeleeVerbs), nameof(Pawn_MeleeVerbs.TryMeleeAttack)), prefix: new HarmonyMethod(typeof(Tests), nameof(Melee)));
        Audit.Hooks.Patch(AccessTools.Method(typeof(Pawn_NativeVerbs), nameof(Pawn_NativeVerbs.TryBeatFire)), prefix: new HarmonyMethod(typeof(Tests), nameof(Beat)));
        Audit.Hooks.Patch(AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValue)), prefix: new HarmonyMethod(typeof(Tests), nameof(Stat)));
        var f = new Fixture();
        Pawn chicken = f.Pawn(new IntVec3(5, 0, 5));
        for (int i = 0; i < 81; i++) f.Pawn(new IntVec3(5 + i % 3, 0, 5 + i % 3));
        Scans = 0; Wait(f.Wait(chicken));
        Check(Scans == (green ? 0 : 9), $"peaceful chicken scans = {Scans}");
        Fire distant = f.Fire(new IntVec3(200, 0, 200)); f.Tick();
        Scans = 0; Wait(f.Wait(chicken));
        Check(Scans == (green ? 0 : 9), $"active-fire chicken scans = {Scans}");
        Pawn colonist = f.Pawn(new IntVec3(15, 0, 15), true); f.Tick();
        Scans = 0; Wait(f.Wait(colonist));
        Check(Scans == (green ? 0 : 9), $"distant-fire colonist scans = {Scans}");
        _ = Sealed.Invoke(null, [chicken]);
        var cache = (System.Collections.IDictionary)AccessTools.Field(Program.Unburned.GetType("Unburned.StatWorker_Flammability"), "sealedCache").GetValue(null)!;
        Check(cache.Count == (green ? 0 : 1), $"upstream strong cache after animal = {cache.Count}");
        if (Program.Dnaf != null)
        {
            f.Map.thingGrid.Deregister(distant); f.Map.listerThings.Remove(distant); f.Tick();
            using NativeArray<int> grid = new(f.Map.cellIndices.NumGridCells, Allocator.Persistent);
            var process = Program.Dnaf.GetType("DoNotAvoidFire.HarmonyPrivate_Patch")!.GetMethod("ProcessIgnoreFire")!;
            Stats = 0;
            process.Invoke(null, [grid, f.Request(chicken), default(PathFinder.MapGridRequest), default(PathFinderJob), default(NativeArray<ushort>), default(NativeArray<bool>), default(JobHandle)]);
            Check(Stats == (green ? 0 : 1), $"no-fire path stat calls = {Stats}");
            MethodInfo update = f.Component!.GetType().GetMethod("UpdateFire")!;
            update.Invoke(f.Component, [-1, chicken.Position]);
            int count = (int)AccessTools.Field(f.Component.GetType(), "firecount").GetValue(f.Component)!;
            Check(green ? count >= 0 : count == -1, $"unmatched remove count = {count}");
        }
        if (Program.Dnaf != null) GridTests.Run(green);
        BehaviorTests.Run(green);
        if (green && Program.Dnaf != null) LifecycleTests.Run();
        if (green) CacheTests.Run();
        WorkloadTests.Run(green);
        Console.WriteLine(green ? "GREEN complete" : "RED: reproduced upstream excess scans, cache retention, premature stat read and counter underflow");
        return 0;
    }
    internal static void Check(bool ok, string msg) { if (!ok) throw new Exception("FAIL " + msg); Console.WriteLine("PASS " + msg); }
    private static void Scan() => Scans++;
    private static bool False(ref bool __result) { __result = false; return false; }
    private static bool Stat(ref float __result) { Stats++; __result = Flammability; return false; }
    private static bool Melee(Thing target, ref bool __result) { Attacks.Add(target); __result = true; return false; }
    private static bool Beat(Fire targetFire) { Beats.Add(targetFire); return false; }
}
