using System.Collections.Generic;
using System.Linq;
using Fortified;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WhiteGiver.DMSMechStorage;

/// <summary>
/// Rename, inspect and re-equip the pawn held inside a spawned DMS capsule. The capsule draws the
/// pawn's primary weapon beside it, so all three operations act on the same pawn equipment tracker.
/// </summary>
public static class StoredMechWeapons
{
    public static bool TryGetStoredMech(Building_MechCapsule capsule, out Pawn mech)
    {
        mech = null!;
        if (!capsule.Spawned || !MechStorage.IsDmsCapsule(capsule)) return false;
        Pawn? stored = capsule.Mech;
        if (stored == null || stored.Dead || stored.equipment == null) return false;
        mech = stored;
        return true;
    }

    public static string WeaponLabel(ThingWithComps weapon) => weapon.LabelNoCount.CapitalizeFirst();

    public static string EquipmentLabel(Pawn mech) =>
        mech.equipment?.Primary is ThingWithComps weapon
            ? (weapon.def.destroyOnDrop
                ? "DMSMS_EquippedBuiltIn".Translate(WeaponLabel(weapon))
                : "DMSMS_Equipped".Translate(WeaponLabel(weapon)))
            : "DMSMS_Equipped".Translate("DMSMS_NoWeapon".Translate());

    private static bool CanStoreWeapon(Building_MechCapsule capsule, ThingWithComps? weapon) =>
        weapon != null && !weapon.Destroyed && weapon.Spawned && weapon.Map == capsule.Map
        && weapon.def.IsWeapon && weapon.def.equipmentType == EquipmentType.Primary && !weapon.def.destroyOnDrop;

    public static void OpenRename(Building_MechCapsule capsule)
    {
        if (!TryGetStoredMech(capsule, out Pawn mech)) return;
        Find.WindowStack.Add(mech.NamePawnDialog());
    }

    public static bool Unequip(Building_MechCapsule capsule)
    {
        if (!TryGetStoredMech(capsule, out Pawn mech)) return false;
        ThingWithComps? weapon = mech.equipment!.Primary;
        if (weapon == null) return false;
        if (weapon.def.destroyOnDrop
            || !mech.equipment.TryDropEquipment(weapon, out _, capsule.Position, forbid: false))
        {
            MechStorage.Reject("DMSMS_UnequipFailed".Translate(), capsule);
            return false;
        }
        return true;
    }

    public static bool Equip(Building_MechCapsule capsule, ThingWithComps? weapon)
    {
        if (!TryGetStoredMech(capsule, out Pawn mech) || !CanStoreWeapon(capsule, weapon))
        {
            MechStorage.Reject("DMSMS_EquipFailed".Translate(), capsule);
            return false;
        }
        ThingWithComps? previous = mech.equipment!.Primary;
        if (previous != null && previous != weapon && !Unequip(capsule)) return false;
        weapon!.DeSpawn(DestroyMode.Vanish);
        mech.equipment.AddEquipment(weapon);
        if (mech.equipment.Primary == weapon) return true;
        // AddEquipment only refuses when a primary is already present; put the item back rather than eat it.
        if (!weapon.Spawned && !weapon.Destroyed)
            GenPlace.TryPlaceThing(weapon, capsule.Position, capsule.Map, ThingPlaceMode.Near);
        MechStorage.Reject("DMSMS_EquipFailed".Translate(), capsule);
        return false;
    }

    public static void BeginEquipTargeting(Building_MechCapsule capsule)
    {
        if (!TryGetStoredMech(capsule, out _)) return;
        var parameters = new TargetingParameters
        {
            canTargetPawns = false,
            canTargetBuildings = false,
            canTargetItems = true,
            canTargetLocations = false,
            canTargetFires = false,
            canTargetPlants = false,
            canTargetCorpses = false,
            canTargetEntities = false,
            mapObjectTargetsMustBeAutoAttackable = false,
            validator = target => target.Thing is ThingWithComps weapon && CanStoreWeapon(capsule, weapon)
        };
        Find.Targeter.BeginTargeting(parameters, target => Equip(capsule, target.Thing as ThingWithComps),
            caster: null, actionWhenFinished: null, mouseAttachment: null, requiresCastedSelected: false);
    }

    public static IEnumerable<Gizmo> Gizmos(Building_MechCapsule capsule)
    {
        if (!TryGetStoredMech(capsule, out Pawn mech)) yield break;
        yield return new Command_Action
        {
            defaultLabel = "Rename".Translate(),
            defaultDesc = "DMSMS_RenameDesc".Translate(mech.LabelCap),
            icon = TexButton.Rename,
            action = () => OpenRename(capsule)
        };
        ThingWithComps? weapon = mech.equipment!.Primary;
        if (weapon != null && !weapon.def.destroyOnDrop)
        {
            yield return new Command_Action
            {
                defaultLabel = "DMSMS_Unequip".Translate(WeaponLabel(weapon)),
                defaultDesc = "DMSMS_UnequipDesc".Translate(WeaponLabel(weapon)),
                icon = TexButton.Drop,
                action = () => Unequip(capsule)
            };
        }
        yield return new Command_Action
        {
            defaultLabel = "DMSMS_EquipWeapon".Translate(),
            defaultDesc = "DMSMS_EquipWeaponDesc".Translate(),
            icon = TexCommand.Install,
            action = () => BeginEquipTargeting(capsule)
        };
    }

    public static IEnumerable<FloatMenuOption> FloatMenuOptions(Building_MechCapsule capsule)
    {
        if (!TryGetStoredMech(capsule, out Pawn mech)) yield break;
        ThingWithComps? weapon = mech.equipment!.Primary;
        if (weapon != null && !weapon.def.destroyOnDrop)
            yield return new FloatMenuOption("DMSMS_Unequip".Translate(WeaponLabel(weapon)), () => Unequip(capsule));
        yield return new FloatMenuOption("DMSMS_EquipWeapon".Translate(), () => BeginEquipTargeting(capsule));
    }
}

[HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetGizmos))]
public static class CapsuleEquipmentGizmos
{
    public static void Postfix(ThingWithComps __instance, ref IEnumerable<Gizmo> __result)
    {
        if (__instance is Building_MechCapsule capsule)
            __result = StoredMechWeapons.Gizmos(capsule).Concat(__result ?? Enumerable.Empty<Gizmo>());
    }
}

[HarmonyPatch(typeof(Building_MechCapsule), nameof(Building_MechCapsule.GetFloatMenuOptions))]
public static class CapsuleEquipmentFloatMenu
{
    public static void Postfix(Building_MechCapsule __instance, ref IEnumerable<FloatMenuOption> __result)
    {
        __result = StoredMechWeapons.FloatMenuOptions(__instance)
            .Concat(__result ?? Enumerable.Empty<FloatMenuOption>());
    }
}

[HarmonyPatch(typeof(Building_MechCapsule), nameof(Building_MechCapsule.GetInspectString))]
public static class CapsuleEquipmentInspect
{
    public static void Postfix(Building_MechCapsule __instance, ref string __result)
    {
        if (!StoredMechWeapons.TryGetStoredMech(__instance, out Pawn mech)) return;
        string line = StoredMechWeapons.EquipmentLabel(mech);
        __result = __result.NullOrEmpty() ? line : __result + "\n" + line;
    }
}

/// <summary>
/// Vanilla only offers its title-bar rename button for selected pawns. Mirror that button for a
/// selected DMS capsule and reuse the mech's own naming dialog.
/// </summary>
[HarmonyPatch(typeof(MainTabWindow_Inspect), nameof(MainTabWindow_Inspect.DoInspectPaneButtons))]
public static class CapsuleRenameButton
{
    public static void Postfix(Rect rect, ref float lineEndWidth)
    {
        if (Find.Selector.SingleSelectedThing is not Building_MechCapsule capsule
            || !StoredMechWeapons.TryGetStoredMech(capsule, out Pawn mech)) return;
        RenameUIUtility.DrawRenameButton(new Rect(rect.width - 78f, 0f, 30f, 30f), mech);
        lineEndWidth += 30f;
    }
}
