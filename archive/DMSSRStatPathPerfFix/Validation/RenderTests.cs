using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using DMSSR.Content;
using DMSSR.Mech;
using DMSSR.Patches;
using DMSSR.Production;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using WhiteGiver.DMSSRStatPathPerfFix;

namespace DMSSRStatPathPerfFix.Validation;

internal static class RenderTests
{
    internal sealed class NoiseExtension : DefModExtension { }
    private static int checks;
    internal static int CheckCount => checks;

    internal static void Run(Fixture fx, bool fixedMode)
    {
        Console.WriteLine($"== {(fixedMode ? "GREEN" : "RED")} 1.1.0 drawing hotspots ==");
        ThingWithComps ordinary = new() { def = new ThingDef { defName = "ValidationItem" } };
        foreach (Type type in new[] { typeof(Patch_EmbryteWeaver_UnfinishedDraw), typeof(Patch_EmbryteWeaver_UnfinishedPrint) })
        {
            Probe.Reset();
            MethodInfo prefix = AccessTools.Method(type, "Prefix");
            for (int i = 0; i < 100; i++) Check((bool)prefix.Invoke(null, new object[] { ordinary })!, "ordinary thing suppressed");
            Check(Probe.DisplayLookups == (fixedMode ? 0 : 100), "ordinary thing enters display-owner lookup");
            UnfinishedThing unfinished = new() { def = ordinary.def };
            Check((bool)prefix.Invoke(null, new object[] { unfinished })!, "unspawned UFT suppressed");
            TestUnfinishedOwner(fx, prefix);
        }
        MethodInfo transform = AccessTools.Method(typeof(ModExtension_WingedMech).Assembly.GetType("DMSSR.Mech.HydraFlightRendering"), "TryGetTransform");
        Pawn pawn = fx.NewPawn(fx.HumanDef, new IntVec3(2, 0, 2));
        pawn.def = new ThingDef { defName = "ValidationNonWinged", race = fx.HumanDef.race,
            modExtensions = Enumerable.Range(0, 30).Select(_ => (DefModExtension)new NoiseExtension()).ToList() };
        int initialWingedBuilds = RenderPatches.WingedBuilds;
        Probe.Reset();
        for (int i = 0; i < 100; i++)
        {
            object?[] args = { pawn, Rot4.North, null };
            Check(!(bool)transform.Invoke(null, args)!, "non-winged pawn has transform");
            Check(((Matrix4x4)args[2]!).Equals(Matrix4x4.identity), "non-winged matrix changed");
        }
        Check(Probe.WingedLookups == (fixedMode ? 0 : 100), $"winged extension not cached: {Probe.WingedLookups}");
        if (fixedMode) Check(RenderPatches.WingedBuilds == initialWingedBuilds + 1, "negative winged cache did not fill exactly once");

        PawnRenderNode node = SetRenderRoot(pawn);
        MethodInfo scale = AccessTools.Method(typeof(Patch_SyntheticPresentationScale), "Postfix");
        int initialSyntheticBuilds = RenderPatches.SyntheticBuilds;
        Probe.Reset();
        for (int i = 0; i < 100; i++)
        {
            object?[] args = { node, new PawnDrawParms { pawn = pawn }, Vector3.one };
            scale.Invoke(null, args);
            Check(((Vector3)args[2]!).Equals(Vector3.one), "unscaled pawn changed");
        }
        Check(Probe.SyntheticLookups == (fixedMode ? 0 : 100), "synthetic extension not cached");
        if (fixedMode) Check(RenderPatches.SyntheticBuilds == initialSyntheticBuilds + 1, "negative synthetic cache did not fill exactly once");
        // Positive case: real postfix arithmetic, including root-only application.
        pawn.def = new ThingDef { defName = "ValidationScaled", race = fx.HumanDef.race,
            modExtensions = new List<DefModExtension> { new ModExtension_SyntheticPresentation { renderScale = 1.3f } } };
        object?[] positive = { node, new PawnDrawParms { pawn = pawn }, new Vector3(2f, 3f, 4f) };
        scale.Invoke(null, positive);
        Check(((Vector3)positive[2]!).Equals(new Vector3(2f, 3f, 4f) * 1.3f), "positive scale differs");
        positive[0] = RuntimeHelpers.GetUninitializedObject(typeof(PawnRenderNode));
        positive[2] = Vector3.one;
        scale.Invoke(null, positive);
        Check(((Vector3)positive[2]!).Equals(Vector3.one), "child node was scaled");
        HydraAndParallel(fx, transform, scale, fixedMode);
        Console.WriteLine($"PASS drawing hotspots: {checks} assertions.");
    }

    internal static PawnRenderNode SetRenderRoot(Pawn pawn)
    {
        Pawn_DrawTracker drawer = (Pawn_DrawTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_DrawTracker));
        PawnRenderer renderer = (PawnRenderer)RuntimeHelpers.GetUninitializedObject(typeof(PawnRenderer));
        PawnRenderTree tree = (PawnRenderTree)RuntimeHelpers.GetUninitializedObject(typeof(PawnRenderTree));
        PawnRenderNode node = (PawnRenderNode)RuntimeHelpers.GetUninitializedObject(typeof(PawnRenderNode));
        drawer.renderer = renderer;
        renderer.renderTree = tree;
        tree.rootNode = node;
        AccessTools.Field(typeof(Pawn), "drawer").SetValue(pawn, drawer);
        return node;
    }

    private static void TestUnfinishedOwner(Fixture fx, MethodInfo prefix)
    {
        Building_HumanoidModification owner = (Building_HumanoidModification)RuntimeHelpers.GetUninitializedObject(typeof(Building_HumanoidModification));
        owner.def = new ThingDef { defName = "ValidationWeaver", size = new IntVec2(3, 3) };
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(owner, (sbyte)0);
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(owner, new IntVec3(4, 0, 4));
        BillStack stack = new(owner);
        Bill_ProductionWithUft bill = new();
        stack.Bills.Add(bill);
        bill.billStack = stack;
        UnfinishedThing unfinished = new() { def = new ThingDef { defName = "ValidationUft" } };
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(unfinished, (sbyte)0);
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(unfinished, owner.Position);
        unfinished.BoundBill = bill;
        Check(Building_HumanoidModification.TryGetUnfinishedDisplayOwner(unfinished, out var actual) && actual == owner, "real positive display owner fixture");
        Check(!(bool)prefix.Invoke(null, new object[] { unfinished })!, "bound inside UFT should be suppressed");
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(unfinished, new IntVec3(8, 0, 8));
        Check((bool)prefix.Invoke(null, new object[] { unfinished })!, "outside UFT should draw");
        AccessTools.Field(typeof(Thing), "positionInt").SetValue(unfinished, owner.Position);
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(owner, (sbyte)-1);
        Check((bool)prefix.Invoke(null, new object[] { unfinished })!, "despawned owner should not hide UFT");
        unfinished.BoundBill = null;
        Check((bool)prefix.Invoke(null, new object[] { unfinished })!, "unbound UFT should draw");
    }

    private static void HydraAndParallel(Fixture fx, MethodInfo transform, MethodInfo scale, bool fixedMode)
    {
        Type hydra = transform.DeclaringType!;
        Pawn pawn = fx.NewPawn(fx.HumanDef, new IntVec3(2, 0, 2));
        PawnRenderNode root = SetRenderRoot(pawn);
        var extension = new ModExtension_WingedMech { flightBankAngle = 20f, groundedDrawOffset = 0.4f };
        pawn.def = new ThingDef { defName = "ValidationWinged", race = fx.HumanDef.race,
            modExtensions = new List<DefModExtension> { extension } };
        // Leaf Unity draw position is absent offline. The complete transform body and state cache
        // remain real; the grounded branch follows missing wings, exactly as in a real pawn.
        Harmony qa = new(Program.ValidationOwner);
        qa.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.DrawPos)),
            prefix: new HarmonyMethod(typeof(RenderTests), nameof(DrawPosition)));
        object?[] args = { pawn, Rot4.East, null };
        Check((bool)transform.Invoke(null, args)!, "grounded winged pawn missing offset");
        Check(((Matrix4x4)args[2]!).Equals(Matrix4x4.Translate(new Vector3(0, 0, 0.4f))), "grounded matrix differs");
        // Banked positive branch with real visual state (current tick) to avoid a Unity mesh graph.
        object states = AccessTools.Field(hydra, "States").GetValue(null)!;
        object state = states.GetType().GetMethod("GetOrCreateValue")!.Invoke(states, new object[] { pawn })!;
        Matrix4x4 banked = Matrix4x4.Translate(new Vector3(3f, 2f, 1f));
        AccessTools.Field(state.GetType(), "transform").SetValue(state, banked);
        AccessTools.Field(state.GetType(), "changed").SetValue(state, true);
        args = new object?[] { pawn, Rot4.East, null };
        Check((bool)transform.Invoke(null, args)! && ((Matrix4x4)args[2]!).Equals(banked), "positive cached banking transform changed");
        // An unspawned passenger resolves its carrier BEFORE extension lookup. Never gate the
        // passenger's own def: a non-winged passenger inherits a winged carrier's transform.
        Pawn passenger = fx.NewPawn(fx.HumanDef, new IntVec3(2, 0, 2));
        AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(passenger, (sbyte)-1);
        Type mounted = typeof(ModExtension_WingedMech).Assembly.GetType("DMSSR.Mech.MechCarrierMountedSystems", true)!;
        object carriers = AccessTools.Field(mounted, "MountedPassengerCarriers").GetValue(null)!;
        DMSSR.Mech.CompMechCarrier carrier = new() { parent = pawn };
        carriers.GetType().GetMethod("Add")!.Invoke(carriers, new object[] { passenger, carrier });
        args = new object?[] { passenger, Rot4.East, null };
        Check((bool)transform.Invoke(null, args)! && ((Matrix4x4)args[2]!).Equals(banked), "mounted passenger transform lost");
        // Real PawnRenderTree.TryGetMatrix (empty ancestor chain) dispatches through DMSSR's
        // registered render postfix. Its portrait gate must suppress the inherited transform.
        PawnRenderTree tree = pawn.Drawer.renderer.renderTree;
        AccessTools.Field(typeof(PawnRenderNode), "props").SetValue(root, new PawnRenderNodeProperties());
        root.tree = tree;
        root.debugScale = 1f;
        AccessTools.Field(typeof(PawnRenderTree), "nodeAncestors").SetValue(tree,
            new Dictionary<PawnRenderNode, List<PawnRenderNode>> { [root] = new() });
        if (!fixedMode)
            qa.CreateClassProcessor(typeof(ModExtension_WingedMech).Assembly.GetType("DMSSR.Mech.Patch_HydraFlightRenderMatrix", true)!).Patch();
        PawnDrawParms parms = new() { pawn = passenger, facing = Rot4.East, matrix = Matrix4x4.identity };
        float altitude = root.Worker.AltitudeFor(root, parms);
        Matrix4x4 native = altitude == 0 ? Matrix4x4.identity : Matrix4x4.Translate(Vector3.up * altitude);
        Check(tree.TryGetMatrix(root, parms, out Matrix4x4 real) && real.Equals(banked * native), "real TryGetMatrix integration");
        parms.flags = PawnRenderFlags.Portrait;
        Check(tree.TryGetMatrix(root, parms, out real) && real.Equals(native), "portrait Hydra render gate");
        // Real ScaleFor base method can run without a Unity scene in portrait mode.
        pawn.def = new ThingDef { defName = "ValidationRealScale", race = fx.HumanDef.race,
            modExtensions = new() { new ModExtension_SyntheticPresentation { renderScale = 1.3f } } };
        parms.pawn = pawn;
        if (!fixedMode) qa.CreateClassProcessor(typeof(Patch_SyntheticPresentationScale)).Patch();
        Check(root.Worker.ScaleFor(root, parms).Equals(Vector3.one * 1.3f), "real ScaleFor integration");
        // Restore winged definition for mounted/concurrent checks below.
        pawn.def = new ThingDef { defName = "ValidationWingedRestored", race = fx.HumanDef.race,
            modExtensions = new() { extension } };
        if (fixedMode)
        {
            Check(ReferenceEquals(RenderPatches.GetWinged(pawn.def), extension), "winged extension identity");
            int builds = RenderPatches.WingedBuilds;
            Probe.Reset();
            var errors = new ConcurrentQueue<string>();
            Parallel.For(0, 16, _ =>
            {
                for (int i = 0; i < 10_000; i++)
                {
                    object?[] local = { passenger, Rot4.East, null };
                    if (!(bool)transform.Invoke(null, local)! || !((Matrix4x4)local[2]!).Equals(banked)) errors.Enqueue("matrix");
                }
            });
            Check(errors.IsEmpty && RenderPatches.WingedBuilds == builds && Probe.WingedLookups == 0, "parallel mounted extension cache");
            ThingDef def = new() { defName = "ValidationParallelNegative", race = fx.HumanDef.race,
                modExtensions = Enumerable.Range(0, 30).Select(_ => (DefModExtension)new NoiseExtension()).ToList() };
            Pawn[] pawns = Enumerable.Range(0, 16).Select(_ => fx.NewPawn(def, new IntVec3(2, 0, 2))).ToArray();
            PawnRenderNode[] roots = pawns.Select(SetRenderRoot).ToArray();
            Parallel.For(0, 16, index =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    object?[] matrix = { pawns[index], Rot4.North, null };
                    if ((bool)transform.Invoke(null, matrix)!) errors.Enqueue("negative transform");
                    object?[] vector = { roots[index], new PawnDrawParms { pawn = pawns[index] }, Vector3.one };
                    scale.Invoke(null, vector);
                    if (!((Vector3)vector[2]!).Equals(Vector3.one)) errors.Enqueue("negative scale");
                }
            });
            Check(errors.IsEmpty, "parallel cold negative definition caches");
            var synthetic = new ModExtension_SyntheticPresentation { renderScale = 1.25f };
            def = new ThingDef { defName = "ValidationLiveScale", race = fx.HumanDef.race, modExtensions = new() { synthetic } };
            pawn.def = def;
            object?[] vectorArgs = { root, new PawnDrawParms { pawn = pawn }, Vector3.one };
            scale.Invoke(null, vectorArgs);
            Check(((Vector3)vectorArgs[2]!).Equals(Vector3.one * 1.25f), "positive cached scale");
            synthetic.renderScale = 0.75f;
            vectorArgs[2] = Vector3.one;
            scale.Invoke(null, vectorArgs);
            Check(((Vector3)vectorArgs[2]!).Equals(Vector3.one * 0.75f), "scale field frozen in cache");
        }
    }

    private static bool DrawPosition(ref Vector3 __result) { __result = Vector3.zero; return false; }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        checks++;
    }
}
