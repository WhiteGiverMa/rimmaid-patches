using System;
using System.Collections.Generic;
using System.Reflection;
using DMSE;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSEMissileBaseFix;

internal static class PatchInstaller
{
    private static bool applied;
    private static readonly List<string> installed = new();

    internal static IReadOnlyList<string> Installed => installed;

    internal static void Reset()
    {
        applied = false;
        installed.Clear();
    }

    internal static int Apply()
    {
        if (applied)
        {
            return installed.Count;
        }

        applied = true;
        installed.Clear();

        Harmony harmony = new(DMSEMissileBaseFixMod.HarmonyId);
        harmony.UnpatchAll(DMSEMissileBaseFixMod.HarmonyId);

        TryInstall("MapParent.CanReformFoggedEnemies", () => InstallCanReformFoggedEnemies(harmony));
        TryInstall("Building_Turret.ThreatDisabled", () => InstallTurretThreatDisabled(harmony));
        TryInstall("CaravanFormingUtility.AllReachableColonyItems", () => InstallCompStorageLoot(harmony));

        return installed.Count;
    }

    private static void TryInstall(string label, Action action)
    {
        try
        {
            action();
            installed.Add(label);
        }
        catch (Exception exception)
        {
            Log.Error($"{DMSEMissileBaseFixMod.LogPrefix} skipped '{label}': {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void InstallCanReformFoggedEnemies(Harmony harmony)
    {
        MethodInfo getter = AccessTools.PropertyGetter(typeof(WorldObject), nameof(WorldObject.CanReformFoggedEnemies))
            ?? throw new MissingMethodException(typeof(WorldObject).FullName, "get_CanReformFoggedEnemies");

        harmony.Patch(
            getter,
            postfix: new HarmonyMethod(typeof(MissileBasePatches), nameof(MissileBasePatches.CanReformFoggedEnemiesPostfix)));
    }

    private static void InstallTurretThreatDisabled(Harmony harmony)
    {
        MethodInfo target = AccessTools.DeclaredMethod(
            typeof(Building_Turret),
            nameof(Building_Turret.ThreatDisabled),
            new[] { typeof(IAttackTargetSearcher) })
            ?? throw new MissingMethodException(typeof(Building_Turret).FullName, nameof(Building_Turret.ThreatDisabled));

        harmony.Patch(
            target,
            postfix: new HarmonyMethod(typeof(MissileBasePatches), nameof(MissileBasePatches.ThreatDisabledPostfix)));
    }

    private static void InstallCompStorageLoot(Harmony harmony)
    {
        MethodInfo target = AccessTools.DeclaredMethod(
            typeof(CaravanFormingUtility),
            nameof(CaravanFormingUtility.AllReachableColonyItems),
            new[] { typeof(Map), typeof(bool), typeof(bool), typeof(bool) })
            ?? throw new MissingMethodException(typeof(CaravanFormingUtility).FullName, nameof(CaravanFormingUtility.AllReachableColonyItems));

        harmony.Patch(
            target,
            postfix: new HarmonyMethod(typeof(MissileBasePatches), nameof(MissileBasePatches.AllReachableColonyItemsPostfix)));
    }
}

internal static class MissileBasePatches
{
    /// <summary>
    /// Matches vanilla Camp and CaravansBattlefield. DMSE's Site instances otherwise include every
    /// fogged dormant threat in FormCaravanComp.AnyActiveThreatNow, including unrelated threats that
    /// were generated inside still-hidden map areas.
    /// </summary>
    internal static void CanReformFoggedEnemiesPostfix(WorldObject __instance, ref bool __result)
    {
        if (!__result && __instance is MapParent parent && MissileBaseSiteUtility.IsMissileBaseSite(parent))
        {
            __result = true;
        }
    }

    /// <summary>
    /// Vanilla ThreatDisabled checks power/manning/dormancy but not whether the gun has the fuel its
    /// AttackVerb needs for even one shot. On a DMSE missile-base map only, a genuinely dry gun is no
    /// longer allowed to block caravan reformation forever. A loaded operational SAM or fueled CIWS
    /// remains protected, so Field Air-Defense cannot be disarmed remotely merely by emptying its gun.
    /// </summary>
    internal static void ThreatDisabledPostfix(Building_Turret __instance, ref bool __result)
    {
        if (__result
            || __instance is not Building_TurretGun turret
            || !MissileBaseSiteUtility.IsMissileBaseMap(turret.MapHeld)
            || MissileBaseSiteUtility.HasOperationalAirDefense(turret))
        {
            return;
        }

        CompRefuelable? refuelable = turret.refuelableComp ?? turret.GetComp<CompRefuelable>();
        if (refuelable == null)
        {
            return;
        }

        Verb? attackVerb = turret.AttackVerb;
        float fuelPerShot = attackVerb?.verbProps.consumeFuelPerShot ?? 0f;
        if (fuelPerShot > 0f && refuelable.Fuel < fuelPerShot)
        {
            __result = true;
        }
    }

    /// <summary>
    /// CompMissileRackStorage is an IThingHolder but not an IHaulSource. Vanilla therefore omits its
    /// contents from caravan/transporter item lists, and simply adding the unspawned objects to a list
    /// would still leave GenClosest unable to find them for the actual gathering job. Once the storage
    /// is safe to access, move eligible contents through the real ThingOwner.TryDrop path. They become
    /// ordinary ground loot, so Dialog_FormCaravan, Dialog_LoadTransporters and both hauling pipelines
    /// all work without a parallel custom job implementation.
    /// </summary>
    internal static void AllReachableColonyItemsPostfix(
        Map? map,
        bool allowEvenIfOutsideHomeArea,
        bool allowEvenIfReserved,
        ref List<Thing> __result)
    {
        if (!allowEvenIfOutsideHomeArea
            || map == null
            || __result == null
            || !MissileBaseSiteUtility.IsMissileBaseMap(map)
            || GenHostility.AnyHostileActiveThreatToPlayer(map, countDormantPawnsAsHostile: false, canBeFogged: false))
        {
            return;
        }

        HashSet<Thing>? existing = null;
        CollectFromBuildings(map, map.listerBuildings.allBuildingsNonColonist, allowEvenIfReserved, __result, ref existing);
        CollectFromBuildings(map, map.listerBuildings.allBuildingsColonist, allowEvenIfReserved, __result, ref existing);
    }

    private static void CollectFromBuildings(
        Map map,
        List<Building>? buildings,
        bool allowEvenIfReserved,
        List<Thing> result,
        ref HashSet<Thing>? existing)
    {
        if (buildings == null || buildings.Count == 0)
        {
            return;
        }

        for (int i = 0; i < buildings.Count; i++)
        {
            Building? building = buildings[i];
            if (building == null
                || !building.Spawned
                || building.Destroyed
                || building.Position.Fogged(map))
            {
                continue;
            }

            CompMissileRackStorage? storage = building.GetComp<CompMissileRackStorage>();
            if (storage == null)
            {
                continue;
            }

            if (building is IAttackTarget target && GenHostility.IsActiveThreatToPlayer(target))
            {
                continue;
            }

            if (building.HostileTo(Faction.OfPlayer) && MissileBaseSiteUtility.HasOperationalAirDefense(building))
            {
                continue;
            }

            ThingOwner? held = storage.HeldContainer;
            if (held == null || held.Count == 0)
            {
                continue;
            }

            for (int j = held.Count - 1; j >= 0; j--)
            {
                Thing? item = held[j];
                if (!IsEligibleStoredItem(map, item, allowEvenIfReserved))
                {
                    continue;
                }

                if (!held.TryDrop(
                    item,
                    building.Position,
                    map,
                    ThingPlaceMode.Near,
                    out Thing? dropped,
                    placedAction: null,
                    nearPlaceValidator: null,
                    playDropSound: false))
                {
                    continue;
                }

                if (dropped == null
                    || dropped.Destroyed
                    || !dropped.Spawned
                    || dropped.Map != map
                    || dropped.Position.Fogged(map))
                {
                    continue;
                }

                existing ??= new HashSet<Thing>(result);
                if (existing.Add(dropped))
                {
                    result.Add(dropped);
                }
            }
        }
    }

    private static bool IsEligibleStoredItem(Map map, Thing? item, bool allowEvenIfReserved)
    {
        if (item == null || item.Destroyed || item.def.category != ThingCategory.Item || !item.def.EverHaulable)
        {
            return false;
        }

        if (!item.GetInnerIfMinified().def.canLoadIntoCaravan)
        {
            return false;
        }

        return allowEvenIfReserved
            || map.reservationManager == null
            || Faction.OfPlayer == null
            || !map.reservationManager.IsReservedByAnyoneOf(item, Faction.OfPlayer);
    }
}
