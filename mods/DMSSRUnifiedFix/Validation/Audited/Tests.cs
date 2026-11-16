using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DMSSR.Abilities;
using DMSSR.AI;
using DMSSR.Gene;
using DMSSR.Hediffs;
using DMSSR.Jobs;
using DMSSR.Patches;
using DMSSR.UI;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using Verse.AI;
using WhiteGiver.DMSSRUnifiedFix;
using WhiteGiver.DMSSRUnifiedFix.Audited;

namespace DMSSRUnifiedFix.Validation.Audited;

internal static class Tests
{
    private static readonly BindingFlags Flags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Harmony Harness = new(Program.ValidationOwner + ".audited");
    private static Fixture fx = null!;
    private static int assertions;
    private static int nextItemId = 5000;
    private static bool killed;

    internal static void Run(string mode)
    {
        fx = Fixture.Create();
        LoadedLanguage language = new("English")
        {
            info = new LanguageInfo { languageWorkerClass = typeof(LanguageWorker_English) },
        };
        LanguageDatabase.activeLanguage = language;
        LanguageDatabase.defaultLanguage = language;
        bool green = mode != "red";

        Harness.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.DeSpawnOrDeselect)),
            prefix: new HarmonyMethod(typeof(Tests), nameof(SkipDeSpawnOrDeselect)));
        Harness.Patch(AccessTools.Method(typeof(Thing), nameof(Thing.Destroy), new[] { typeof(DestroyMode) }),
            prefix: new HarmonyMethod(typeof(Tests), nameof(DestroyPrefix)));
        foreach (MethodInfo mote in typeof(MoteMaker).GetMethods(Flags).Where(m => m.Name == "ThrowText"))
        {
            Harness.Patch(mote, prefix: new HarmonyMethod(typeof(Tests), nameof(SkipDrop)));
        }
        Harness.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.DrawPos)),
            prefix: new HarmonyMethod(typeof(Tests), nameof(DrawPosPrefix)));
        Harness.Patch(AccessTools.Method(
                typeof(Pawn_CarryTracker),
                "TryDropCarriedThing",
                new[] { typeof(IntVec3), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>) }),
            prefix: new HarmonyMethod(typeof(Tests), nameof(SkipDrop)));
        ApplyUpstreamDeathCheatPatch();
        ApplyUpstreamFacechangePatches();
        if (green)
        {
            DMSSRUnifiedFixMod.ApplyPatches();
        }

        Check(DeathCheatProtocolFix.Installed == green, "PR protocol subsystem install state");
        Check(SyntheticBionicRestoreFix.Installed == green, "synthetic bionic subsystem install state");
        Check(RemoteControlFix.Installed == green, "remote control subsystem install state");
        Check(CascadeControlFix.Installed == green, "cascade link subsystem install state");
        Check(PowerChargeFix.Installed == green, "power charge subsystem install state");
        Check(CephalopodaThrowFix.Installed == green, "cephalopoda throw subsystem install state");
        Check(EmergencyTendFix.Installed == green, "emergency tend subsystem install state");
        Check(UndyingArmorFix.Installed == green, "undying armor subsystem install state");
        Check(MechaniteRecognitionFix.Installed == green, "mechanite recognition subsystem install state");
        Check(GeneFittingPodFix.Installed == green, "gene fitting pod subsystem install state");
        Check(FacedancerControlFix.Installed == green, "facedancer gate subsystem install state");

        DeathCheat(green);
        SyntheticBionic(green);
        ModificationPartDefaults(green);
        RemoteControl(green);
        CascadeExpiry(green);
        PowerCharge(green);
        CephalopodaThrow(green);
        EmergencyTend(green);
        UndyingArmor(green);
        MechaniteRecognition(green);
        GeneFittingPod(green);
        FacedancerGate(green);
        XmlPatchWiring();

        Console.WriteLine($"PASS AUDITED {mode.ToUpperInvariant()}: {assertions} assertions.");
    }

    // ----------------------------------------------------------------- A1

    private static void DeathCheat(bool green)
    {
        Pawn mech = fx.NewMech(new IntVec3(4, 0, 4));
        ThingWithComps item = NewItem(fx.CerebellumItem);

        bool first = ModificationForPawn(mech, item, out string firstReason);
        Hediff? installed = mech.health.hediffSet.GetFirstHediffOfDef(fx.DeathCheat);
        HediffComp_DeathCheatCharges? charges =
            installed == null ? null : HediffUtility.TryGetComp<HediffComp_DeathCheatCharges>(installed);
        Check(first && installed != null && charges?.charges == 1, "first PR protocol install adds one charge: " + firstReason);
        Check(installed?.Part == fx.Part("Torso"), "first PR protocol install is attached to the core part");

        bool second = ModificationForPawn(mech, item, out string secondReason);
        HediffComp_Modification? modification =
            installed == null ? null : HediffUtility.TryGetComp<HediffComp_Modification>(installed);
        int count = mech.health.hediffSet.hediffs.Count(h => h.def == fx.DeathCheat);
        if (green)
        {
            Check(second, "second PR protocol install succeeds: " + secondReason);
            Check(charges?.charges == 2, "second PR protocol install adds the second charge");
            Check(count == 1 && modification?.InstalledCount == 2, "second install merges into one visible modification");
            Check(FindTargetModification(mech, fx.DeathCheat.defName), "PR protocol is removable through the modification job");
            Check(IsInstalledModification(installed!), "PR protocol is visible in the modification window");
        }
        else
        {
            Check(!second && secondReason != null, "second PR protocol install is rejected before 1.1.0: " + secondReason);
            Check(charges?.charges == 1, "rejected second install does not add a charge");
            Check(count == 1, "rejected second install does not duplicate the hediff");
            Check(!FindTargetModification(mech, fx.DeathCheat.defName), "PR protocol cannot be uninstalled before 1.1.0");
            Check(!IsInstalledModification(installed!), "PR protocol is invisible to the modification window before 1.1.0");
        }

        // Whole-body initial charge from DefModExt_InitialDeathCheat / Scourge spawn.
        Pawn initial = fx.NewMech(new IntVec3(6, 0, 4));
        initial.health.AddHediff(fx.DeathCheat, null);
        Hediff? initialHediff = initial.health.hediffSet.GetFirstHediffOfDef(fx.DeathCheat);
        HediffUtility.TryGetComp<HediffComp_DeathCheatCharges>(initialHediff!)?.SetCharges(5);
        Check(initialHediff != null, "initial whole-body PR protocol exists");
        if (green)
        {
            Check(initialHediff!.Part == fx.Part("Torso"), "initial Part==null PR protocol normalizes to the core part");
            Check(FindTargetModification(initial, fx.DeathCheat.defName), "normalized initial PR protocol is removable");
        }
        else
        {
            Check(initialHediff!.Part == null, "initial Part==null PR protocol reproduces the upstream shape");
        }

        // 20-charge cap and queued installs.
        charges!.SetCharges(HediffComp_DeathCheatCharges.MaxCharges);
        List<MechModificationQueueEntry> queue = new()
        {
            new MechModificationQueueEntry
            {
                kind = MechModificationOperationKind.Install,
                itemDef = fx.CerebellumItem,
                part = fx.Part("Torso"),
            },
        };
        bool capAllowed = ModificationInstallValidator.CanInstall(
            mech, fx.CerebellumItem, fx.Part("Torso"), null, out string capReason, checkInventory: false);
        if (green)
        {
            Check(!capAllowed && capReason != null, "20-charge PR protocol rejects another install: " + capReason);
            charges.SetCharges(HediffComp_DeathCheatCharges.MaxCharges - 1);
            bool queuedAllowed = ModificationInstallValidator.CanInstall(
                mech, fx.CerebellumItem, fx.Part("Torso"), queue, out string queuedReason, checkInventory: false);
            Check(!queuedAllowed && queuedReason != null, "19 charges plus one queued install reaches the cap: " + queuedReason);
        }
        else
        {
            Check(capAllowed, "20-charge PR protocol accepted another install before 1.1.0 (reason=" + capReason + ")");
        }

        // Oppressive restructuring: the item must never be destroyed without a charge.
        Pawn oppressed = fx.NewMech(new IntVec3(8, 0, 4));
        fx.GiveHediff(oppressed, fx.Oppressive);
        ThingWithComps blockedItem = NewItem(fx.CerebellumItem);
        bool blocked = ModificationForPawn(oppressed, blockedItem, out string blockedReason);
        if (green)
        {
            Check(!blocked, "PR protocol install is rejected during oppressive restructuring");
            Check(!oppressed.health.hediffSet.HasHediff(fx.DeathCheat), "blocked PR protocol install adds no hediff");
            Check(blockedReason != null && blockedReason.Length > 0, "blocked PR protocol install supplies a reason");
        }
        else
        {
            Check(blocked, "pre-fix PR protocol install reports success during oppressive restructuring");
            Check(!oppressed.health.hediffSet.HasHediff(fx.DeathCheat), "pre-fix install adds no hediff during oppressive restructuring");
        }

        ThingWithComps carried = NewItem(fx.CerebellumItem);
        oppressed.carryTracker.innerContainer.TryAdd(carried);
        ApplyModification(oppressed, carried);
        Check(carried.Destroyed != green, green
            ? "oppressive restructuring does not consume the carried item"
            : "pre-fix oppressive restructuring consumes the carried item without a charge");
    }

    // ----------------------------------------------------------------- A2

    private static void SyntheticBionic(bool green)
    {
        Pawn mech = fx.NewMech(new IntVec3(10, 0, 4));
        BodyPartRecord leg = fx.Part("DMS_SynthicLeg");
        fx.GiveHediff(mech, fx.SyntheticInterface);
        mech.health.hediffSet.hediffs.Add(HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, mech, leg));
        Check(mech.health.hediffSet.PartIsMissing(leg), "fixture limb is missing before synthetic install");

        ThingWithComps item = NewItem(fx.HeteroLegItem);
        Job_ApplySyntheticBionic job = new()
        {
            recipeDefName = fx.HeteroLegRecipe.defName,
            targetPartIndex = leg.Index,
            targetPartDefName = leg.def.defName,
        };
        job.SetTarget(TargetIndex.A, mech);
        job.SetTarget(TargetIndex.B, item);
        JobDriver_ApplySyntheticBionic driver = new() { pawn = mech, job = job };
        Call(driver, "ApplySyntheticBionic");

        Check(item.Destroyed, "synthetic bionic item is consumed");
        if (green)
        {
            Check(!mech.health.hediffSet.PartIsMissing(leg), "synthetic install restores the missing limb record");
            Check(mech.health.hediffSet.GetDirectlyAddedPartFor(leg)?.def == fx.HeteroLeg, "synthetic bionic attaches to the restored limb");
        }
        else
        {
            Check(mech.health.hediffSet.PartIsMissing(leg), "pre-fix synthetic install silently leaves the limb missing");
            Check(mech.health.hediffSet.GetDirectlyAddedPartFor(leg) == null, "pre-fix synthetic bionic is dropped");
        }
    }

    // ----------------------------------------------------------------- B1

    private static void ModificationPartDefaults(bool green)
    {
        CheckPartDefault(green, fx.CascadeDock, fx.CascadeItem, "Brain");
        CheckPartDefault(green, fx.Blackwater, fx.BlackwaterItem, "Spine");
    }

    private static void CheckPartDefault(bool green, HediffDef hediffDef, ThingDef itemDef, string intendedPart)
    {
        Pawn mech = fx.NewMech(new IntVec3(2, 0, 4));
        ThingWithComps item = NewItem(itemDef);
        bool first = ModificationForPawn(mech, item, out string firstReason);
        Hediff? installed = mech.health.hediffSet.GetFirstHediffOfDef(hediffDef);
        BodyPartRecord intended = fx.Part(intendedPart);
        BodyPartRecord reactor = fx.Part("Reactor");
        Check(first && installed != null, $"{hediffDef.defName} first install succeeds: {firstReason}");
        Check(installed?.Part == (green ? intended : reactor), green
            ? $"{hediffDef.defName} keeps the requested {intendedPart} part"
            : $"{hediffDef.defName} is overwritten to Reactor before 1.1.0");

        bool second = ModificationForPawn(mech, item, out string secondReason);
        HediffComp_Modification? modification = installed == null
            ? null
            : HediffUtility.TryGetComp<HediffComp_Modification>(installed);
        int count = mech.health.hediffSet.hediffs.Count(h => h.def == hediffDef);
        if (green)
        {
            Check(!second, $"{hediffDef.defName} second install is rejected: {secondReason}");
            Check(count == 1 && modification?.InstalledCount == 1,
                $"{hediffDef.defName} does not duplicate its single modification");
        }
        else
        {
            Check(second, $"{hediffDef.defName} second install silently duplicates before 1.1.0");
            Check(count == 1 && modification?.InstalledCount == 2,
                $"{hediffDef.defName} pre-fix duplicate stacks on Reactor");
        }
    }

    // ----------------------------------------------------------------- A3

    private static void RemoteControl(bool green)
    {
        Pawn actor = fx.NewColonist(new IntVec3(12, 0, 4));
        Pawn mech = fx.NewMech(new IntVec3(13, 0, 4));
        Check(!MechanitorUtility.ShouldBeMechanitor(actor), "colonist is not a mechanitor before the implant");

        fx.GiveHediff(actor, fx.RemoteController, fx.Part("Brain"));
        bool tracker = actor.mechanitor != null;
        Check(tracker == green, "remote controller " + (green ? "creates" : "does not create") + " a mechanitor tracker");
        Check(MechanitorUtility.ShouldBeMechanitor(actor) == green, "remote controller mechanitor predicate follows the fix");

        mech.jobs = null;
        mech.relations.AddDirectRelation(PawnRelationDefOf.Overseer, actor);
        if (green)
        {
            Check(actor.mechanitor!.TotalBandwidth >= 1, "remote controller derives the overseen mech's bandwidth");
            Check(actor.mechanitor.TotalAvailableControlGroups >= 1, "remote controller derives one control group");
            Check(actor.mechanitor.ControlledPawns.Contains(mech), "remote-controlled mech is in the controlled list");
            Check(MechanitorUtility.InMechanitorCommandRange(mech, new LocalTargetInfo(mech.Position + new IntVec3(1, 0, 0))),
                "remote controller command range no longer throws");
            Check(MechanitorUtility.CanDraftMech(mech), "remote controller can draft the controlled mech");
            ScribeRemoteTracker(actor);
        }
        else
        {
            Check(actor.mechanitor == null, "pre-fix remote controller has no mechanitor tracker");
            Check(ThrowsNullReference(() => MechanitorUtility.InMechanitorCommandRange(mech, new LocalTargetInfo(mech.Position))),
                "pre-fix command range throws NullReferenceException");
        }
    }

    private static void ScribeRemoteTracker(Pawn actor)
    {
        string path = Path.Combine(Path.GetTempPath(), "dmssr-unified-validation", "remote-" + Guid.NewGuid().ToString("N") + ".xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
                actor.drugs = null;
            actor.timetable = null;
            actor.outfits = null;
            actor.foodRestriction = null;
            actor.reading = null;
            actor.inventoryStock = null;
            List<Pawn> pawns = new() { actor, actor.mechanitor!.ControlledPawns[0] };
            Scribe.saver.InitSaving(path, "root");
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
            Scribe.saver.FinalizeSaving();

            List<Pawn>? loaded = null;
            Scribe.loader.InitLoading(path);
            Scribe_Collections.Look(ref loaded, "pawns", LookMode.Deep);
            Scribe.loader.FinalizeLoading();

            Check(loaded != null && loaded.Count == 2, "remote controller save/load keeps both pawns");
            Pawn loadedActor = loaded![0];
            Pawn loadedMech = loaded[1];
            Check(loadedActor.mechanitor != null && loadedActor.mechanitor.Pawn == loadedActor,
                "remote controller tracker survives save/load");
            Check(loadedMech.GetOverseer() == loadedActor, "remote control overseer relation survives save/load");
        }
        finally
        {
            Scribe.ForceStop();
            try { File.Delete(path); } catch { }
        }
    }

    // ----------------------------------------------------------------- B2

    private static void CascadeExpiry(bool green)
    {
        Pawn root = fx.NewMech(new IntVec3(2, 0, 6));
        Pawn leaf = fx.NewMech(new IntVec3(3, 0, 6));
        Hediff mark = fx.GiveHediff(root, fx.CasterMark);
        Hediff linked = fx.GiveHediff(leaf, fx.Linked);
        HediffComp_SchizophreniaLink link = HediffUtility.TryGetComp<HediffComp_SchizophreniaLink>(linked)!;
        link.SetSource(root, 8f, true, 60);

        Check((bool)Call(link, "IsLinkValid", leaf, root)!, "cascade link is valid while the root mark exists");
        root.health.RemoveHediff(mark);
        bool remains = leaf.health.hediffSet.HasHediff(fx.Linked);
        Check(remains != green, green
            ? "root expiry removes the linked leaf immediately"
            : "pre-fix root expiry leaves the linked leaf behind");

        Pawn lateLeaf = fx.NewMech(new IntVec3(4, 0, 6));
        Hediff lateLinked = fx.GiveHediff(lateLeaf, fx.Linked);
        HediffComp_SchizophreniaLink lateLink = HediffUtility.TryGetComp<HediffComp_SchizophreniaLink>(lateLinked)!;
        lateLink.SetSource(root, 8f, true, 60);
        bool lateValid = (bool)Call(lateLink, "IsLinkValid", lateLeaf, root)!;
        Check(lateValid == !green, green
            ? "link validation rejects a source without the root mark"
            : "pre-fix link validation accepts a source without the root mark");
    }

    // ----------------------------------------------------------------- B3

    private static void PowerCharge(bool green)
    {
        Pawn caster = fx.NewColonist(new IntVec3(5, 0, 6));
        Pawn friendly = fx.NewColonist(new IntVec3(6, 0, 6));
        Pawn hostile = fx.NewPawn(fx.HumanDef, new IntVec3(7, 0, 6), fx.HostileFaction, humanlike: true);
        JobDriver_PowerChargeDash driver = new() { pawn = caster, job = new Job() };

        bool acceptedFriendly = (bool)Call(driver, "ShouldCollectPawn", friendly)!;
        bool acceptedHostile = (bool)Call(driver, "ShouldCollectPawn", hostile)!;
        if (green)
        {
            Check(!acceptedFriendly, "power charge does not grab a same-faction pawn");
            Check(acceptedHostile, "power charge still grabs a hostile pawn");
        }
        else
        {
            Check(acceptedFriendly, "pre-fix power charge grabs a same-faction pawn");
            Check(acceptedHostile, "pre-fix power charge grabs a hostile pawn");
        }

        Hediff burst = fx.GiveHediff(caster, fx.Burst);
        Hediff buff = fx.GiveHediff(caster, fx.ChargeBuff);
        JobDriver_PowerChargeDash interrupted = new() { pawn = caster, job = new Job() };
        interrupted.job.SetTarget(TargetIndex.A, caster.Position);
        _ = Call(interrupted, "MakeNewToils");
        interrupted.Cleanup(JobCondition.InterruptForced);
        Check(!caster.health.hediffSet.hediffs.Contains(burst) == green, green
            ? "interrupted charge removes the dash Burst hediff"
            : "pre-fix interrupted charge leaves the dash Burst hediff");
        Check(!caster.health.hediffSet.hediffs.Contains(buff) == green, green
            ? "interrupted charge removes the post-dash Buff hediff"
            : "pre-fix interrupted charge leaves the post-dash Buff hediff");

        caster.health.hediffSet.hediffs.RemoveAll(h => h.def == fx.Burst || h.def == fx.ChargeBuff);
        caster.health.hediffSet.DirtyCache();
        fx.GiveHediff(caster, fx.Burst);
        fx.GiveHediff(caster, fx.ChargeBuff);
        JobDriver_PowerChargeDash success = new() { pawn = caster, job = new Job() };
        success.job.SetTarget(TargetIndex.A, caster.Position);
        _ = Call(success, "MakeNewToils");
        success.Cleanup(JobCondition.Succeeded);
        if (green)
        {
            Check(!caster.health.hediffSet.HasHediff(fx.Burst), "successful charge clears the dash Burst hediff");
            Check(caster.health.hediffSet.HasHediff(fx.ChargeBuff), "successful charge keeps the intended post-dash Buff");
        }
        else
        {
            Check(caster.health.hediffSet.HasHediff(fx.Burst), "pre-fix successful charge keeps the dash Burst hediff");
            Check(caster.health.hediffSet.HasHediff(fx.ChargeBuff), "pre-fix successful charge keeps the Buff");
        }
    }

    // ----------------------------------------------------------------- B4

    private static void CephalopodaThrow(bool green)
    {
        Pawn pawn = fx.NewPawn(fx.MechDef, new IntVec3(1, 0, 1), fx.PlayerFaction);
        pawn.abilities = new Pawn_AbilityTracker(pawn);
        AbilityDef abilityDef = new()
        {
            defName = "Audit_Cephalopoda_Grasp",
            charges = 4,
            cooldownPerCharge = true,
            verbProperties = new VerbProperties(),
        };
        Ability ability = new() { pawn = pawn, def = abilityDef, comps = new List<AbilityComp>(), maxCharges = 4 };
        VerbTracker verbTracker = new(ability);
        typeof(VerbTracker).GetField("verbs", Flags)!.SetValue(verbTracker, new List<Verb>
        {
            new Verb_CastAbility { verbProps = new VerbProperties { isPrimary = true } },
        });
        typeof(Ability).GetField("verbTracker", Flags)!.SetValue(ability, verbTracker);
        typeof(Ability).GetField("charges", Flags)!.SetValue(ability, 4);
        CompAbilityEffect_CephalopodaJumpWithAllies comp = new()
        {
            parent = ability,
            props = new CompProperties_AbilityCephalopodaJumpWithAllies
            {
                allyTargetRange = 3f,
                allyLandingRadius = 4f,
                maxTargetBodySize = 2.5f,
            },
        };
        ability.comps.Add(comp);
        pawn.abilities.abilities.Add(ability);

        JobGiver_AICastCephalopodaGraspOnBestEnemyValue giver = new() { ability = abilityDef };
        MethodInfo tryGiveJob = typeof(JobGiver_AICastCephalopodaGraspOnBestEnemyValue)
            .GetMethod("TryGiveJob", Flags)!;
        try
        {
            Job? job = (Job?)tryGiveJob.Invoke(giver, new object[] { pawn });
            if (green)
            {
                Check(job == null, "cephalopoda AI returns null instead of throwing when no ally pair exists");
            }
            else
            {
                Check(false, "pre-fix cephalopoda AI did not throw");
            }
        }
        catch (TargetInvocationException exception) when (exception.InnerException is NullReferenceException)
        {
            Check(!green, "pre-fix cephalopoda AI throws NullReferenceException");
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    // ----------------------------------------------------------------- B5

    private static void EmergencyTend(bool green)
    {
        Pawn target = fx.NewColonist(new IntVec3(9, 0, 8));
        List<Hediff_Injury> injuries = new();
        BodyPartRecord torso = fx.Part("Torso");
        for (int i = 0; i < 12; i++)
        {
            Hediff_Injury injury = (Hediff_Injury)fx.GiveHediffDirect(target, fx.Injury, torso);
            typeof(Hediff).GetField("severityInt", Flags)!.SetValue(injury, 0.05f);
            injuries.Add(injury);
        }

        CompProperties_AbilityEmergencyTend props = new() { maxTendQuality = 1.3f };
        Ability ability = new() { pawn = target, comps = new List<AbilityComp>() };
        CompAbilityEffect_EmergencyTend comp = new() { parent = ability, props = props };
        ability.comps.Add(comp);
        comp.Apply(new LocalTargetInfo(target), new LocalTargetInfo(target));

        float min = float.MaxValue;
        float max = float.MinValue;
        float total = 0f;
        foreach (Hediff_Injury injury in injuries)
        {
            HediffComp_TendDuration tend = HediffUtility.TryGetComp<HediffComp_TendDuration>(injury)!;
            min = Math.Min(min, tend.tendQuality);
            max = Math.Max(max, tend.tendQuality);
            total += tend.tendQuality;
        }
        float average = total / injuries.Count;
        if (green)
        {
            Check(min >= 1.0f && max <= 1.3f + 0.0001f, $"emergency tend quality uses the authored 1.3 ceiling (min={min:0.###}, max={max:0.###})");
            Check(average > 1.1f, $"emergency tend average is above 110% (average={average:0.###})");
        }
        else
        {
            Check(max <= 0.25f + 0.0001f && average < 0.2f, $"pre-fix emergency tend quality is capped at 25% (average={average:0.###})");
        }
    }

    // ----------------------------------------------------------------- B6

    private static void UndyingArmor(bool green)
    {
        Pawn pawn = fx.NewColonist(new IntVec3(11, 0, 8));
        StatDef sharp = StatDefOf.ArmorRating_Sharp!;
        float before = sharp.Worker.GetValue(StatRequest.For(pawn));
        Check(Math.Abs(before - 0.5f) < 0.0001f, "fixture armor baseline is 0.5");

        HediffWithComps boost = (HediffWithComps)HediffMaker.MakeHediff(fx.UndyingBoost, pawn);
        HediffComp_ArmorBoost comp = HediffUtility.TryGetComp<HediffComp_ArmorBoost>(boost)!;
        comp.Setup(20000, 1.5f, 0.5f, 0.5f);
        pawn.health.AddHediff(boost, null);
        HediffComp_Disappears disappears = HediffUtility.TryGetComp<HediffComp_Disappears>(boost)!;

        float after = sharp.Worker.GetValue(StatRequest.For(pawn));
        if (green)
        {
            Check(Math.Abs(after - 0.75f) < 0.0001f, $"undying armor multiplies the final armor stat (before={before}, after={after})");
            Check(disappears.ticksToDisappear == 20000 && disappears.disappearsAfterTicks == 20000,
                "undying armor uses the full 20,000-tick duration");
        }
        else
        {
            Check(Math.Abs(after - before) < 0.0001f, "pre-fix undying armor is not read by the stat system");
            Check(disappears.ticksToDisappear == 10000, "pre-fix undying armor countdown is 10,000 ticks");
        }
    }

    // ----------------------------------------------------------------- B7

    private static void MechaniteRecognition(bool green)
    {
        Pawn pawn = fx.NewColonist(new IntVec3(13, 0, 8));
        HediffWithComps fibrous = (HediffWithComps)HediffMaker.MakeHediff(fx.FibrousMechanites, pawn);
        fibrous.Severity = 0.1f;
        HediffComp_Disappears disappears = HediffUtility.TryGetComp<HediffComp_Disappears>(fibrous)!;
        disappears.ticksToDisappear = 1000000;
        disappears.disappearsAfterTicks = 1000000;
        pawn.health.hediffSet.hediffs.Add(fibrous);

        object heterogenization = NewGene(typeof(Gene_ImmuneHeterogenization), pawn);
        Call(heterogenization, "BoostNanomechInfectionDurations");
        int ticks = disappears.ticksToDisappear;
        Check(ticks == (green ? 2000000 : 1000000), green
            ? "immune heterogenization doubles mechanite duration"
            : "pre-fix immune heterogenization ignores mechanite duration");

        Pawn mutationPawn = fx.NewColonist(new IntVec3(14, 0, 8));
        HediffWithComps sensory = (HediffWithComps)HediffMaker.MakeHediff(fx.SensoryMechanites, mutationPawn);
        sensory.Severity = 0.1f;
        mutationPawn.health.hediffSet.hediffs.Add(sensory);
        object mutation = NewGene(typeof(Gene_ImmuneMutation), mutationPawn);
        Call(mutation, "ApplyDiseaseImmunity");
        bool kept = mutationPawn.health.hediffSet.hediffs.Contains(sensory);
        Check(kept == green, green
            ? "immune mutation keeps an early mechanite infection"
            : "pre-fix immune mutation deletes the mechanite as a disease");

        Pawn severePawn = fx.NewColonist(new IntVec3(15, 0, 8));
        HediffWithComps severe = (HediffWithComps)HediffMaker.MakeHediff(fx.FibrousMechanites, severePawn);
        severe.Severity = 1f;
        severePawn.health.hediffSet.hediffs.Add(severe);
        object severeMutation = NewGene(typeof(Gene_ImmuneMutation), severePawn);
        killed = false;
        Call(severeMutation, "ApplyDiseaseImmunity");
        Check(killed == green, green
            ? "severe mechanite triggers the immune mutation lethality rule"
            : "pre-fix severe mechanite is treated as an ordinary removable disease");
        Check(severePawn.health.hediffSet.hediffs.Contains(severe) == green, "severe mechanite is not deleted before the lethality rule");
    }

    // ----------------------------------------------------------------- B8

    private static void GeneFittingPod(bool green)
    {
        Pawn pawn = fx.NewColonist(new IntVec3(16, 0, 8));
        Building_DMSGeneFittingPod pod = new() { def = fx.PodDef };
        pod.InitializeComps();
        Comp_DMSGeneFittingPod comp = pod.GetComp<Comp_DMSGeneFittingPod>();
        Job job = new();
        job.SetTarget(TargetIndex.A, pod);
        JobDriver_EnterGeneFittingPod driver = new() { pawn = pawn, job = job };
        driver.TryMakePreToilReservations(false);

        Check(comp.IsQueuedSubject(pawn) == green, green
            ? "right-click pod entry queues the subject before the job starts"
            : "pre-fix pod entry never queues the subject");
        Check(comp.QueuedSubject == (green ? pawn : null), "pod queue subject matches the initiating pawn");

        _ = ((IEnumerable<Toil>)Call(driver, "MakeNewToils")!).ToList();
        JobCondition queueCondition = driver.globalFailConditions[1]();
        Check((queueCondition == JobCondition.Ongoing) == green, green
            ? "pod entry required-queue condition passes"
            : "pre-fix pod entry required-queue condition is Incompletable");
    }

    // ----------------------------------------------------------------- B9

    private static void FacedancerGate(bool green)
    {
        Pawn hostile = fx.NewPawn(fx.HumanDef, new IntVec3(17, 0, 8), fx.HostileFaction, humanlike: true);
        hostile.drafter = null;
        hostile.playerSettings = null;
        Hediff facechange = fx.GiveHediffDirect(hostile, fx.FacedancerFacechange);
        HediffComp_FacedancerFacechange comp = HediffUtility.TryGetComp<HediffComp_FacedancerFacechange>(facechange)!;
        comp.hydraEncounterId = 17;
        comp.CompPostPostAdd(null);

        bool colonistControlled = hostile.IsColonistPlayerControlled;
        bool mechControlled = hostile.IsColonyMechPlayerControlled;
        bool draftGizmo = hostile.drafter != null && hostile.drafter.ShowDraftGizmo;
        bool canDraft = MechanitorUtility.CanDraftMech(hostile).Accepted;
        if (green)
        {
            Check(!colonistControlled && !mechControlled && !draftGizmo && !canDraft,
                "Hydra facechange never grants player control or drafting");
            Check(hostile.drafter == null && hostile.playerSettings == null,
                "Hydra facechange does not allocate drafter or playerSettings on hostile infiltrator");
        }
        else
        {
            Check(colonistControlled && mechControlled && draftGizmo && canDraft,
                "pre-fix Hydra facechange grants every player-control flag");
        }

        Pawn original = fx.NewColonist(new IntVec3(18, 0, 8));
        Pawn genuine = fx.NewPawn(fx.HumanDef, new IntVec3(19, 0, 8), fx.PlayerFaction, humanlike: true);
        Hediff genuineFacechange = fx.GiveHediff(genuine, fx.FacedancerFacechange);
        HediffComp_FacedancerFacechange genuineComp = HediffUtility.TryGetComp<HediffComp_FacedancerFacechange>(genuineFacechange)!;
        typeof(HediffComp_FacedancerFacechange).GetField("originalFacedancer", Flags)!.SetValue(genuineComp, original);
        typeof(HediffComp_FacedancerFacechange).GetField("originalFaction", Flags)!.SetValue(genuineComp, fx.PlayerFaction);
        typeof(HediffComp_FacedancerFacechange).GetField("initialized", Flags)!.SetValue(genuineComp, true);
        genuineComp.CompPostPostAdd(null);
        Check(genuine.IsColonistPlayerControlled, "a player-owned facedancer disguise keeps player control");
    }

    // ----------------------------------------------------------------- XML

    private static void XmlPatchWiring()
    {
        string patches = Path.Combine(Program.ModRoot, "1.7", "Patches");
        System.Xml.Linq.XDocument death = System.Xml.Linq.XDocument.Load(Path.Combine(patches, "DMSSR_DeathCheat_Modification.xml"));
        Check(death.Descendants("xpath").Any(x => (((string?)x) ?? "").Contains("DMS_DeathCheat")),
            "PR protocol XML patch targets the live hediff def");
        System.Xml.Linq.XDocument parts = System.Xml.Linq.XDocument.Load(Path.Combine(patches, "DMSSR_ModificationPartDefaults.xml"));
        string[] targets = parts.Descendants("xpath").Select(x => (string)x).ToArray();
        Check(targets.Any(t => t.Contains("DMS_CascadeDock")) && targets.Any(t => t.Contains("DMS_Blackwater")),
            "modification part XML patch targets both wrong defaults");

        string heddiffs = Path.Combine(Program.WorkshopRoot, "3835512735", "1.7", "Defs", "Hediffs");
        System.Xml.Linq.XDocument deathXml = System.Xml.Linq.XDocument.Load(Path.Combine(heddiffs, "Hediffs_DeathCheat.xml"));
        Check(deathXml.Descendants("HediffDef").Any(d => (string?)d.Element("defName") == "DMS_DeathCheat"),
            "installed PR protocol def is present");
        System.Xml.Linq.XDocument cascadeXml = System.Xml.Linq.XDocument.Load(Path.Combine(heddiffs, "Hediffs_Schizophrenia.xml"));
        Check(cascadeXml.Descendants("defaultInstallPart").Any(p => (string)p == "Reactor"),
            "installed cascade dock still carries the Reactor default before the patch");
        System.Xml.Linq.XDocument blackwaterXml = System.Xml.Linq.XDocument.Load(Path.Combine(heddiffs, "Hediffs_Blackwater.xml"));
        Check(blackwaterXml.Descendants("defaultInstallPart").Any(p => (string)p == "Reactor"),
            "installed Blackwater still carries the Reactor default before the patch");
    }

    // ----------------------------------------------------------------- helpers

    private static void ApplyUpstreamDeathCheatPatch()
    {
        MethodInfo add = AccessTools.Method(
            typeof(Pawn_HealthTracker),
            nameof(Pawn_HealthTracker.AddHediff),
            new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })!;
        Harness.Patch(add, prefix: new HarmonyMethod(
            AccessTools.Method(typeof(Patch_BlockDeathCheatGain), "Prefix")));
    }

    private static void ApplyUpstreamFacechangePatches()
    {
        Harness.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonistPlayerControlled)),
            postfix: new HarmonyMethod(typeof(Patch_Facechange_IsColonistPlayerControlled), "Postfix"));
        Harness.Patch(AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.IsColonyMechPlayerControlled)),
            postfix: new HarmonyMethod(typeof(Patch_Facechange_IsColonyMechPlayerControlled), "Postfix"));
        Harness.Patch(AccessTools.PropertyGetter(typeof(Pawn_DraftController), nameof(Pawn_DraftController.ShowDraftGizmo)),
            postfix: new HarmonyMethod(typeof(Patch_Facechange_ShowDraftGizmo), "Postfix"));
        Harness.Patch(AccessTools.Method(typeof(MechanitorUtility), nameof(MechanitorUtility.CanDraftMech)),
            postfix: new HarmonyMethod(typeof(Patch_Facechange_CanDraftMech), "Postfix"));
        Harness.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill), new[] { typeof(DamageInfo?), typeof(Hediff) }),
            prefix: new HarmonyMethod(typeof(Tests), nameof(KillPrefix)));
    }

    private static bool KillPrefix(Pawn __instance)
    {
        killed = true;
        return false;
    }

    private static object NewGene(Type type, Pawn pawn)
    {
        object gene = Activator.CreateInstance(type)!;
        typeof(Gene).GetField("pawn", Flags)!.SetValue(gene, pawn);
        return gene;
    }

    private static bool SkipDeSpawnOrDeselect() => false;

    private static bool SkipDrop() => false;

    private static bool DrawPosPrefix(Pawn __instance, ref Vector3 __result)
    {
        __result = __instance.Position.ToVector3();
        return false;
    }

    private static bool DestroyPrefix(Thing __instance)
    {
        typeof(Thing).GetField("mapIndexOrState", Flags)!.SetValue(__instance, (sbyte)(-3));
        return false;
    }

    private static ThingWithComps NewItem(ThingDef def)
    {
        ThingWithComps item = new()
        {
            def = def,
            stackCount = 1,
            thingIDNumber = nextItemId++,
        };
        item.InitializeComps();
        return item;
    }

    private static bool ModificationForPawn(Pawn target, Thing item, out string reason)
    {
        Job_Modification job = new() { itemDefName = item.def.defName };
        job.SetTarget(TargetIndex.A, target);
        job.SetTarget(TargetIndex.B, item);
        JobDriver_ApplyModification driver = new() { pawn = target, job = job };
        object?[] args = { target, item, null };
        bool result = (bool)AccessTools.Method(typeof(JobDriver_ApplyModification), "ModificationForPawn")!
            .Invoke(driver, args)!;
        reason = (string?)args[2] ?? "";
        return result;
    }

    private static void ApplyModification(Pawn target, Thing item)
    {
        Job_Modification job = new() { itemDefName = item.def.defName };
        job.SetTarget(TargetIndex.A, target);
        job.SetTarget(TargetIndex.B, item);
        JobDriver_ApplyModification driver = new() { pawn = target, job = job };
        Call(driver, "ApplyModification");
    }

    private static bool FindTargetModification(Pawn target, string defName)
    {
        Job_Modification job = new() { targetHediffDefName = defName };
        job.SetTarget(TargetIndex.A, target);
        JobDriver_RemoveModification driver = new() { pawn = target, job = job };
        return Call(driver, "FindTargetModification") != null;
    }

    private static bool IsInstalledModification(Hediff hediff)
    {
        Window_MechModification window =
            (Window_MechModification)RuntimeHelpers.GetUninitializedObject(typeof(Window_MechCustomization));
        return (bool)AccessTools.Method(typeof(Window_MechModification), "IsInstalledModification", new[] { typeof(Hediff) })!
            .Invoke(window, new object?[] { hediff })!;
    }

    private static bool ThrowsNullReference(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (NullReferenceException)
        {
            return true;
        }
    }

    private static object? Call(object instance, string name, params object?[] args)
    {
        MethodInfo method = instance.GetType().GetMethod(name, Flags)
            ?? throw new MissingMethodException(instance.GetType().FullName, name);
        try
        {
            return method.Invoke(instance, args);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException ?? exception;
        }
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
        {
            throw new InvalidOperationException("Audited assertion failed: " + message);
        }
        Console.WriteLine("PASS " + message);
    }
}
