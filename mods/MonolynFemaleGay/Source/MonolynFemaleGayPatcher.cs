using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.MonolynFemaleGay;

[StaticConstructorOnStartup]
internal static class MonolynFemaleGayStartup
{
    static MonolynFemaleGayStartup() => MonolynFemaleGayPatcher.Install();
}

internal static class MonolynFemaleGayPatcher
{
    internal const string HarmonyId = "whitegiver.monolyn.femalegay";
    internal const string GeneName = "WG_MonolynFemaleGay";
    private const string Key = "WGMonolynFemaleGay.";

    private static UpstreamContract? upstream;
    private static GeneDef femaleGay = null!;
    private static bool syncing;
    // Null outside the General tab; false inside it if the stock switch was off at entry.
    private static bool? abstinenceBeforeDraw;
    private static MonolynFemaleGaySettings Settings => MonolynFemaleGayMod.Settings!;
    private static bool Enabled => Settings.forceFemaleGay;
    internal static bool Ready => upstream != null;

    internal static string StatusKey => Key + (Enabled ? "StatusFemaleGay"
        : upstream!.Abstinence ? "StatusAbstinence" : "StatusSterile");

    internal static void Install()
    {
        if (Ready) return;
        var harmony = new Harmony(HarmonyId);
        try
        {
            var contract = UpstreamContract.Bind();
            if (MonolynFemaleGayMod.Settings == null)
                throw new InvalidOperationException("Sidecar ModSettings did not load.");
            var def = DefDatabase<GeneDef>.GetNamedSilentFail(GeneName);
            if (def?.geneClass != typeof(Gene_MonolynFemaleGay))
                throw new InvalidOperationException($"Missing or incompatible GeneDef {GeneName}.");

            harmony.Patch(contract.ApplyMethod, prefix: Hook(nameof(BeforeApply)), postfix: Hook(nameof(AfterApply)));
            harmony.Patch(contract.DrawMethod, prefix: Hook(nameof(BeforeDraw)), postfix: Hook(nameof(AfterDraw)),
                finalizer: Hook(nameof(EndDraw)));
            harmony.Patch(contract.GenerateMethod, prefix: Hook(nameof(BeforeGenerate)));
            femaleGay = def;
            upstream = contract;
        }
        catch (Exception e)
        {
            harmony.UnpatchAll(HarmonyId);
            Log.Error($"[{HarmonyId}] Cannot install Monolyn integration (upstream contract or Harmony binding failed): {e}");
            return;
        }

        // Defs must already be correct at the main menu, before generating starting pawns.
        ResolveModes();
        Sync(includePawns: false);
    }

    private static HarmonyMethod Hook(string name) => new(typeof(MonolynFemaleGayPatcher), name);

    private static void ResolveModes()
    {
        if (!Enabled || !upstream!.Abstinence) return;
        if (abstinenceBeforeDraw == false)
        {
            // The stock checkbox calls ApplyGenderPatch *inside* the draw method.
            Settings.forceFemaleGay = false;
            MonolynFemaleGayMod.Instance?.WriteSettings();
            LoadedModManager.GetMod(upstream.ModType)?.WriteSettings();
            return;
        }
        upstream.Abstinence = false;
        LoadedModManager.GetMod(upstream.ModType)?.WriteSettings();
    }

    private static void BeforeApply()
    {
        if (!Ready || syncing) return;
        ResolveModes();
        if (Enabled || Current.Game == null) return;

        // Restore sidecar carriers before ASEL adds HereditaryAbstinence, so it
        // captures their original gender and not an intermediate forced Female.
        syncing = true;
        try
        {
            foreach (Pawn pawn in SnapshotPawns())
                if (pawn?.genes is { } genes)
                    RemoveMatches(genes, genes.GenesListForReading, femaleGay);
        }
        finally { syncing = false; }
    }

    private static void AfterApply()
    {
        if (Ready) Sync(includePawns: true);
    }

    internal static void Sync(bool includePawns)
    {
        if (syncing || upstream is not { } contract) return;
        syncing = true;
        try
        {
            GeneDef desired = Enabled ? femaleGay : contract.Abstinence ? contract.AbstinenceGene : contract.SterileGene;
            List<GeneDef> defs = contract.Xenotype.genes;
            int index = defs.FindIndex(d => d == femaleGay || d == contract.AbstinenceGene || d == contract.SterileGene);
            defs.RemoveAll(d => d == femaleGay || d == contract.AbstinenceGene || d == contract.SterileGene);
            defs.Insert(index == -1 ? defs.Count : index, desired);

            if (!includePawns || Current.Game == null) return;
            foreach (Pawn pawn in SnapshotPawns())
            {
                if (pawn?.genes is not { } genes) continue;
                if (!Enabled) RemoveMatches(genes, genes.GenesListForReading, femaleGay);
                if (genes.Xenotype == contract.Xenotype)
                {
                    if (desired != contract.AbstinenceGene) RemoveMatches(genes, genes.Endogenes, contract.AbstinenceGene);
                    if (desired != contract.SterileGene) RemoveMatches(genes, genes.Endogenes, contract.SterileGene);
                    if (genes.GetGene(desired) == null) genes.AddGene(desired, xenogene: false);
                }
                if (Enabled && genes.GetGene(femaleGay) is Gene_MonolynFemaleGay carried)
                    carried.EnsureFemale();
            }
        }
        finally { syncing = false; }
    }

    private static void RemoveMatches(Pawn_GeneTracker tracker, List<Gene> genes, GeneDef def)
    {
        for (int i = genes.Count - 1; i >= 0; i--)
            if (genes[i].def == def) tracker.RemoveGene(genes[i]);
    }

    private static HashSet<Pawn> SnapshotPawns()
    {
        var pawns = new HashSet<Pawn>(PawnsFinder.All_AliveOrDead);
        // Starting/optional pawns can be held solely in the new-game selection list.
        if (Current.Game.InitData?.startingAndOptionalPawns is { } startingPawns)
            pawns.UnionWith(startingPawns);
        return pawns;
    }

    private static void BeforeDraw(out bool? __state)
    {
        __state = abstinenceBeforeDraw;
        abstinenceBeforeDraw = upstream?.Abstinence;
    }

    private static void AfterDraw(Listing_Standard __0)
    {
        if (!Ready) return;
        // Fallback if another patch blocked the stock draw's nested ApplyGenderPatch call.
        if (abstinenceBeforeDraw == false && upstream!.Abstinence && Enabled)
            upstream.Apply();
        DrawToggle(__0);
    }

    // Also restores draw scope if another mod's hook throws. Does not suppress exceptions.
    private static void EndDraw(bool? __state) => abstinenceBeforeDraw = __state;

    internal static void DrawToggle(Listing_Standard listing)
    {
        bool old = Enabled;
        listing.CheckboxLabeled((Key + "EnabledLabel").Translate(), ref Settings.forceFemaleGay,
            (Key + "EnabledTooltip").Translate());
        if (old == Enabled) return;
        // A click on our switch wins over the previously selected stock mode.
        if (Enabled && upstream!.Abstinence)
        {
            upstream.Abstinence = false;
            LoadedModManager.GetMod(upstream.ModType)?.WriteSettings();
        }
        MonolynFemaleGayMod.Instance?.WriteSettings();
        upstream!.Apply();
    }

    private static void BeforeGenerate(ref PawnGenerationRequest __0)
    {
        if (upstream is not { } contract || !Enabled || __0.FixedGender.HasValue) return;
        PawnKindDef? kind = __0.KindDef;
        if (kind?.race != contract.Race && !IsFixedMonolyn(__0, kind, contract.Xenotype)) return;
        __0.FixedGender = Gender.Female;
        __0.AllowGay = true;
    }

    private static bool IsFixedMonolyn(PawnGenerationRequest request, PawnKindDef? kind, XenotypeDef xenotype)
    {
        if (request.ForcedXenotype != null) return request.ForcedXenotype == xenotype;
        if (request.ForcedCustomXenotype != null || request.ForceBaselinerChance > 0f || (request.AllowedXenotypes?.Count ?? 0) > 0
            || kind?.xenotypeSet is not { Count: 1 } set || set[0].xenotype != xenotype || set[0].chance < 1f)
            return false;
        if (request.MustBeCapableOfViolence && !xenotype.canGenerateAsCombatant) return false;
        // A faction or its ideology can add other candidates to a kind's set.
        foreach (var chance in PawnGenerator.XenotypesAvailableFor(kind, faction: request.Faction))
            if (chance.Key != xenotype && chance.Value > 0f) return false;
        return true;
    }
}
