using System;
using System.Collections.Generic;
using System.Linq;
using Fortified;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace WhiteGiver.DMSMechStorage;

public sealed class DMSMechStorageMod : Mod
{
    public DMSMechStorageMod(ModContentPack content) : base(content)
    {
        new Harmony("whitegiver.dms.mechstorage").PatchAll(typeof(DMSMechStorageMod).Assembly);
        Log.Message("[DMS Mech Storage] Mechanitor storage orders and scoped FFF capsule restoration enabled.");
    }
}

public static class MechStorage
{
    private static readonly HashSet<string> Packages = new(StringComparer.OrdinalIgnoreCase)
    {
        "aoba.deadmanswitch.core", "aoba.deadmanswitch.ancientcorps", "aoba.deadmanswitch.synthetic",
        "aoba.deadmanswitch.expedition", "aoba.deadmanswitch.taktikmagier", "aoba.deadmanswitch.mobiledragoon",
        "aoba.deadmanswitch.motorized", "srxf.deadmanswitch.legion", "dms.tritech", "dms.xivbattlegroup"
    };

    private const string CindersPackage = "BreadMo.Cinders";

    public static bool IsDmsMech(Pawn pawn) => pawn.RaceProps.IsMechanoid
        && pawn.def.modContentPack?.PackageIdPlayerFacing is string package
        && (Packages.Contains(package) || IsCindersDmsMech(pawn, package));

    // Cinders 自己的 DMS 兼容层把墓碑改成 FFF 重装备机兵；其余 Cinders 机械体只有内置炮塔。
    private static bool IsCindersDmsMech(Pawn pawn, string package) =>
        string.Equals(package, CindersPackage, StringComparison.OrdinalIgnoreCase)
        && pawn.def.HasModExtension<MechWeaponExtension>() && pawn is IWeaponUsable;

    public static bool IsDmsCapsule(Building_MechCapsule capsule) => capsule.Faction == Faction.OfPlayer
        && capsule.Mech is Pawn mech && mech.Faction == Faction.OfPlayer && IsDmsMech(mech);

    public static bool CanAct(Pawn actor) => actor.Spawned && !actor.Dead && !actor.Downed
        && actor.IsColonistPlayerControlled && MechanitorUtility.IsMechanitor(actor);

    public static bool TargetValid(Pawn actor, Pawn mech) => CanAct(actor) && IsDmsMech(mech)
        && mech.Spawned && !mech.Dead && mech.Faction == Faction.OfPlayer && mech.Map == actor.Map;

    public static AcceptanceReport CanStore(Pawn actor, Pawn mech)
    {
        if (!CanAct(actor)) return "DMSMS_ActorUnavailable".Translate();
        if (!TargetValid(actor, mech)) return "DMSMS_TargetUnavailable".Translate();
        if (mech.IsAttacking()) return "DMSMS_Fighting".Translate();
        if (mech.IsForbidden(actor)) return "Forbidden".Translate();
        ThingDef? capsuleDef = MechCapsuleUtility.GetCapsuleDefForMech(mech);
        if (capsuleDef == null || !ClearFootprint(capsuleDef, mech.Position, mech.Map, mech.Rotation, mech))
            return "DMSMS_NoSpace".Translate();
        if (!actor.CanReach(mech, PathEndMode.Touch, Danger.Deadly)) return "NoPath".Translate();
        if (!actor.CanReserve(mech)) return "Reserved".Translate();
        return true;
    }

    public static bool ClearFootprint(ThingDef def, IntVec3 cell, Map map, Rot4 rotation, Thing ignored)
    {
        foreach (IntVec3 occupied in GenAdj.OccupiedRect(cell, rotation, def.Size))
            if (!occupied.InBounds(map) || !occupied.Walkable(map)
                || map.edificeGrid[occupied] is Building building && building != ignored) return false;
        // The capsule itself occupies the release cell; every other edifice was rejected above.
        return GenSpawn.CanSpawnAt(def, cell, map, rotation, canWipeEdifices: true)
            && !GenSpawn.WouldWipeAnythingWith(cell, rotation, def, map,
                thing => thing != ignored && thing.def.category != ThingCategory.Filth);
    }

    public static bool Store(Pawn actor, Pawn mech)
    {
        AcceptanceReport report = CanStore(actor, mech);
        if (!report.Accepted)
        {
            Reject(report.Reason, actor);
            return false;
        }
        Map map = mech.Map;
        IntVec3 cell = mech.Position;
        Rot4 rotation = mech.Rotation;
        Pawn? previous = mech.GetOverseer();
        MechanitorControlGroup? group = previous?.mechanitor?.GetControlGroup(mech);
        Building_MechCapsule? capsule = null;
        try
        {
            capsule = MechCapsuleUtility.DeactivateMech(mech);
            if (capsule == null || !capsule.Spawned || capsule.Destroyed || capsule.Mech != mech
                || mech.holdingOwner != capsule.innerContainer)
                throw new InvalidOperationException("The original mech was not safely stored in a spawned FFF capsule.");
        }
        catch (Exception exception)
        {
            capsule ??= mech.holdingOwner?.Owner as Building_MechCapsule;
            Recover(actor, mech, capsule, map, cell, rotation, previous, group);
            Log.Error($"[DMS Mech Storage] Storage failed: {exception}");
            Reject("DMSMS_Failed".Translate(), actor);
            return false;
        }

        if (mech is IOverseer { Comp.dummyPawn: Pawn dummy })
        {
            dummy.mechanitor.UndraftAllMechs();
            foreach (Pawn subordinate in dummy.mechanitor.OverseenPawns.ToList())
            {
                dummy.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer, subordinate);
                if (subordinate.Spawned && !subordinate.Dead)
                    subordinate.OverseerSubject?.Notify_DisconnectedFromOverseer();
            }
            dummy.mechanitor.Notify_BandwidthChanged();
        }
        previous?.mechanitor?.Notify_BandwidthChanged();
        return true;
    }

    private static void Recover(Pawn actor, Pawn mech, Building_MechCapsule? capsule, Map map, IntVec3 cell,
        Rot4 rotation, Pawn? previous, MechanitorControlGroup? group)
    {
        if (capsule != null)
        {
            Detach(capsule, mech);
            if (!capsule.Destroyed) capsule.Destroy(DestroyMode.Vanish);
        }
        try
        {
            if (capsule == null)
            {
                ThingOwner? owner = mech.holdingOwner;
                if (owner != null && owner.Owner is not Map)
                {
                    if (owner.IndexOf(mech) >= 0)
                    {
                        mech.holdingOwner = owner;
                        owner.Remove(mech);
                    }
                    else
                    {
                        mech.holdingOwner = null;
                    }
                }
            }
            if (!mech.Spawned) GenSpawn.Spawn(mech, cell, map, rotation, WipeMode.VanishOrMoveAside, respawningAfterLoad: true);
        }
        catch (Exception exception)
        {
            Log.Error($"[DMS Mech Storage] Original-position recovery failed: {exception}");
        }
        if (mech.Spawned)
        {
            if (previous != null && mech.GetOverseer() != previous)
                previous.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            group?.Assign(mech);
        }
        else
        {
            // Keep the same pawn in an ordinary FFF capsule in the actor's persistent inventory if map spawning fails too.
            var retained = (Building_MechCapsule)ThingMaker.MakeThing(MechCapsuleUtility.GetCapsuleDefForMech(mech));
            retained.SetFaction(Faction.OfPlayer);
            if (!retained.TryAcceptMech(mech) || !actor.inventory.innerContainer.TryAdd(retained.MakeMinified()))
                throw new InvalidOperationException("Could not retain the mech after map recovery failed.");
        }
        previous?.mechanitor?.Notify_BandwidthChanged();
    }

    private static void Detach(Building_MechCapsule capsule, Pawn mech)
    {
        // A spawned thing is legitimately owned by Map.spawnedThings in 1.7; only container owners need clearing.
        ThingOwner? owner = mech.holdingOwner;
        if (owner != null && owner.Owner is not Map)
        {
            // Contains only compares the holder link and Remove would throw on a dangling link, so test the real list.
            if (owner.IndexOf(mech) >= 0)
            {
                mech.holdingOwner = owner;
                owner.Remove(mech);
            }
            else
            {
                mech.holdingOwner = null;
            }
        }
        // Repair the link while the capsule still lists the mech, then remove it for real.
        if (capsule.innerContainer.IndexOf(mech) >= 0)
        {
            if (mech.holdingOwner != capsule.innerContainer) mech.holdingOwner = capsule.innerContainer;
            capsule.innerContainer.Remove(mech);
        }
    }

    private static string Contents(Building_MechCapsule capsule)
    {
        var labels = new List<string>();
        for (int i = 0; i < capsule.innerContainer.Count; i++) labels.Add(capsule.innerContainer[i].LabelShort);
        return string.Join(",", labels);
    }

    public static bool RequiresEnrollment(Pawn mech) => mech.OverseerSubject != null
        && mech is not IStateControllableMech { ControllableByState: true }
        && mech.TryGetComp<CompDeadManSwitch>()?.woken != true;

    public static AcceptanceReport CanActivate(Building_MechCapsule capsule, Thing actor)
    {
        Pawn? mech = capsule.Mech;
        if (!capsule.Spawned || mech == null || mech.Dead || !actor.Spawned || actor.Faction != Faction.OfPlayer
            || actor.Map != capsule.Map || actor is Pawn { Downed: true } or Pawn { Dead: true }
            || Building_MechCapsule.ResolveController(actor) is not Pawn controller)
            return "DMSMS_ActorUnavailable".Translate();
        if (!ClearFootprint(mech.def, capsule.Position, capsule.Map, capsule.Rotation, capsule))
            return "DMSMS_NoSpace".Translate();
        if (RequiresEnrollment(mech))
        {
            float cost = mech.GetStatValue(StatDefOf.BandwidthCost);
            if (controller.mechanitor.TotalBandwidth - controller.mechanitor.UsedBandwidth < cost)
                return "FFF.Reason.NeedBandwidth".Translate(cost);
        }
        return true;
    }

    public static bool Activate(Building_MechCapsule capsule, Thing actor)
    {
        AcceptanceReport report = CanActivate(capsule, actor);
        if (!report.Accepted)
        {
            Reject(report.Reason, actor);
            return false;
        }
        Pawn mech = capsule.Mech;
        bool enroll = RequiresEnrollment(mech);
        IntVec3 cell = capsule.Position;
        Map map = capsule.Map;
        Rot4 rotation = capsule.Rotation;
        try
        {
            // Detach first so a container/holder desync cannot survive the spawn, then place at the capsule cell.
            Detach(capsule, mech);
            // A leftover spawned state from an earlier attempt can sit at the default cell (map corner).
            if (mech.Spawned) mech.DeSpawn(DestroyMode.Vanish);
            // Repair saves left corrupted by older builds: listed in the map owner without a live link.
            if (map.spawnedThings.IndexOf(mech) >= 0)
            {
                mech.holdingOwner = map.spawnedThings;
                map.spawnedThings.Remove(mech);
            }
            GenSpawn.Spawn(mech, cell, map, rotation, WipeMode.VanishOrMoveAside, respawningAfterLoad: true);
            if (!mech.Spawned || mech.Map != map || mech.Position != cell || mech.Destroyed || capsule.HasMech)
                throw new InvalidOperationException(
                    $"release failed: spawned={mech.Spawned}, pos={mech.Position}, destroyed={mech.Destroyed}, "
                    + $"registered={map.spawnedThings.Contains(mech)}, capsuleHasMech={capsule.HasMech}, "
                    + $"count={capsule.innerContainer.Count}, contents=[{Contents(capsule)}], "
                    + $"cell={cell}, map={map.uniqueID}");
            // respawningAfterLoad skips pather.ResetToCurrentPosition; a relocated pawn must not path from its old cell.
            mech.pather?.ResetToCurrentPosition();
            if (enroll)
            {
                Pawn controller = Building_MechCapsule.ResolveController(actor);
                if (actor is IOverseer overseer) overseer.Comp.Connect(mech);
                else controller.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
                controller.mechanitor.Notify_BandwidthChanged();
            }
            mech.Notify_DisabledWorkTypesChanged();
        }
        catch (Exception exception)
        {
            if (!mech.Spawned && mech.holdingOwner == null) capsule.TryAcceptMech(mech);
            if (mech.Spawned && !capsule.HasMech) capsule.Destroy(DestroyMode.Vanish);
            Log.Error($"[DMS Mech Storage] Activation failed; retained the original mech: {exception}");
            Reject("DMSMS_Failed".Translate(), actor);
            return false;
        }
        capsule.Destroy(DestroyMode.Vanish);
        Messages.Message("FFF.MechActivated".Translate(mech.LabelCap, actor.LabelShort), mech, MessageTypeDefOf.PositiveEvent);
        return true;
    }

    public static void Reject(string reason, Thing actor) => Messages.Message(reason, actor, MessageTypeDefOf.RejectInput, false);
}

[HarmonyPatch(typeof(Building_MechCapsule), nameof(Building_MechCapsule.CanActivate))]
public static class CapsuleActivationCheck
{
    public static bool Prefix(Building_MechCapsule __instance, Thing actor, ref AcceptanceReport __result)
    {
        if (!MechStorage.IsDmsCapsule(__instance)) return true;
        __result = MechStorage.CanActivate(__instance, actor);
        return false;
    }
}

[HarmonyPatch(typeof(Building_MechCapsule), nameof(Building_MechCapsule.ActivateMech))]
public static class CapsuleActivation
{
    public static bool Prefix(Building_MechCapsule __instance, Thing actor)
    {
        if (!MechStorage.IsDmsCapsule(__instance)) return true;
        MechStorage.Activate(__instance, actor);
        return false;
    }
}

[HarmonyPatch(typeof(MinifiedThing), nameof(MinifiedThing.LabelNoCount), MethodType.Getter)]
public static class MinifiedMechName
{
    public static void Postfix(MinifiedThing __instance, ref string __result)
    {
        if (__instance.InnerThing is Building_MechCapsule capsule && MechStorage.IsDmsCapsule(capsule))
            __result = capsule.Mech.LabelCap;
    }
}

/// <summary>
/// Native container release spawns through GenPlace, which always passes respawningAfterLoad:false.
/// A released mech is a restored pawn, so keep its battery time and platform resources.
/// </summary>
[HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn),
    new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) })]
public static class RestoredMechSpawn
{
    public static void Prefix(Thing newThing, ref bool respawningAfterLoad)
    {
        if (newThing.holdingOwner?.Owner is Building_MechCapsule capsule
            && newThing == capsule.Mech && MechStorage.IsDmsCapsule(capsule))
            respawningAfterLoad = true;
    }
}
