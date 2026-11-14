using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMSSR.Content;
using DMSSR.Hediffs;
using DMSSR.Mech;
using DMSSR.Patches;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace DMSSRStatPathPerfFix.Validation;

internal static class HotspotBenchmarks
{
    private delegate void CapacityPostfix(HediffSet set, PawnCapacityDef capacity, ref float value);
    private delegate void SchizophreniaPostfix(PawnCapacitiesHandler handler, PawnCapacityDef capacity, ref float value);
    private delegate void ScalePostfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 value);
    private delegate bool Transform(Pawn pawn, Rot4 facing, out Matrix4x4 matrix);

    internal static void Run(Fixture fx)
    {
        Harmony mod = new(Program.Owner);
        Pawn pawn = fx.NewPawn(fx.MechDef, new IntVec3(2, 0, 2));
        pawn.def = new ThingDef { defName = "ValidationBenchmarkRace", race = fx.MechDef.race,
            modExtensions = Enumerable.Range(0, 30).Select(_ => (DefModExtension)new RenderTests.NoiseExtension()).ToList() };
        for (int i = 0; i < 30; i++) fx.AddNoiseHediff(pawn);
        PawnRenderNode root = RenderTests.SetRenderRoot(pawn);
        MethodInfo capacity = AccessTools.Method(typeof(Patch_MechCustomization_ApplyCapacity), "Postfix");
        MethodInfo schizophrenia = AccessTools.Method(typeof(Patch_Schizophrenia_ConsciousnessFloor), "Postfix");
        MethodInfo providerPrefix = AccessTools.Method(typeof(DMSSR.Patch_PawnCapacity_Calc), "Prefix");
        MethodInfo providerPostfix = AccessTools.Method(typeof(DMSSR.Patch_PawnCapacity_Calc), "Postfix");
        MethodInfo draw = AccessTools.Method(typeof(Patch_EmbryteWeaver_UnfinishedDraw), "Prefix");
        MethodInfo transform = AccessTools.Method(typeof(ModExtension_WingedMech).Assembly.GetType("DMSSR.Mech.HydraFlightRendering"), "TryGetTransform");
        MethodInfo scale = AccessTools.Method(typeof(Patch_SyntheticPresentationScale), "Postfix");
        ThingWithComps item = new();
        string[] names = { "capacity irrelevant", "schizophrenia irrelevant", "dynamic providers absent", "ordinary draw prefix", "non-winged transform", "unscaled root" };
        double[,] best = new double[2, names.Length];
        for (int s = 0; s < 2; s++) for (int i = 0; i < names.Length; i++) best[s, i] = double.MaxValue;
        for (int round = 0; round < 3; round++)
        {
            for (int state = 0; state < 2; state++)
            {
                mod.UnpatchAll(Program.Owner);
                if (state == 1)
                {
                    WhiteGiver.DMSSRStatPathPerfFix.HediffCachePatches.Apply(mod);
                    WhiteGiver.DMSSRStatPathPerfFix.MechLookupPatches.Apply(mod);
                    WhiteGiver.DMSSRStatPathPerfFix.CapacityPatches.Apply(mod);
                    WhiteGiver.DMSSRStatPathPerfFix.RenderPatches.Apply(mod);
                }
                var cap = capacity.CreateDelegate<CapacityPostfix>();
                var sch = schizophrenia.CreateDelegate<SchizophreniaPostfix>();
                var pre = providerPrefix.CreateDelegate<Action>();
                var post = providerPostfix.CreateDelegate<CapacityPostfix>();
                var drawing = draw.CreateDelegate<Func<ThingWithComps, bool>>();
                var matrix = transform.CreateDelegate<Transform>();
                var scaling = scale.CreateDelegate<ScalePostfix>();
                PawnCapacityDef irrelevant = fx.Capacities["Breathing"];
                Action[] actions =
                {
                    () => { float value = 1; cap(pawn.health.hediffSet, irrelevant, ref value); },
                    () => { float value = 1; sch(pawn.health.capacities, irrelevant, ref value); },
                    () => { float value = 1; pre(); post(pawn.health.hediffSet, irrelevant, ref value); },
                    () => _ = drawing(item),
                    () => _ = matrix(pawn, Rot4.North, out _),
                    () => { Vector3 value = Vector3.one; scaling(root, new PawnDrawParms { pawn = pawn }, ref value); },
                };
                for (int i = 0; i < actions.Length; i++) best[state, i] = Math.Min(best[state, i], Measure(actions[i]));
            }
        }
        for (int i = 0; i < names.Length; i++)
            Console.WriteLine($"[perf 1.1.0] {names[i],-27} upstream {best[0, i]:F1} ns -> fixed {best[1, i]:F1} ns ({best[0, i] / best[1, i]:F2}x)");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Measure(Action action)
    {
        for (int i = 0; i < 2000; i++) action();
        const int iterations = 200_000;
        Stopwatch timer = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++) action();
        return timer.Elapsed.TotalNanoseconds / iterations;
    }
}
