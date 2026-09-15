// Recovered from GizmoDiag.dll with ILSpy 11.0.0 on 2026-09-15.
// This is a readable recovery copy, not the original source file.
using System;
using System.Diagnostics;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace GizmoDiag;

public sealed class GizmoDiagMod : Mod
{
    public GizmoDiagMod(ModContentPack content) : base(content)
    {
        Log.Message("[GizmoDiag] Mod loaded, patching...");
        new Harmony("meidocho.gizmodiag").PatchAll();
        Log.Message("[GizmoDiag] PatchAll done");
    }
}

internal sealed class Sampler
{
    private readonly Stopwatch stopwatch = new();
    private readonly string label;
    private readonly bool includeGc;
    private float lastLogTime;
    private int samples;
    private long totalMicroseconds;
    private long maxMicroseconds;
    private int lastGc0 = -1;
    private int lastGc1 = -1;
    private long lastMemoryBytes;

    internal Sampler(string label, bool includeGc = false)
    {
        this.label = label;
        this.includeGc = includeGc;
    }
    internal void Begin() => stopwatch.Restart();

    internal void End()
    {
        stopwatch.Stop();
        long elapsed = (long)(stopwatch.Elapsed.TotalMilliseconds * 1000d);
        samples++;
        totalMicroseconds += elapsed;
        maxMicroseconds = Math.Max(maxMicroseconds, elapsed);
        if (Time.realtimeSinceStartup - lastLogTime < 2f)
            return;

        lastLogTime = Time.realtimeSinceStartup;
        string gc = "";
        if (includeGc)
        {
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            long memoryBytes = GC.GetTotalMemory(false);
            if (lastGc0 >= 0)
                gc = $" GC0={gc0 - lastGc0} GC1={gc1 - lastGc1} memDelta={(memoryBytes - lastMemoryBytes) / 1024}KB";
            lastGc0 = gc0;
            lastGc1 = gc1;
            lastMemoryBytes = memoryBytes;
        }

        Log.Message($"[GizmoDiag] {label}: avg={totalMicroseconds / samples}us max={maxMicroseconds}us samples={samples}{gc}");
        samples = 0;
        totalMicroseconds = 0;
        maxMicroseconds = 0;
    }
}

[HarmonyPatch(typeof(GizmoGridDrawer), "DrawGizmoGridFor")]
public static class PatchDrawGizmoGridFor
{
    private static readonly Sampler Sampler = new("DrawGizmoGridFor", includeGc: true);
    private static void Prefix() => Sampler.Begin();
    private static void Postfix() => Sampler.End();
}

[HarmonyPatch(typeof(GizmoGridDrawer), "DrawGizmoGrid")]
public static class PatchDrawGizmoGrid
{
    private static readonly Sampler Sampler = new("DrawGizmoGrid");
    private static void Prefix() => Sampler.Begin();
    private static void Postfix() => Sampler.End();
}

[HarmonyPatch(typeof(SelectionDrawer), "DrawSelectionOverlays")]
public static class PatchDrawSelectionOverlays
{
    private static readonly Sampler Sampler = new("DrawSelectionOverlays");
    private static void Prefix() => Sampler.Begin();
    private static void Postfix() => Sampler.End();
}
