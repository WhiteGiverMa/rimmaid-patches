using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.HaulDestinationTrace;

public sealed class HaulDestinationTraceMod : Mod
{
    public HaulDestinationTraceMod(ModContentPack content) : base(content)
    {
        var harmony = new Harmony(TraceLog.Owner);
        try
        {
            harmony.PatchAll(typeof(HaulDestinationTraceMod).Assembly);
            TraceLog.Reset();
            TraceLog.Write("Loaded 1.0.0; diagnostic only, original jobs and exceptions are unchanged.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchAll(TraceLog.Owner);
            TraceLog.Write("Registration failed; no diagnostic patches retained: " + exception.GetType().Name);
        }
    }
}

internal static class TraceLog
{
    internal const string Owner = "whitegiver.diagnostics.hauldestination";
    private const int Limit = 128;
    private static readonly object Gate = new();
    private static readonly Dictionary<Job, Trace> Jobs = new(ReferenceEqualityComparer.Instance);
    private static readonly List<Trace> Generations = new();
    private static readonly Dictionary<Pawn_PathFollower, PathFrames> Paths = new(ReferenceEqualityComparer.Instance);
    private static int reports, dropped;
    private static bool disabled;
    [ThreadStatic] private static bool observing;

    private sealed class Trace(Job job, Pawn pawn)
    {
        internal readonly Job Job = job;
        internal readonly int Id = job.loadID;
        internal readonly Pawn Pawn = pawn;
        internal readonly Queue<string> Events = new();
        internal string Origin = "", Bench = "bench=not_observed";
        internal bool Active, Ended, Created, InvalidReported, ClearedWhileActive;
        internal int Reports;
    }

    private sealed record PathEntry(Trace Trace, string Input);
    private sealed class PathFrames
    {
        internal readonly Stack<PathEntry?> Entries = new();
        internal int Overflow;
    }

    internal static void Reset()
    {
        lock (Gate)
        {
            Jobs.Clear();
            Generations.Clear();
            Paths.Clear();
            reports = dropped = 0;
            disabled = false;
        }
    }

    internal static void Write(string message)
    {
        try { Log.Message("[HaulDestinationTrace] " + message); }
        catch (Exception) { /* ponytail: diagnostic output must never replace the game's result or exception. */ }
    }

    private static void Observe(Func<string?> action)
    {
        if (observing || disabled) return;
        observing = true;
        try
        {
            string? message;
            lock (Gate) message = action();
            if (message != null) Write(message);
        }
        catch (Exception exception)
        {
            disabled = true;
            Write("observer_disabled=" + exception.GetType().Name + "; original execution continues unchanged.");
        }
        finally { observing = false; }
    }

    private static bool Has(Job? job)
    {
        if (observing || disabled || job == null) return false;
        lock (Gate) return Jobs.ContainsKey(job);
    }

    private static Trace? Find(Job? job)
    {
        if (job == null || !Jobs.TryGetValue(job, out Trace? trace)) return null;
        if (trace.Id == job.loadID) return trace;
        if (trace.ClearedWhileActive && job.loadID == -1 && job.def == null && InUse(trace)) return trace;
        string prior = $"rebind_missing_create oldId={trace.Id} newId={job.loadID} prior_tail=({string.Join(" | ", trace.Events.TakeLast(2))})";
        Note(trace, prior);
        trace.Ended = true;
        Jobs.Remove(job);
        return job.def?.defName == "HaulToCell" ? NewTrace(job, trace.Pawn, prior) : null;
    }

    private static bool InUse(Trace trace) => ReferenceEquals(trace.Pawn.jobs?.curJob, trace.Job) ||
        ReferenceEquals(trace.Pawn.jobs?.curDriver?.job, trace.Job);

    private static Trace? NewTrace(Job job, Pawn pawn, string origin)
    {
        if (Generations.Count == Limit)
        {
            int index = Generations.FindIndex(candidate => candidate.Reports == 0 && (candidate.Ended || !candidate.Active));
            if (index < 0) { dropped++; return null; }
            Trace removed = Generations[index];
            Generations.RemoveAt(index);
            if (Jobs.TryGetValue(removed.Job, out Trace? mapped) && ReferenceEquals(mapped, removed)) Jobs.Remove(removed.Job);
            dropped++;
        }
        var trace = new Trace(job, pawn) { Origin = origin };
        Jobs[job] = trace;
        Generations.Add(trace);
        return trace;
    }

    private static void Note(Trace trace, string text)
    {
        if (trace.Events.Count == 16) trace.Events.Dequeue();
        trace.Events.Enqueue($"tick={Verse.Find.TickManager?.TicksGame ?? -1} {text}");
    }

    private static string? Report(Trace trace, string reason)
    {
        if (reports >= 8 || trace.Reports >= 2) return null;
        reports++;
        trace.Reports++;
        return $"report={reports} reason={reason} trackedId={trace.Id} trace_dropped={dropped}\n" +
            trace.Origin + "\n" + trace.Bench + "\n" + string.Join("\n", trace.Events);
    }

    private static string? Invalid(Trace trace, string phase)
    {
        if (trace.InvalidReported || trace.Job.targetB.IsValid) return null;
        trace.InvalidReported = true;
        return Report(trace, "invalid_at_" + phase);
    }

    private static string ObjectId(object? value) => value == null ? "null" : RuntimeHelpers.GetHashCode(value).ToString("x8");
    private static string Identity(Job? job) => job == null ? "null" : $"{job.def?.defName ?? "null"}#{job.loadID}@{ObjectId(job)}";
    private static string ThingId(Thing? thing) => thing == null ? "null" :
        $"{thing.def?.defName ?? "null"}#{thing.thingIDNumber} pos={thing.Position} spawned={thing.Spawned} map={thing.Map?.uniqueID.ToString() ?? "null"}";
    private static string Target(LocalTargetInfo target) => $"valid={target.IsValid} cell={target.Cell} thing=({ThingId(target.Thing)})";
    private static string Snapshot(Pawn pawn, Job job)
    {
        Job? current = pawn.jobs?.curJob;
        Job? driver = pawn.jobs?.curDriver?.job;
        return $"job={Identity(job)} pawn=({ThingId(pawn)}) curJob={Identity(current)} driverJob={Identity(driver)} " +
            $"sameJob={ReferenceEquals(current, driver)} curB=({(current == null ? "null" : Target(current.targetB))}) " +
            $"driverB=({(driver == null ? "null" : Target(driver.targetB))}) " +
            $"A=({Target(job.targetA)}) B=({Target(job.targetB)}) C=({Target(job.targetC)})";
    }

    private static string Callers() => string.Join(" <- ", new StackTrace(2, false).GetFrames().Take(8)
        .Select(frame => frame.GetMethod()).Select(method => method?.DeclaringType?.FullName + "." + method?.Name));

    internal static void Created(Pawn pawn, Job? job)
    {
        if (job == null || job.def?.defName != "HaulToCell") return;
        Observe(() =>
        {
            Trace? trace = Find(job) ?? NewTrace(job, pawn, "");
            if (trace == null || trace.Created) return null;
            trace.Created = true;
            trace.Origin = "created " + Snapshot(pawn, job) + " " + trace.Origin;
            Note(trace, "created");
            return Invalid(trace, "creation_return");
        });
    }

    internal static void Bench(IBillGiver giver, Job? job)
    {
        if (!Has(job)) return;
        Observe(() =>
        {
            Trace? trace = Find(job);
            if (trace != null && trace.Created) trace.Bench = giver is Thing thing ? "bench=(" + ThingId(thing) + ")" : "bench_type=" + giver.GetType().FullName;
            return null;
        });
    }

    internal static void Step(JobDriver driver, string phase)
    {
        if (!Has(driver.job)) return;
        Observe(() =>
        {
            Trace? trace = Find(driver.job);
            if (trace == null) return null;
            trace.Active = true;
            Note(trace, $"toil_{phase} index={driver.CurToilIndex} ended={driver.ended} {Snapshot(trace.Pawn, trace.Job)}");
            return trace.Ended ? null : Invalid(trace, "toil_" + phase);
        });
    }

    internal static void Clear(Job job)
    {
        if (!Has(job)) return;
        Observe(() =>
        {
            Trace? trace = Find(job);
            if (trace != null)
            {
                Note(trace, "clear_before " + Snapshot(trace.Pawn, job) + " callers=" + Callers());
                trace.ClearedWhileActive = InUse(trace);
                trace.Ended = !trace.ClearedWhileActive;
                trace.Active = trace.ClearedWhileActive;
            }
            return null;
        });
    }

    internal static void Set(Job job, TargetIndex index, LocalTargetInfo target)
    {
        if (index != TargetIndex.B || !Has(job)) return;
        Observe(() =>
        {
            Trace? trace = Find(job);
            if (trace != null) Note(trace, $"set_B before=({Target(job.targetB)}) requested=({Target(target)}) callers={Callers()}");
            return null;
        });
    }

    internal static void PathEnter(Pawn_PathFollower follower, Pawn pawn, LocalTargetInfo dest, PathEndMode mode)
    {
        Observe(() =>
        {
            Trace? trace = Find(pawn.jobs?.curJob);
            if (trace == null && !Paths.ContainsKey(follower)) return null;
            if (!Paths.TryGetValue(follower, out PathFrames? frames))
            {
                if (Paths.Count == Limit) { dropped++; return null; }
                Paths[follower] = frames = new PathFrames();
            }
            if (frames.Overflow > 0 || frames.Entries.Count == 8) { frames.Overflow++; dropped++; return null; }
            string input = trace == null ? "untracked_nested" : $"path_enter mode={mode} dest=({Target(dest)}) {Snapshot(pawn, trace.Job)}";
            frames.Entries.Push(trace == null ? null : new PathEntry(trace, input));
            if (trace != null) Note(trace, input);
            return null;
        });
    }

    internal static void PathExit(Pawn_PathFollower follower, Pawn pawn, LocalTargetInfo dest, PathEndMode mode, Exception? error)
    {
        Observe(() =>
        {
            Trace? trace;
            if (Paths.TryGetValue(follower, out PathFrames? frames))
            {
                if (frames.Overflow > 0) { frames.Overflow--; return null; }
                PathEntry? entry = frames.Entries.Pop();
                if (frames.Entries.Count == 0) Paths.Remove(follower);
                if (entry == null) return null;
                trace = entry.Trace;
            }
            else
            {
                Job? job = pawn.jobs?.curJob;
                if (error == null || job?.def?.defName != "HaulToCell" || reports >= 8) return null;
                trace = Find(job) ?? NewTrace(job, pawn, "missing_creation_observation; producer_unknown " + Snapshot(pawn, job));
                if (trace == null) return null;
                trace.Active = true;
                Note(trace, "missing_path_enter_observation");
            }
            Note(trace, $"path_exit mode={mode} dest=({Target(dest)}) exception={error?.GetType().FullName ?? "none"} {Snapshot(pawn, trace.Job)}");
            if (error == null && dest.IsValid && trace.Job.targetB.IsValid) return null;
            return Report(trace, "path_failure_or_invalid_destination");
        });
    }
}

[HarmonyPatch(typeof(HaulAIUtility), nameof(HaulAIUtility.HaulAsideJobFor))]
internal static class CreatedHook
{
    [HarmonyPostfix] private static void Postfix(Pawn p, Job? __result) => TraceLog.Created(p, __result);
}

[HarmonyPatch(typeof(WorkGiverUtility), nameof(WorkGiverUtility.HaulStuffOffBillGiverJob))]
internal static class BenchHook
{
    [HarmonyPostfix] private static void Postfix(IBillGiver giver, Job? __result) => TraceLog.Bench(giver, __result);
}

[HarmonyPatch(typeof(JobDriver), "TryActuallyStartNextToil")]
internal static class StepHook
{
    [HarmonyPrefix] private static void Prefix(JobDriver __instance) => TraceLog.Step(__instance, "before");
    [HarmonyPostfix] private static void Postfix(JobDriver __instance) => TraceLog.Step(__instance, "after");
}

[HarmonyPatch(typeof(Job), nameof(Job.Clear))]
internal static class ClearHook
{
    [HarmonyPrefix] private static void Prefix(Job __instance) => TraceLog.Clear(__instance);
}

[HarmonyPatch(typeof(Job), nameof(Job.SetTarget))]
internal static class SetHook
{
    [HarmonyPrefix] private static void Prefix(Job __instance, TargetIndex ind, LocalTargetInfo pack) => TraceLog.Set(__instance, ind, pack);
}

[HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
internal static class PathHook
{
    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    private static void Prefix(Pawn_PathFollower __instance, Pawn ___pawn, LocalTargetInfo dest, PathEndMode peMode) =>
        TraceLog.PathEnter(__instance, ___pawn, dest, peMode);

    [HarmonyFinalizer, HarmonyPriority(Priority.Last)]
    private static void Finalizer(Pawn_PathFollower __instance, Pawn ___pawn, LocalTargetInfo dest, PathEndMode peMode, Exception? __exception) =>
        TraceLog.PathExit(__instance, ___pawn, dest, peMode, __exception);
}

[HarmonyPatch(typeof(Game), MethodType.Constructor)]
internal static class ResetHook
{
    [HarmonyPostfix] private static void Postfix() => TraceLog.Reset();
}
