using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Meidocho.CEFix.MiliraFloatUnitElevation;

/// <summary>
/// BASELINE (2026-07-15): Before this change the mod performed zero automatic-target
/// filtering.  Milira float units were freely eligible for every auto-target finder in
/// every faction.  This enum, its persisted setting, and FloatUnitAutoTargetHelper are
/// the first policy infrastructure; downstream Harmony patches will consult the helper
/// to decide whether a candidate should be skipped during automatic acquisition.
/// Manual player attacks and forced turret targets remain unaffected at all times.
/// </summary>
public enum FloatUnitAutoTargetPolicy
{
    /// <summary>
    /// Only player-controlled searchers (Faction == Faction.OfPlayer) skip Milira
    /// float units during automatic targeting.
    /// </summary>
    PlayerOnly,

    /// <summary>
    /// Every faction — player and AI alike — skips Milira float units during
    /// automatic targeting.
    /// </summary>
    AllFactions,

    /// <summary>
    /// No automatic-target restriction.  All auto-target finders may select float
    /// units freely.  This matches the pre-patch (unfiltered) behaviour.
    /// </summary>
    AllowAll
}

// ── persisted settings ────────────────────────────────────────────────
public class CEFixMiliraModSettings : ModSettings
{
    public FloatUnitAutoTargetPolicy AutoTargetPolicy =
        FloatUnitAutoTargetPolicy.PlayerOnly;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref AutoTargetPolicy, "autoTargetPolicy",
            FloatUnitAutoTargetPolicy.PlayerOnly);
    }
}

// ── Mod entry-point ───────────────────────────────────────────────────
public class CEFixMiliraMod : Mod
{
    public static CEFixMiliraMod? Instance { get; private set; }

    internal readonly CEFixMiliraModSettings settings;

    public CEFixMiliraMod(ModContentPack content) : base(content)
    {
        Instance = this;
        settings = GetSettings<CEFixMiliraModSettings>();
    }

    // ── settings tab ───────────────────────────────────────────────────
    public override string SettingsCategory() => "CE Fix: Milira Float Unit";

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);

        Text.Font = GameFont.Medium;
        listing.Label("Auto-Target Policy");
        Text.Font = GameFont.Small;

        listing.GapLine();

        listing.Label(
            "Controls which factions' automatic targeting may select Milira " +
            "float-unit pawns.  Manual player attacks and forced turret targets " +
            "are never restricted by this setting.");

        listing.Gap();

        DrawRadio(ref listing, "Player only (recommended)",
            FloatUnitAutoTargetPolicy.PlayerOnly,
            "Only player-controlled pawns and player turrets avoid auto-targeting float units.  Enemy AI may still select them.");

        DrawRadio(ref listing, "All factions",
            FloatUnitAutoTargetPolicy.AllFactions,
            "Every faction — including hostile AI — avoids auto-targeting float units.  Float units are never chosen by automatic target finders.");

        DrawRadio(ref listing, "Allow all",
            FloatUnitAutoTargetPolicy.AllowAll,
            "No restriction — all automatic target finders may select float units (original pre-patch behaviour).");

        listing.End();
    }

    private void DrawRadio(ref Listing_Standard listing, string label,
        FloatUnitAutoTargetPolicy value, string tooltip)
    {
        bool selected = settings.AutoTargetPolicy == value;
        Rect row = listing.GetRect(Text.LineHeight + 4f);
        TooltipHandler.TipRegion(row, tooltip);

        if (Widgets.RadioButtonLabeled(row, label, selected))
        {
            settings.AutoTargetPolicy = value;
        }
    }
}

// ── policy helper (public, callable from Harmony patches) ──────────────
public static class FloatUnitAutoTargetHelper
{
    private const string FloatUnitBodyDef = "Milira_FloatUnit";

    /// <summary>
    /// Returns <c>true</c> when <paramref name="target"/> is a Milira float unit
    /// and the current <see cref="FloatUnitAutoTargetPolicy"/> says the
    /// <paramref name="searcher"/> should skip it during automatic acquisition.
    /// Always returns <c>false</c> for non-float-unit pawns, non-pawn targets,
    /// or when policy is <see cref="FloatUnitAutoTargetPolicy.AllowAll"/>.
    /// </summary>
    public static bool ShouldSkipFloatUnit(
        IAttackTargetSearcher searcher, Thing target)
    {
        // Fast-path: only Pawn with the exact float-unit body
        if (target is not Pawn pawn
            || pawn.RaceProps?.body?.defName != FloatUnitBodyDef)
        {
            return false;
        }

        var policy = CEFixMiliraMod.Instance?.settings?.AutoTargetPolicy
                     ?? FloatUnitAutoTargetPolicy.PlayerOnly;

        return policy switch
        {
            FloatUnitAutoTargetPolicy.PlayerOnly
                => searcher.Thing.Faction == Faction.OfPlayer,

            FloatUnitAutoTargetPolicy.AllFactions => true,

            FloatUnitAutoTargetPolicy.AllowAll => false,

            _ => searcher.Thing.Faction == Faction.OfPlayer
        };
    }
}
