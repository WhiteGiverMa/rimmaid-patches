using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;
using WhiteGiver.DMSMechStorage;

internal static class InfoCardRegression
{
    private static readonly MethodInfo DescriptionEntry = AccessTools.DeclaredMethod(
        typeof(StatsReportUtility), "DescriptionEntry", new[] { typeof(Thing) });

    internal static void Run()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        Driver.Check(MechStorage.Store(actor, mech), "Info-card setup could not seal the mech.");
        var capsule = (Building_MechCapsule)mech.holdingOwner.Owner;
        StatDrawEntry entry = Description(capsule);
        Dialog_InfoCard.Hyperlink[] links = Links(entry, capsule);
        Driver.Check(links.Length == 1 && links[0].thing == mech && links[0].def == null,
            "Description did not link to the actual stored pawn instead of its race definition.");
        Driver.Check(entry.category == StatCategoryDefOf.BasicsImportant && entry.LabelCap == "Description"
            && entry.ValueString == "" && entry.DisplayPriorityWithinCategory == 99999
            && entry.overridesHideStats && entry.ShouldDisplay(capsule)
            && entry.GetExplanationText(StatRequest.For(capsule)) == capsule.DescriptionFlavor,
            "Adding the pawn hyperlink changed the native description entry.");
        Driver.Check(Links(entry, capsule).Length == 1 && Links(Description(capsule), capsule).Length == 1,
            "Re-enumerating or reopening the description duplicated its pawn hyperlink.");
        Driver.Check(!links[0].IsHidden && links[0].Label == mech.Label && links[0].selectedStatIndex == -1,
            "The native pawn hyperlink label or default destination changed.");
        mech.Name = new NameSingle("Renamed stored mech");
        Driver.Check(links[0].Label == mech.Label, "The pawn hyperlink did not follow the stored pawn's rename.");

        var originalLinks = capsule.def.descriptionHyperlinks;
        capsule.def.descriptionHyperlinks = new List<DefHyperlink> { new(mech.def) };
        try
        {
            links = Links(Description(capsule), capsule);
            Driver.Check(links.Length == 2 && links[0].def == mech.def && links[0].thing == null
                && links[1].thing == mech && links[1].def == null
                && capsule.def.descriptionHyperlinks.Count == 1,
                "Appending the pawn hyperlink replaced an existing link or mutated the shared definition.");
        }
        finally
        {
            capsule.def.descriptionHyperlinks = originalLinks;
        }

        var enemy = new Faction { def = new FactionDef { defName = "InfoCardEnemy" } };
        Driver.Set(typeof(Thing), "factionInt", capsule, enemy);
        Driver.Check(Links(Description(capsule), capsule).Length == 0, "A non-player capsule received a pawn hyperlink.");
        Driver.Set(typeof(Thing), "factionInt", capsule, Driver.Player);
        Driver.Set(typeof(Thing), "factionInt", mech, enemy);
        Driver.Check(Links(Description(capsule), capsule).Length == 0, "A non-player stored pawn received a hyperlink.");
        Driver.Set(typeof(Thing), "factionInt", mech, Driver.Player);

        Pawn foreign = Driver.NewPawn("other.mod", at: new IntVec3(18, 0, 18));
        Building_MechCapsule foreignCapsule = MechCapsuleUtility.DeactivateMech(foreign);
        Driver.Check(Links(Description(foreignCapsule), foreignCapsule).Length == 0,
            "A non-DMS capsule received the DMS pawn hyperlink.");
        var empty = new Building_MechCapsule { def = capsule.def };
        empty.SetFaction(Driver.Player);
        Driver.Check(Links(Description(empty), empty).Length == 0, "An empty capsule received a pawn hyperlink.");
        var unrelated = new Thing { def = new ThingDef { description = "Unrelated description." } };
        Driver.Check(Links(Description(unrelated), unrelated).Length == 0,
            "An unrelated thing received the DMS pawn hyperlink.");

        MinifiedThing mini = capsule.MakeMinified();
        Driver.Check(mini.InnerThing == capsule && !capsule.Spawned
            && Links(Description(mini.InnerThing), capsule).Single().thing == mech,
            "The unspawned inner capsule lost its pawn hyperlink after native minification.");
        Pawn_EquipmentTracker equipment = mech.equipment;
        mech.equipment = null!;
        Driver.Check(Links(Description(capsule), capsule).Single().thing == mech,
            "The info-only hyperlink incorrectly required an equipment tracker.");
        mech.equipment = equipment;
        Driver.Check(capsule.Mech == mech && mech.holdingOwner == capsule.innerContainer
            && capsule.innerContainer.Count == 1 && !mech.Spawned,
            "Reading the description changed the stored pawn or container ownership.");
        capsule.innerContainer.Remove(mech);
        Driver.Check(Links(Description(capsule), capsule).Length == 0,
            "An emptied capsule retained a stale pawn hyperlink when reopened.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: real description factory links the exact stored pawn, preserves text/existing links, follows renames and native minification; no duplicate links, equipment dependency or scope leakage.");
    }

    private static StatDrawEntry Description(Thing thing) =>
        (StatDrawEntry)DescriptionEntry.Invoke(null, new object[] { thing })!;

    private static Dialog_InfoCard.Hyperlink[] Links(StatDrawEntry entry, Thing thing) =>
        entry.GetHyperlinks(StatRequest.For(thing))?.ToArray() ?? Array.Empty<Dialog_InfoCard.Hyperlink>();
}
