using System;
using System.Collections.Generic;
using System.Linq;
using Fortified;
using RimWorld;
using UnityEngine;
using Verse;
using WhiteGiver.DMSMechStorage;

internal static class WeaponInteractionRegression
{
    internal static void Run()
    {
        InspectRenameAndMenu();
        EquipAndUnequip();
        ScopeAndRejections();
    }

    private static void InspectRenameAndMenu()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        ThingWithComps weapon = Weapon("ValidationRifle");
        mech.equipment.GetDirectlyHeldThings().TryAdd(weapon);
        Driver.Check(MechStorage.Store(actor, mech), "Equipment-inspect setup could not seal the mech.");
        var capsule = (Building_MechCapsule)mech.holdingOwner.Owner;

        string inspect = capsule.GetInspectString();
        Driver.Check(inspect.Contains("当前装备：") && inspect.Contains(weapon.LabelNoCount),
            "Inspect string did not report the stored weapon: " + inspect);

        List<FloatMenuOption> options = capsule.GetFloatMenuOptions(actor).ToList();
        FloatMenuOption unequip = options.Single(option => option.Label.StartsWith("卸下", StringComparison.Ordinal));
        FloatMenuOption equip = options.Single(option => option.Label == "为其配备武器...");
        Driver.Check(unequip.action != null && equip.action != null, "Capsule equipment menu entries were not actionable.");

        List<Gizmo> gizmos = StoredMechWeapons.Gizmos(capsule).ToList();
        Driver.Check(gizmos.Count == 3 && gizmos[0] is Command_Action && gizmos[1] is Command_Action && gizmos[2] is Command_Action,
            "Stored capsule did not expose rename/unequip/equip gizmos.");
        var commands = gizmos.Cast<Command_Action>().ToList();
        Driver.Check(commands[0].defaultLabel == "Rename" && commands[0].action != null,
            "Rename gizmo was missing or not actionable.");
        Driver.Check(commands[1].defaultLabel.StartsWith("卸下", StringComparison.Ordinal)
            && commands[1].defaultLabel.Contains(weapon.LabelNoCount), "Unequip gizmo did not name the weapon.");
        Driver.Check(commands[2].defaultLabel == "为其配备武器...", "Equip gizmo label changed.");

        mech.Name = new NameSingle("改名后的猎狙");
        Driver.Check(capsule.Label == mech.LabelCap, "Capsule label did not follow the renamed stored pawn.");
        MinifiedThing mini = capsule.MakeMinified();
        Driver.Check(mini.LabelNoCount == mech.LabelCap, "Minified capsule label did not follow the rename.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: inspect string, right-click equipment entries, rename/unequip/equip gizmos and renamed capsule/minified labels.");
    }

    private static void EquipAndUnequip()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        ThingWithComps first = Weapon("ValidationRifle");
        mech.equipment.GetDirectlyHeldThings().TryAdd(first);
        Driver.Check(MechStorage.Store(actor, mech), "Equip/unequip setup could not seal the mech.");
        var capsule = (Building_MechCapsule)mech.holdingOwner.Owner;
        var origin = new Vector3(10.5f, 4f, 10.5f);
        Driver.Check(StoredWeaponDraw.TryGet(capsule, origin, out _, out _), "Stored weapon was not drawn before unequip.");

        Driver.Check(StoredMechWeapons.Unequip(capsule), "Unequip failed: " + string.Join("\n", Driver.Errors));
        Driver.Check(mech.equipment.Primary == null && first.Spawned && first.Map == Driver.Map && !first.IsForbidden(actor),
            "Unequip did not drop the same weapon on the map.");
        Driver.Check(!StoredWeaponDraw.TryGet(capsule, origin, out _, out _), "Unequipped weapon was still drawn beside the capsule.");
        Driver.Check(capsule.GetInspectString().Contains("当前装备：无"), "Inspect string did not report an empty equipment slot.");

        ThingWithComps second = Weapon("ValidationBlade");
        GenSpawn.Spawn(second, new IntVec3(15, 0, 15), Driver.Map);
        Driver.Check(StoredMechWeapons.Equip(capsule, second), "Equip failed: " + string.Join("\n", Driver.Errors));
        Driver.Check(mech.equipment.Primary == second && !second.Spawned && second.holdingOwner?.Owner == mech.equipment,
            "Equip did not move the same weapon into the stored pawn's equipment.");
        Driver.Check(StoredWeaponDraw.TryGet(capsule, origin, out _, out _), "Newly equipped weapon was not drawn beside the capsule.");

        ThingWithComps third = Weapon("ValidationSidearm");
        GenSpawn.Spawn(third, new IntVec3(16, 0, 16), Driver.Map);
        Driver.Check(StoredMechWeapons.Equip(capsule, third), "Swap equip failed: " + string.Join("\n", Driver.Errors));
        Driver.Check(mech.equipment.Primary == third && second.Spawned && second.Map == Driver.Map && !second.IsForbidden(actor),
            "Equipping a replacement did not unequip the previous weapon.");
        Driver.Check(StoredMechWeapons.Equip(capsule, second), "Re-equipping the dropped weapon failed.");
        Driver.Check(mech.equipment.Primary == second && third.Spawned, "Second swap did not exchange the weapons.");

        mech.Name = new NameSingle("激活后仍叫猎狙");
        string name = mech.Name.ToStringFull;
        capsule.ActivateMech(actor);
        Driver.Check(mech.Spawned && mech.Name.ToStringFull == name && mech.equipment.Primary == second,
            "Activation lost the renamed pawn or its re-equipped weapon.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: same-item unequip/drop, equip, swap, draw gating, empty inspect line and activation retaining rename/weapon.");
    }

    private static void ScopeAndRejections()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        Driver.Check(MechStorage.Store(actor, mech), "Rejection setup could not seal the mech.");
        var capsule = (Building_MechCapsule)mech.holdingOwner.Owner;
        ThingWithComps cargo = Item("ValidationCargo");
        GenSpawn.Spawn(cargo, new IntVec3(14, 0, 14), Driver.Map);
        Driver.Check(!StoredMechWeapons.Equip(capsule, cargo) && cargo.Spawned && mech.equipment.Primary == null,
            "A non-weapon item was equipped or consumed.");
        ThingWithComps disposable = Weapon("ValidationDisposable");
        disposable.def.destroyOnDrop = true;
        GenSpawn.Spawn(disposable, new IntVec3(15, 0, 15), Driver.Map);
        Driver.Check(!StoredMechWeapons.Equip(capsule, disposable) && disposable.Spawned,
            "A destroy-on-drop weapon was accepted for storage.");
        Driver.Check(!StoredMechWeapons.Gizmos(capsule).Cast<Command_Action>()
            .Any(g => g.defaultLabel.StartsWith("卸下", StringComparison.Ordinal)), "Unequip was offered without a weapon.");

        Pawn foreign = Driver.NewPawn("other.mod", at: new IntVec3(18, 0, 18));
        var foreignCapsule = MechCapsuleUtility.DeactivateMech(foreign);
        Driver.Check(!StoredMechWeapons.TryGetStoredMech(foreignCapsule, out _)
            && !StoredMechWeapons.Gizmos(foreignCapsule).Any()
            && !StoredMechWeapons.FloatMenuOptions(foreignCapsule).Any(),
            "A non-DMS capsule received DMS equipment controls.");
        string inspect = foreignCapsule.GetInspectString() ?? string.Empty;
        Driver.Check(!inspect.Contains("当前装备："), "A non-DMS capsule received the DMS inspect line.");

        var empty = (Building_MechCapsule)ThingMaker.MakeThing(MechCapsuleUtility.GetCapsuleDefForMech(mech));
        empty.SetFaction(Driver.Player);
        GenSpawn.Spawn(empty, new IntVec3(20, 0, 20), Driver.Map);
        Driver.Check(!StoredMechWeapons.Gizmos(empty).Any() && !StoredMechWeapons.FloatMenuOptions(empty).Any(),
            "An empty capsule received equipment controls.");

        Pawn mounted = Driver.NewPawn(at: new IntVec3(23, 0, 23));
        ThingWithComps mountedGun = Weapon("ValidationMountedGun");
        mountedGun.def.destroyOnDrop = true;
        mounted.equipment.GetDirectlyHeldThings().TryAdd(mountedGun);
        Driver.Check(MechStorage.Store(actor, mounted), "Built-in weapon setup could not seal the mech.");
        var mountedCapsule = (Building_MechCapsule)mounted.holdingOwner.Owner;
        string mountedInspect = mountedCapsule.GetInspectString();
        Driver.Check(mountedInspect.Contains("内置武器：") && !mountedInspect.Contains("当前装备："),
            "A built-in mounted weapon was not labelled separately: " + mountedInspect);
        Driver.Check(!StoredMechWeapons.Gizmos(mountedCapsule).Cast<Command_Action>()
                .Any(g => g.defaultLabel.StartsWith("卸下", StringComparison.Ordinal))
            && !StoredMechWeapons.FloatMenuOptions(mountedCapsule).Any(o => o.Label.StartsWith("卸下", StringComparison.Ordinal)),
            "A built-in mounted weapon was offered for unequip.");

        Pawn removed = Driver.NewPawn(at: new IntVec3(22, 0, 22));
        Driver.Check(MechStorage.Store(actor, removed), "Removed-mech setup could not seal the mech.");
        var removedCapsule = (Building_MechCapsule)removed.holdingOwner.Owner;
        removedCapsule.innerContainer.Remove(removed);
        Driver.Check(!StoredMechWeapons.TryGetStoredMech(removedCapsule, out _), "A capsule without a mech was still treated as stored.");
        Driver.Check(Driver.Errors.Count == 0, string.Join("\n", Driver.Errors));
        Console.WriteLine("PASS: non-weapon/destroy-on-drop/empty/non-DMS/despawned rejections; no controls or inspect leakage.");
    }

    private static ThingWithComps Weapon(string name)
    {
        ThingWithComps thing = Item(name);
        thing.def.equipmentType = EquipmentType.Primary;
        thing.def.tools = new List<Tool> { new Tool() };
        return thing;
    }

    private static ThingWithComps Item(string name)
    {
        var thing = new ThingWithComps
        {
            def = new ThingDef
            {
                defName = name, label = name, category = ThingCategory.Item, thingClass = typeof(ThingWithComps),
                stackLimit = 1, useHitPoints = false
            }
        };
        Driver.Set(typeof(ThingWithComps), "comps", thing, new List<ThingComp>
        {
            new CompForbiddable { parent = thing, props = new CompProperties_Forbiddable() }
        });
        return thing;
    }
}
