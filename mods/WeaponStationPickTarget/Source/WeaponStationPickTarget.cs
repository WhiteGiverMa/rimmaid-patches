using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using WeaponStation;

namespace Meidocho.WeaponStationPickTarget;

public sealed class WeaponStationPickTargetMod : Mod
{
    public const string HarmonyId = "meidocho.weaponstation.picktarget";

    public WeaponStationPickTargetMod(ModContentPack content) : base(content)
    {
        Apply(new Harmony(HarmonyId));
    }

    /// <summary>
    /// Registers the gizmo replacement for both the base and Combat Extended station classes.
    /// The CE type is resolved by name so this assembly still loads when CE is absent.
    /// </summary>
    public static void Apply(Harmony harmony)
    {
        PatchStationType(harmony, "WeaponStation.Building_WeaponStation");
        PatchStationType(harmony, "WeaponStation_CE.Building_WeaponStationCE");
    }

    private static void PatchStationType(Harmony harmony, string typeName)
    {
        Type? type = AccessTools.TypeByName(typeName);
        if (type == null)
        {
            Log.Message($"[Weapon Station Pick Target] {typeName} is not loaded; skipped.");
            return;
        }

        MethodInfo? target = AccessTools.Method(type, "GetGizmos");
        if (target == null)
        {
            Log.Error($"[Weapon Station Pick Target] {typeName}.GetGizmos was not found; the left-click picker is unavailable for this station type.");
            return;
        }

        harmony.Patch(target, postfix: new HarmonyMethod(typeof(WeaponStationPickTargetMod), nameof(Postfix_GetGizmos)));
        Log.Message($"[Weapon Station Pick Target] Patched {typeName}.GetGizmos.");
    }

    private static void Postfix_GetGizmos(object __instance, ref IEnumerable<Gizmo> __result)
    {
        if (__instance is not IWeaponStation station || __result == null)
        {
            return;
        }

        __result = WrapGizmos(__result, station);
    }

    private static IEnumerable<Gizmo> WrapGizmos(IEnumerable<Gizmo> gizmos, IWeaponStation station)
    {
        string installLabel = "VortexWS_InstallWeapon".Translate();
        foreach (Gizmo gizmo in gizmos)
        {
            if (gizmo is Command_Action original && original.Label == installLabel && original.action != null)
            {
                Action<Thing>? setter = ResolvePendingSetter(station);
                if (setter == null)
                {
                    // Fall back to the original menu rather than leaving an inert button.
                    yield return original;
                    continue;
                }

                yield return new Command_InstallWeapon(station, original, setter);
            }
            else
            {
                yield return gizmo;
            }
        }
    }

    private static readonly Dictionary<Type, MethodInfo?> PendingSetters = new();

    private static Action<Thing>? ResolvePendingSetter(IWeaponStation station)
    {
        Type type = station.GetType();
        if (!PendingSetters.TryGetValue(type, out MethodInfo? method))
        {
            method = AccessTools.Method(type, "SetPendingInstall", new[] { typeof(Thing) });
            PendingSetters[type] = method;
            if (method == null)
            {
                Log.Error($"[Weapon Station Pick Target] {type.FullName}.SetPendingInstall(Thing) was not found; the original install menu is kept.");
            }
        }

        if (method == null)
        {
            return null;
        }

        return weapon => method.Invoke(station, new object[] { weapon });
    }

    internal static HashSet<Thing> ClaimedWeapons(IWeaponStation station)
    {
        Map? map = station.Thing.Map;
        if (map == null)
        {
            return new HashSet<Thing>();
        }

        return map.listerBuildings.allBuildingsColonist
            .OfType<IWeaponStation>()
            .Where(other => !ReferenceEquals(other, station))
            .Select(other => other.PendingWeapon)
            .OfType<Thing>()
            .ToHashSet();
    }

    internal static bool IsValidWeapon(IWeaponStation station, Thing? thing, HashSet<Thing> claimed)
    {
        if (thing == null)
        {
            return false;
        }

        Map? map = station.Thing.Map;
        if (map == null || thing.Map != map || !thing.Spawned)
        {
            return false;
        }

        ThingDef def = thing.def;
        if (def == null || def.category != ThingCategory.Item || !def.IsRangedWeapon)
        {
            return false;
        }

        if (def == station.Def.building.turretGunDef || claimed.Contains(thing))
        {
            return false;
        }

        return !ForbidUtility.IsForbidden(thing, Faction.OfPlayer);
    }

    internal static bool HasAnyWeapon(IWeaponStation station, HashSet<Thing> claimed)
    {
        Map? map = station.Thing.Map;
        if (map == null)
        {
            return false;
        }

        foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon))
        {
            if (IsValidWeapon(station, thing, claimed))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class Command_InstallWeapon : Command_Target
{
    private readonly IWeaponStation station;
    private readonly Action openMenu;

    public Command_InstallWeapon(IWeaponStation station, Command_Action original, Action<Thing> setter)
    {
        this.station = station;
        openMenu = original.action;

        defaultLabel = original.defaultLabel;
        defaultDesc = original.defaultDesc;
        defaultDescPostfix = original.defaultDescPostfix + "VortexWS_PickTargetDescPostfix".Translate();
        mouseText = original.mouseText;
        icon = original.icon;
        iconAngle = original.iconAngle;
        iconProportions = original.iconProportions;
        iconTexCoords = original.iconTexCoords;
        iconDrawScale = original.iconDrawScale;
        iconOffset = original.iconOffset;
        defaultIconColor = original.defaultIconColor;
        hotKey = original.hotKey;
        activateSound = original.activateSound;
        groupKey = original.groupKey;
        groupKeyIgnoreContent = original.groupKeyIgnoreContent;
        tutorTag = original.tutorTag;
        groupable = original.groupable;
        alsoClickIfOtherInGroupClicked = original.alsoClickIfOtherInGroupClicked;
        hideMouseIcon = original.hideMouseIcon;
        overrideMaterial = original.overrideMaterial;
        Order = original.Order;
        disabled = original.Disabled;
        disabledReason = original.disabledReason;
        shrinkable = false;

        HashSet<Thing> claimed = WeaponStationPickTargetMod.ClaimedWeapons(station);
        targetingParams = new TargetingParameters
        {
            canTargetLocations = false,
            canTargetPawns = false,
            canTargetBuildings = false,
            canTargetItems = true,
            canTargetFires = false,
            canTargetPlants = false,
            canTargetAnimals = false,
            canTargetHumans = false,
            canTargetMechs = false,
            canTargetSubhumans = false,
            canTargetEntities = false,
            canTargetCorpses = false,
            mapObjectTargetsMustBeAutoAttackable = false,
            thingCategory = ThingCategory.Item,
            validator = target => WeaponStationPickTargetMod.IsValidWeapon(station, target.Thing, claimed)
        };
        action = target =>
        {
            if (target.Thing is Thing weapon)
            {
                setter(weapon);
            }
        };
    }

    public override void ProcessInput(Event ev)
    {
        HashSet<Thing> claimed = WeaponStationPickTargetMod.ClaimedWeapons(station);
        if (!WeaponStationPickTargetMod.HasAnyWeapon(station, claimed))
        {
            Messages.Message("VortexWS_NoWeapon".Translate(), MessageTypeDefOf.RejectInput, false);
            return;
        }

        base.ProcessInput(ev);
    }

    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
    {
        GizmoResult result = base.GizmoOnGUI(topLeft, maxWidth, parms);
        if (result.State != GizmoState.OpenedFloatMenu)
        {
            return result;
        }

        // RimWorld 1.7 falls back to the left-click action when a gizmo has no right-click
        // options. Intercept that state and open the original weapon list directly instead.
        result.InteractEvent?.Use();
        openMenu();
        return new GizmoResult(GizmoState.Mouseover, result.InteractEvent);
    }
}
