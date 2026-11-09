using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace MjRimMods.WhileYouAreNearby;

// Preserve the original class and Scribe keys so existing settings remain readable.
public sealed class SimpleSettings : ModSettings
{
    public bool debug;
    public int longDistanceThreshold = 32;
    public int maxSearchRadius = 12;
    public int priorityBoost = 1;
    public int vanillaChancePercentage = 5;
    public WorkTypeSelection excludedLongDistanceWorkTypes = new("ExcludedLongDistanceWorkTypes");
    public WorkTypeSelection excludedNearbyWorkTypes = new("ExcludedNearbyWorkTypes");

    public override void ExposeData()
    {
        Scribe_Values.Look(ref debug, "Debug", false);
        Scribe_Values.Look(ref longDistanceThreshold, "longDistanceThreshold", 32);
        Scribe_Values.Look(ref maxSearchRadius, "maxSearchRadius", 12);
        Scribe_Values.Look(ref priorityBoost, "priorityBoost", 1);
        Scribe_Values.Look(ref vanillaChancePercentage, "vanillaChancePercentage", 5);
        excludedLongDistanceWorkTypes.ExposeData();
        excludedNearbyWorkTypes.ExposeData();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            longDistanceThreshold = Math.Clamp(longDistanceThreshold, 1, 999);
            maxSearchRadius = Math.Clamp(maxSearchRadius, 1, 999);
            priorityBoost = Math.Clamp(priorityBoost, 0, 999);
            vanillaChancePercentage = Math.Clamp(vanillaChancePercentage, 0, 100);
        }
    }

    public void Draw(Rect inRect)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect.ContractedBy(10f));
        listing.Label("BasicSettings".Translate());
        listing.GapLine();
        SimpleListing.IntegerInput(listing, ref longDistanceThreshold, "LongDistanceThreshold", 1);
        SimpleListing.IntegerInput(listing, ref maxSearchRadius, "MaxSearchRadius", 1);
        SimpleListing.IntegerInput(listing, ref priorityBoost, "PriorityBoost");
        SimpleListing.IntegerInput(listing, ref vanillaChancePercentage, "VanillaChancePercentage", 0, 100, "%");
        listing.GapLine();
        listing.Label("AdvancedSettings".Translate());
        excludedLongDistanceWorkTypes.Draw(listing);
        excludedNearbyWorkTypes.Draw(listing);
        if (Prefs.DevMode)
        {
            listing.GapLine();
            listing.CheckboxLabeled("DebugLog".Translate(), ref debug, "DebugLogTooltips".Translate());
        }
        listing.End();
    }
}

public sealed class WorkTypeSelection(string name)
{
    private string Label => name.Translate();
    public bool Enabled;
    public HashSet<WorkTypeDef> Defs = new();

    public bool Contains(WorkTypeDef def) => Enabled && Defs.Contains(def);

    public void ExposeData()
    {
        Scribe_Values.Look(ref Enabled, name + "SavedEnabled", false);
        Scribe_Collections.Look(ref Defs, name + "SavedDefs", LookMode.Def);
        Defs ??= new HashSet<WorkTypeDef>();
    }

    public void Draw(Listing_Standard listing)
    {
        listing.Gap(6f);
        listing.CheckboxLabeled(name.Translate(), ref Enabled, (name + "Tooltips").Translate());
        if (Enabled && listing.ButtonText("ClickPlusToEdit".Translate()))
        {
            Find.WindowStack.Add(new SelectionDialog(this));
        }
    }

    private sealed class SelectionDialog : Window
    {
        private readonly WorkTypeSelection selection;
        private Vector2 scrollPosition;

        public SelectionDialog(WorkTypeSelection selection)
        {
            this.selection = selection;
            optionalTitle = selection.Label;
            forcePause = true;
            doCloseButton = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            List<WorkTypeDef> defs = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            Rect outRect = inRect.TopPartPixels(inRect.height - 55f);
            var viewRect = new Rect(0f, 0f, outRect.width - 20f, defs.Count * 36f);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            var listing = new Listing_Standard();
            listing.Begin(viewRect);
            foreach (WorkTypeDef def in defs)
            {
                bool selected = selection.Defs.Contains(def);
                bool value = selected;
                listing.CheckboxLabeled(def.labelShort, ref value, def.description);
                if (value && !selected) selection.Defs.Add(def);
                else if (!value && selected) selection.Defs.Remove(def);
            }
            listing.End();
            Widgets.EndScrollView();
        }
    }
}
