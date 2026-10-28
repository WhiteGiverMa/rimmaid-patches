using System;
using System.Collections.Generic;
using Fortified;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using WhiteGiver.DMSMechStorage;

internal static class FollowupRegression
{
    private static readonly List<(Thing Weapon, Vector3 Position, float Angle)> Drawn = new();

    internal static void Run(Harmony qa)
    {
        qa.Patch(AccessTools.DeclaredMethod(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming)),
            prefix: new HarmonyMethod(typeof(FollowupRegression), nameof(Record)));
        CindersScope();
        StoredWeaponDrawing();
    }

    private static void CindersScope()
    {
        Driver.ResetMap();
        Driver.Check(MechStorage.IsDmsMech(Cinders("Cinder_Mech_Krepost", true)), "Cinders Tombstone mech was not included.");
        Driver.Check(MechStorage.IsDmsMech(Cinders("Cinder_Mech_Tombstone", true)), "Cinders imitation Tombstone was not included.");
        Driver.Check(!MechStorage.IsDmsMech(Cinders("Cinder_Mech_WarAttacker", false)),
            "Cinders built-in-turret mech was included without the DMS heavy-equipment marker.");
        Driver.Check(!MechStorage.IsDmsMech(Cinders("Cinder_Mech_ErodedEXO", false)), "Cinders eroded exoskeleton was included.");
        Driver.Check(!MechStorage.IsDmsMech(Driver.NewPawn("BreadMo.Cinders", mechanitor: true)), "Cinders humanlike race was included.");
        Pawn foreign = Driver.NewPawn("other.mod", type: typeof(WeaponUsableMech));
        foreign.def.modExtensions = new List<DefModExtension> { new MechWeaponExtension() };
        Driver.Check(!MechStorage.IsDmsMech(foreign), "Unrelated mod mech with FFF heavy equipment was included.");
        Console.WriteLine("PASS: Cinders scope follows that mod's own DMS marker (Tombstone + imitation), not the whole Cinders package.");
    }

    private static Pawn Cinders(string defName, bool dmsMarked)
    {
        Pawn pawn = Driver.NewPawn("BreadMo.Cinders", type: dmsMarked ? typeof(WeaponUsableMech) : typeof(Pawn));
        pawn.def.defName = defName;
        if (dmsMarked) pawn.def.modExtensions = new List<DefModExtension> { new MechWeaponExtension() };
        return pawn;
    }

    private static void StoredWeaponDrawing()
    {
        Driver.ResetMap();
        Pawn actor = Driver.NewPawn("ludeon.rimworld", mechanitor: true);
        Pawn mech = Driver.NewPawn();
        ThingWithComps weapon = Item("ValidationRanged");
        weapon.def.equipmentType = EquipmentType.Primary;
        mech.equipment.GetDirectlyHeldThings().TryAdd(weapon);
        Graphic? graphic = weapon.Graphic;
        Driver.Check(MechStorage.Store(actor, mech), "Weapon-draw setup could not seal the mech.");
        var capsule = (Building_MechCapsule)mech.holdingOwner.Owner;
        capsule.thingIDNumber = 424242;
        var origin = new Vector3(10.5f, 4f, 10.5f);

        foreach (Rot4 rotation in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
        {
            capsule.Rotation = rotation;
            Driver.Check(StoredWeaponDraw.TryGet(capsule, origin, out Vector3 position, out float angle),
                "Stored weapon parameters were not produced.");
            Drawn.Clear();
            StoredWeaponDraw.Prefix(capsule, DrawPhase.Draw, origin);
            Driver.Check(Drawn.Count == 1 && Drawn[0].Weapon == weapon && Drawn[0].Position == position
                && Math.Abs(Drawn[0].Angle - angle) < 0.001f, "Draw prefix did not call the native helper with computed parameters.");
            Driver.Check(position != origin && position.y != origin.y && Math.Abs(angle) < 360f,
                "Weapon ground position or angle was not applied for " + rotation);
            StoredWeaponDraw.TryGet(capsule, origin, out Vector3 again, out float againAngle);
            Driver.Check(again == position && againAngle == angle, "Weapon scatter is not deterministic across calls.");
        }

        Drawn.Clear();
        StoredWeaponDraw.Prefix(capsule, DrawPhase.ParallelPreDraw, origin);
        Driver.Check(Drawn.Count == 0, "Weapon was drawn outside the Draw phase.");

        Driver.Check(ReferenceEquals(weapon.Graphic, graphic) && !weapon.Spawned && !weapon.Destroyed
            && mech.equipment.Primary == weapon, "Weapon drawing mutated the stored weapon item.");

        Pawn noWeapon = Driver.NewPawn(at: new IntVec3(14, 0, 14));
        Driver.Check(MechStorage.Store(actor, noWeapon), "Weaponless setup could not seal the mech.");
        var plainCapsule = (Building_MechCapsule)noWeapon.holdingOwner.Owner;
        Drawn.Clear();
        StoredWeaponDraw.Prefix(plainCapsule, DrawPhase.Draw, origin);
        Driver.Check(Drawn.Count == 0 && !StoredWeaponDraw.TryGet(plainCapsule, origin, out _, out _),
            "Weapon was drawn for a mech without a primary weapon.");

        Pawn foreign = Driver.NewPawn("other.mod", at: new IntVec3(18, 0, 18));
        ThingWithComps foreignWeapon = Item("ForeignRanged");
        foreignWeapon.def.equipmentType = EquipmentType.Primary;
        foreign.equipment.GetDirectlyHeldThings().TryAdd(foreignWeapon);
        var foreignCapsule = MechCapsuleUtility.DeactivateMech(foreign);
        Driver.Check(foreignCapsule.Spawned && foreignCapsule.Mech == foreign, "Foreign capsule setup failed.");
        Drawn.Clear();
        StoredWeaponDraw.Prefix(foreignCapsule, DrawPhase.Draw, origin);
        Driver.Check(Drawn.Count == 0, "Weapon was drawn for a non-DMS capsule.");
        Console.WriteLine("PASS: native DrawEquipmentAiming receives AncientCorps offsets for all rotations, deterministic scatter, phase/DMS/weapon gating; stored item untouched.");
    }

    private static ThingWithComps Item(string name)
    {
        var thing = new ThingWithComps
        {
            def = new ThingDef
            {
                defName = name, label = name, category = ThingCategory.Item,
                thingClass = typeof(ThingWithComps), stackLimit = 1, useHitPoints = false
            }
        };
        Driver.Set(typeof(ThingWithComps), "comps", thing, new List<ThingComp>());
        return thing;
    }

    public static bool Record(Thing eq, Vector3 drawLoc, float aimAngle)
    {
        Drawn.Add((eq, drawLoc, aimAngle));
        return false;
    }
}
