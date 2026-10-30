using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Fortified;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using WhiteGiver.DMSMechStorage;

internal static class Scenarios
{
    internal static void Run()
    {
        ScopeAndMenu();
        Job();
        Footprints();
        RoundTrip();
        Failures();
        Autonomous();
        Commander();
        MinifyAndReinstall();
    }

    private static void ScopeAndMenu()
    {
        Driver.ResetMap();
        foreach (string package in new[]
        {
            "Aoba.DeadManSwitch.Core", "Aoba.DeadManSwitch.AncientCorps", "Aoba.DeadManSwitch.Synthetic",
            "Aoba.DeadManSwitch.Expedition", "Aoba.DeadManSwitch.Taktikmagier", "Aoba.DeadManSwitch.MobileDragoon",
            "Aoba.DeadManSwitch.Motorized", "Srxf.DeadManSwitch.Legion", "dms.tritech", "dms.xivbattlegroup"
        }) Driver.Check(MechStorage.IsDmsMech(Driver.NewPawn(package)), "Missing DMS package: " + package);
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Driver.Check(!MechStorage.IsDmsMech(Driver.NewPawn("ludeon.rimworld.biotech"))
            && !MechStorage.IsDmsMech(Driver.NewPawn("other.mod", type: typeof(WeaponUsableMech)))
            && !MechStorage.IsDmsMech(Driver.NewPawn("aoba.deadmanswitch.core", mechanitor: true)),
            "Vanilla, foreign FFF user or humanlike pawn was included.");
        Pawn mech = Driver.NewPawn();
        Pawn original = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        original.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
        Driver.Bandwidth[actor] = 0;
        Driver.Check(MechStorage.CanStore(actor, mech).Accepted, "Sealing incorrectly required own overseer or spare bandwidth.");

        FloatMenuMakerMap.Init();
        var providers = (List<FloatMenuOptionProvider>)AccessTools.Field(typeof(FloatMenuMakerMap), "providers").GetValue(null)!;
        var provider = providers.OfType<FloatMenuOptionProvider_StoreDmsMech>().Single();
        var context = (FloatMenuContext)RuntimeHelpers.GetUninitializedObject(typeof(FloatMenuContext));
        context.allSelectedPawns = new List<Pawn> { actor };
        context.map = Driver.Map;
        Driver.Set(typeof(FloatMenuContext), "cachedClickedCell", context, mech.Position);
        FloatMenuMakerMap.currentProvider = provider;
        Driver.Check(provider.Applies(context) && provider.SelectedPawnValid(actor, context), "Mechanitor provider was not discovered/applicable.");
        context.allSelectedPawns.Add(original);
        Driver.Check(!provider.Applies(context), "Multi-selection was accepted.");
        context.allSelectedPawns.Remove(original);
        Driver.Manipulation = false;
        Driver.Check(!provider.SelectedPawnValid(actor, context), "Manipulation gate was ignored.");
        Driver.Manipulation = true;
        FloatMenuOption option = provider.GetOptionsFor(mech, context).Single();
        Driver.Check(!option.Disabled && option.Label.StartsWith("封存", StringComparison.Ordinal), "Chinese storage entry missing or disabled.");
        Driver.Reachable = false;
        Driver.Check(provider.GetOptionsFor(mech, context).Single().Disabled, "Unreachable target option is enabled.");
        Driver.Reachable = true;
        FloatMenuMakerMap.currentProvider = null;
        option.action();
        Driver.Check(Driver.Ordered?.def == Driver.StoreJob && Driver.Ordered.targetA.Pawn == mech, "Menu did not order the custom target-A job.");
        Driver.Ordered = null;
        mech.DeSpawn();
        option.action();
        Driver.Check(Driver.Ordered == null, "Stale menu target still issued an order.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        var owned = Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method)?.Owners.Contains("whitegiver.dms.mechstorage") == true).ToArray();
        Driver.Check(owned.Length == 10, "Production mod installed " + owned.Length + " patches instead of ten.");
        Driver.Check(owned.All(method => method.DeclaringType == typeof(Building_MechCapsule) || method.DeclaringType == typeof(MinifiedThing)
            || method.DeclaringType == typeof(PawnUtility) || method.DeclaringType == typeof(GenSpawn)
            || method.DeclaringType == typeof(ThingWithComps) || method.DeclaringType == typeof(MainTabWindow_Inspect)),
            "Production mod installed an unexpected global patch.");
        Console.WriteLine("PASS: all ten DMS package gates; vanilla/foreign/human exclusions; native provider discovery, Chinese menu, stale/reach/manipulation gates and job ordering.");
    }

    private static void Job()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        var driver = new JobDriver_StoreMech { pawn = actor, job = JobMaker.MakeJob(Driver.StoreJob, mech) };
        actor.jobs.curJob = driver.job;
        Driver.Check(driver.TryMakePreToilReservations(true), "Storage job could not reserve its mech.");
        Pawn competitor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Driver.Check(!MechStorage.CanStore(competitor, mech).Accepted, "Conflicting reservation was ignored.");
        var toils = ((IEnumerable<Toil>)AccessTools.DeclaredMethod(typeof(JobDriver_StoreMech), "MakeNewToils").Invoke(driver, null)!).ToArray();
        Driver.Check(toils.Length == 3 && toils[1].defaultCompleteMode == ToilCompleteMode.Delay && toils[1].defaultDuration == 300,
            "Job no longer walks, waits 300 ticks and stores only in its final toil.");
        foreach (Toil toil in toils) toil.actor = actor;
        toils[1].initAction();
        Driver.Check(Driver.ForcedWait == 300 && mech.Spawned && mech.holdingOwner?.Owner == Driver.Map, "Native WaitWith did not hold the target without sealing early.");
        var enemy = new Faction { def = new FactionDef { defName = "ValidationEnemy" } };
        Driver.Set(typeof(Thing), "factionInt", mech, enemy);
        toils[2].initAction();
        Driver.Check(mech.Spawned && mech.holdingOwner?.Owner == Driver.Map, "Final toil failed to revalidate changed ownership.");
        Driver.Set(typeof(Thing), "factionInt", mech, Driver.Player);
        toils[2].initAction();
        Driver.Check(mech.holdingOwner?.Owner is Building_MechCapsule { Spawned: true }, "Final native toil did not store its reserved mech.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: real reservation conflict, native 300-tick WaitWith/ForceWait boundary, final-toil ownership revalidation and storage action; path arrival/timing manually driven.");
    }

    private static void Footprints()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        for (int size = 1; size <= 5; size++)
        {
            Driver.Graphics[mech.Drawer.renderer].drawSize = new Vector2(size + 1.1f, size + 1.1f);
            ThingDef def = MechCapsuleUtility.GetCapsuleDefForMech(mech);
            Driver.Check(def.Size.x == size && def.Size.z == size, "FFF size mapping changed.");
            CellRect rect = GenAdj.OccupiedRect(mech.Position, mech.Rotation, def.Size);
            Driver.Check(MechStorage.CanStore(actor, mech).Accepted, "Open footprint rejected.");
            IntVec3 blocker = new(rect.maxX, 0, rect.maxZ);
            Driver.Blocked.Add(blocker);
            Driver.Check(!MechStorage.CanStore(actor, mech).Accepted && mech.Spawned, "A non-root unwalkable footprint cell was ignored.");
            Driver.Blocked.Clear();
            Driver.Check(!MechStorage.ClearFootprint(def, new IntVec3(-1, 0, 0), Driver.Map, mech.Rotation, mech), "Out-of-bounds footprint accepted.");
        }
        ThingDef large = MechCapsuleUtility.GetCapsuleDefForMech(mech);
        var building = new Building
        {
            def = new ThingDef { defName = "ValidationBlocker", category = ThingCategory.Building, thingClass = typeof(Building), building = new BuildingProperties(), fillPercent = 1f, passability = Traversability.Impassable },
            Position = new IntVec3(12, 0, 12)
        };
        Driver.Register(building, Driver.Map);
        Driver.Check(!MechStorage.CanStore(actor, mech).Accepted && !MechStorage.Store(actor, mech) && building.Spawned && mech.Spawned,
            "Sealing did not reject a non-root building or mutated the rejected target.");
        building.Destroy();
        Driver.Check(MechStorage.ClearFootprint(large, mech.Position, Driver.Map, mech.Rotation, mech), "Removing the blocking edifice did not clear the footprint.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: actual FFF sizes 1–5, native CanSpawnAt/edifice/wipe checks, full-footprint blocked cells, bounds and non-destructive rejection.");
    }

    private static void RoundTrip()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        ThingWithComps weapon = Item("ValidationWeapon");
        weapon.def.equipmentType = EquipmentType.Primary;
        ThingWithComps cargo = Item("ValidationCargo");
        mech.equipment.GetDirectlyHeldThings().TryAdd(weapon);
        mech.inventory.innerContainer.TryAdd(cargo);
        var marker = new Hediff { def = new HediffDef { defName = "ValidationDamage" }, pawn = mech };
        mech.health.hediffSet.hediffs.Add(marker);
        string name = mech.Name.ToStringFull;
        actor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
        Driver.Check(actor.mechanitor.UsedBandwidth == 1, "Fixture initial bandwidth differs.");
        Driver.Check(MechStorage.Store(actor, mech), "DMS mech could not be stored: " + string.Join("\n", Driver.Errors));
        Driver.Check(Driver.NativeSealCalls == 1, "Mechanitor storage did not reuse DMS's native FFF sealing utility.");
        var capsule = (Building_MechCapsule)mech.holdingOwner.Owner;
        Driver.Check(capsule.Spawned && capsule.Mech == mech && actor.mechanitor.UsedBandwidth == 0,
            "Storage lost the pawn or failed to release bandwidth.");
        Driver.Check(Driver.Map.edificeGrid[capsule.Position] == capsule, "Fixture omitted actual capsule edifice registration.");
        Driver.Check(capsule.CanActivate(actor).Accepted, "Stored DMS mech could not be activated.");
        capsule.ActivateMech(actor);
        Driver.Check(mech.Spawned, "Activation left the mech unspawned: " + string.Join("\n", Driver.Errors));
        Driver.Check(mech.GetOverseer() == actor, "Activation did not re-enroll the mech.");
        Driver.Check(actor.mechanitor.UsedBandwidth == 1, "Activation did not restore bandwidth accounting.");
        Driver.Check(capsule.Destroyed, "Activation left the emptied capsule on the map.");
        Driver.Check(Driver.LastPawnSpawnWasRestore, "Activation used first-spawn semantics and would refill power/platform resources.");
        Driver.Check(mech.Name.ToStringFull == name && mech.equipment.Primary == weapon && mech.inventory.innerContainer.Contains(cargo)
            && mech.health.hediffSet.hediffs.Contains(marker) && actor.mechanitor.ControlledPawns.Contains(mech), "Round trip changed name/gear/health or left the control cache stale.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: same-pawn name/health/equipment/inventory round trip, original bandwidth/control-cache release and receiving enrollment; capsule's own edifice is ignored only on release.");
    }

    private static void Failures()
    {
        foreach (string failure in new[] { "accept", "capsule-spawn", "both-spawns" })
        {
            Driver.ResetMap();
            Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
            Pawn mech = Driver.NewPawn();
            actor.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            MechanitorControlGroup group = actor.mechanitor.GetControlGroup(mech);
            if (failure == "accept") Driver.RejectAccept = 1;
            else Driver.FailCapsuleSpawn = 1;
            if (failure == "both-spawns") Driver.FailPawnSpawn = 1;
            Driver.Check(!MechStorage.Store(actor, mech) && !mech.Destroyed, "Injected storage failure was accepted or destroyed the pawn.");
            if (failure != "both-spawns")
                Driver.Check(mech.Spawned && mech.GetOverseer() == actor && actor.mechanitor.GetControlGroup(mech) == group
                    && actor.mechanitor.UsedBandwidth == 1, "Failed storage did not restore the original pawn/relation/group.");
            else
            {
                var retained = (Building_MechCapsule)mech.holdingOwner.Owner;
                Driver.Check(retained.ParentHolder is MinifiedThing mini && mini.holdingOwner == actor.inventory.innerContainer
                    && retained.Mech == mech && actor.mechanitor.UsedBandwidth == 0, "Double spawn failure did not retain the pawn in the actor inventory.");
            }
        }
        foreach (bool throwAfterRemoval in new[] { false, true })
        {
            Driver.ResetMap();
            Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
            Pawn mech = Driver.NewPawn();
            Driver.Check(MechStorage.Store(actor, mech), "Activation-failure setup could not store mech.");
            var capsule = (Building_MechCapsule)mech.holdingOwner.Owner;
            if (throwAfterRemoval) Driver.ThrowPawnSetup = true;
            else Driver.FailPawnSpawn = 1;
            Driver.Check(!MechStorage.Activate(capsule, actor) && capsule.Spawned && !capsule.Destroyed && capsule.Mech == mech
                && mech.holdingOwner == capsule.innerContainer && !mech.Destroyed, "Failed activation lost the same contained pawn.");
            Driver.Check(MechStorage.Activate(capsule, actor) && mech.Spawned, "A failed activation made the capsule permanently unusable.");
        }
        Driver.ResetMap();
        Pawn desyncActor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn desyncMech = Driver.NewPawn();
        Driver.Check(MechStorage.Store(desyncActor, desyncMech), "Desync setup could not store the mech.");
        var desyncCapsule = (Building_MechCapsule)desyncMech.holdingOwner.Owner;
        desyncMech.holdingOwner = null;
        Driver.Check(MechStorage.Activate(desyncCapsule, desyncActor) && desyncMech.Spawned
            && desyncMech.holdingOwner?.Owner == Driver.Map && !desyncCapsule.HasMech && desyncCapsule.Destroyed,
            "A lost container link blocked release: " + string.Join("\n", Driver.Errors));
        var emptyCapsule = (Building_MechCapsule)ThingMaker.MakeThing(MechCapsuleUtility.GetCapsuleDefForMech(desyncMech));
        emptyCapsule.SetFaction(Driver.Player);
        GenSpawn.Spawn(emptyCapsule, new IntVec3(20, 0, 20), Driver.Map);
        Driver.Check(!emptyCapsule.CanActivate(desyncActor).Accepted && !MechStorage.Activate(emptyCapsule, desyncActor),
            "An empty capsule was treated as activatable.");

        Driver.ResetMap();
        Pawn linkActor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn linkMech = Driver.NewPawn();
        Driver.Check(MechStorage.Store(linkActor, linkMech), "Dangling-link setup could not store the mech.");
        var linkCapsule = (Building_MechCapsule)linkMech.holdingOwner.Owner;
        IntVec3 capsuleCell = linkCapsule.Position;
        var host = (Building_MechCapsule)ThingMaker.MakeThing(MechCapsuleUtility.GetCapsuleDefForMech(linkMech));
        linkMech.holdingOwner = host.innerContainer;
        Driver.Set(typeof(Thing), "positionInt", linkMech, new IntVec3(0, 0, 0));
        Driver.Register(linkMech, Driver.Map);
        Driver.Check(MechStorage.Activate(linkCapsule, linkActor) && linkMech.Spawned
            && linkMech.Position == capsuleCell,
            "A dangling/stale spawned state blocked release or left the mech at the map corner: "
            + string.Join("\n", Driver.Errors));
        Driver.Check(Driver.Map.spawnedThings.Contains(linkMech) && linkMech.holdingOwner?.Owner == Driver.Map,
            "Map ownership of the released mech was corrupted.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));

        Driver.ResetMap();
        Pawn repairActor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn repairMech = Driver.NewPawn();
        Driver.Check(MechStorage.Store(repairActor, repairMech), "Map-owner repair setup could not store the mech.");
        var repairCapsule = (Building_MechCapsule)repairMech.holdingOwner.Owner;
        Driver.Map.spawnedThings.TryAdd(repairMech, false);
        Driver.Check(MechStorage.Activate(repairCapsule, repairActor) && repairMech.Spawned
            && repairMech.Position == repairCapsule.Position, "A stale map-owner entry blocked release.");
        Driver.Check(Driver.Map.spawnedThings.Contains(repairMech) && Driver.Errors.Count == 0,
            "Stale map-owner entry was not repaired: " + string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: released mech stays registered in the map's own thing owner, at the capsule cell; stale map entries repaired.");

        Driver.ResetMap();
        Pawn moveActor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn moveMech = Driver.NewPawn();
        Driver.Check(MechStorage.Store(moveActor, moveMech), "Migrated-capsule setup could not store the mech.");
        var moveCapsule = (Building_MechCapsule)moveMech.holdingOwner.Owner;
        var destination = new IntVec3(16, 0, 16);
        Driver.Set(typeof(Thing), "positionInt", moveCapsule, destination);
        Driver.Check(MechStorage.Activate(moveCapsule, moveActor) && moveMech.Position == destination
            && moveMech.pather.nextCell == destination,
            "A released mech would path from its old cell after the capsule moved.");
        Console.WriteLine("PASS: a released mech resets its pathfollower to the capsule's new cell.");
        Console.WriteLine("PASS: injected acceptance, capsule/pawn spawn and post-owner-removal failures; original-link rollback, durable inventory retention, same-pawn recontainment and retry; lost container link released; empty capsule rejected without throwing.");
    }

    private static void Autonomous()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Driver.Bandwidth[actor] = 0;
        Pawn drone = Driver.NewPawn(subject: false);
        drone.relations = null!;
        var droneComp = new CompDrone { parent = drone, props = new CompProperties_Drone() };
        Driver.Set(typeof(CompDrone), "parentPlatform", droneComp, actor);
        var cell = new CompMechPowerCell { parent = drone, props = new CompProperties_MechPowerCell { totalPowerTicks = 1000 } };
        Driver.Set(typeof(CompMechPowerCell), "powerTicksLeft", cell, 77);
        Comps(drone).AddRange(new ThingComp[] { droneComp, cell });
        Driver.Check(MechStorage.Store(actor, drone), "Subject-less drone storage failed: " + MechStorage.CanStore(actor, drone).Reason
            + $" | edifice={Driver.Map.edificeGrid[drone.Position]?.GetType().Name ?? "none"}, walkable={drone.Position.Walkable(Driver.Map)}");
        var capsule = (Building_MechCapsule)drone.holdingOwner.Owner;
        Driver.Check(capsule.CanActivate(actor).Accepted, "Autonomous drone incorrectly requires free bandwidth.");
        capsule.ActivateMech(actor);
        Driver.Check(drone.Spawned && drone.relations == null && droneComp.Platform == actor && cell.PowerTicksLeft == 77
            && actor.mechanitor.UsedBandwidth == 0 && droneComp.HasPlatform, "Drone acquired an overseer, lost its platform or refilled its battery.");

        Pawn awake = Driver.NewPawn();
        var awakened = new CompDeadManSwitch { parent = awake, props = new CompProperties_DeadManSwitch(), woken = true };
        Comps(awake).Add(awakened);
        Driver.Check(MechStorage.Store(actor, awake), "Awakened mech storage failed.");
        capsule = (Building_MechCapsule)awake.holdingOwner.Owner;
        capsule.ActivateMech(actor);
        Driver.Check(awake.Spawned && awakened.woken && awake.GetOverseer() == null && actor.mechanitor.UsedBandwidth == 0, "Awakened mech lost autonomous state.");

        Pawn carrier = Driver.NewPawn();
        var platform = new CompMechPlatform { parent = carrier, props = new CompProperties_MechPlatform() };
        var resources = new ThingOwner<Thing>(platform, false, LookMode.Deep);
        ThingWithComps resource = Item("ValidationPlatformResource");
        resource.stackCount = 3;
        resources.TryAdd(resource);
        Driver.Set(typeof(CompMechPlatform), "innerContainer", platform, resources);
        Comps(carrier).Add(platform);
        Driver.Bandwidth[actor] = 10;
        Driver.Check(MechStorage.Store(actor, carrier), "Carrier mech storage failed.");
        capsule = (Building_MechCapsule)carrier.holdingOwner.Owner;
        capsule.ActivateMech(actor);
        Driver.Check(carrier.Spawned && platform.GetDirectlyHeldThings() == resources && resources.Contains(resource) && resource.stackCount == 3,
            "Actual platform PostSpawnSetup reset its retained resource container.");

        Pawn foreign = Driver.NewPawn("other.mod", subject: false);
        foreign.DeSpawn();
        var foreignCapsule = (Building_MechCapsule)ThingMaker.MakeThing(MechCapsuleUtility.GetCapsuleDefForMech(foreign));
        foreignCapsule.SetFaction(Driver.Player);
        foreignCapsule.TryAcceptMech(foreign);
        GenSpawn.Spawn(foreignCapsule, new IntVec3(16, 0, 16), Driver.Map);
        Driver.Bandwidth[actor] = 0;
        Driver.Check(!foreignCapsule.CanActivate(actor).Accepted, "Non-DMS capsule received the autonomous bandwidth bypass.");
        foreignCapsule.ActivateMech(actor);
        Driver.Check(foreignCapsule.Mech == foreign && !foreign.Spawned, "Non-DMS capsule no longer uses FFF's original activation path.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: subject-less/null-relations drone, platform ownership, real power-cell/platform PostSpawnSetup preservation, awakened independence and non-DMS original FFF behavior.");
    }

    private static void Commander()
    {
        foreach (bool fail in new[] { true, false })
        {
            Driver.ResetMap();
            Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
            Driver.Bandwidth[actor] = 0;
            var commander = (OverseerMech)Driver.NewPawn(type: typeof(OverseerMech));
            Pawn dummy = Driver.NewPawn("aoba.framework", mechanitor: true);
            dummy.DeSpawn();
            var command = new CompOverseer { parent = commander, props = new CompProperties_Overseer(), dummyPawn = dummy };
            Comps(commander).Add(command);
            Pawn first = Driver.NewPawn();
            Pawn second = Driver.NewPawn();
            dummy.relations.AddDirectRelation(PawnRelationDefOf.Overseer, first);
            dummy.relations.AddDirectRelation(PawnRelationDefOf.Overseer, second);
            if (fail) Driver.FailCapsuleSpawn = 1;
            bool stored = MechStorage.Store(actor, commander);
            if (fail)
            {
                Driver.Check(!stored && commander.Spawned && first.GetOverseer() == dummy && second.GetOverseer() == dummy,
                    "Failed commander storage disconnected its children.");
                continue;
            }
            Driver.Check(stored && first.GetOverseer() == null && second.GetOverseer() == null && dummy.mechanitor.UsedBandwidth == 0
                && dummy.mechanitor.ControlledPawns.Count == 0, "Stored commander retained a hidden subordinate control graph.");
            var capsule = (Building_MechCapsule)commander.holdingOwner.Owner;
            Driver.Check(capsule.CanActivate(actor).Accepted, "Independent commander incorrectly requires actor bandwidth.");
            capsule.ActivateMech(actor);
            Driver.Check(commander.Spawned && commander.Comp.dummyPawn == dummy && command.activeInt && commander.GetOverseer() == null,
                "Commander lost its dummy/active state or acquired an inappropriate overseer.");
            command.Connect(first);
            Driver.Check(first.GetOverseer() == dummy, "Restored commander cannot reconnect an ordinary subordinate.");
            Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        }
        Console.WriteLine("PASS: real OverseerMech/CompOverseer failure preservation, success-only subordinate retirement, independent restoration and native reconnection.");
    }

    private static void MinifyAndReinstall()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        Driver.Check(MechStorage.Store(actor, mech), "Minify setup failed.");
        var capsule = (Building_MechCapsule)mech.holdingOwner.Owner;
        Graphic graphic = capsule.Graphic;
        string name = mech.LabelCap;
        MinifiedThing mini = capsule.MakeMinified();
        Driver.Check(mini.InnerThing == capsule && capsule.Mech == mech && !capsule.Spawned && mini.LabelNoCount == name
            && ReferenceEquals(capsule.Graphic, graphic), "Native minification changed the contained graph, name or body graphic.");
        var blueprint = new Blueprint_Install();
        Driver.Set(typeof(Blueprint_Install), "miniToInstall", blueprint, mini);
        Driver.Set(typeof(Thing), "mapIndexOrState", blueprint, (sbyte)0);
        object?[] args = { false };
        Thing restored = (Thing)AccessTools.DeclaredMethod(typeof(Blueprint_Install), "MakeSolidThing").Invoke(blueprint, args)!;
        Driver.Check(restored == capsule && mini.Destroyed && capsule.holdingOwner == null && capsule.Mech == mech,
            "Native installation did not detach the same capsule from its minified wrapper.");
        GenSpawn.Spawn(restored, new IntVec3(15, 0, 15), Driver.Map);
        capsule.ActivateMech(actor);
        Driver.Check(mech.Spawned && mech.Name.ToStringFull == name && !mech.Destroyed, "Reinstalled capsule could not restore the same named pawn.");
        var other = (MinifiedThing)ThingMaker.MakeThing(Driver.MiniDef);
        ThingWithComps chair = Item("ordinary chair");
        other.InnerThing = chair;
        Driver.Check(other.LabelNoCount == chair.LabelNoCount, "Unrelated minified item label changed.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: actual FFF Graphic/Label, native minify and Blueprint_Install.MakeSolidThing retain the same capsule/pawn; carried name and unrelated labels preserved.");
    }

    private static List<ThingComp> Comps(ThingWithComps thing) => (List<ThingComp>)AccessTools.Field(typeof(ThingWithComps), "comps").GetValue(thing)!;

    private static ThingWithComps Item(string name)
    {
        var thing = new ThingWithComps
        {
            def = new ThingDef { defName = name.Replace(" ", ""), label = name, category = ThingCategory.Item, thingClass = typeof(ThingWithComps), stackLimit = 75, useHitPoints = false }
        };
        Driver.Set(typeof(ThingWithComps), "comps", thing, new List<ThingComp>());
        return thing;
    }
}
