using HarmonyLib;
using RimWorld;
using UnburnedDNAFPerfFix;
using Verse;
using Verse.AI;

namespace Validation;

internal static class LifecycleTests
{
    internal static void Run()
    {
        var f = new Fixture(30);
        Pawn p = f.Pawn(new IntVec3(15, 0, 15), true);
        // Already-spawned fires before first observation represent loading a burning map.
        Fire a = f.Fire(new IntVec3(20, 0, 20));
        Fire b = f.Fire(a.Position);
        Fire attached = f.Fire(new IntVec3(10, 0, 10), parent: p);
        MapSnapshot s = MapSnapshot.Get(f.Map);
        Tests.Check(s.FireCount == 3 && s.GroundFireCount == 2 && s.GroundCells.Count == 1, "existing fires synchronized, same-cell fires counted exactly, attachments excluded from ground");
        Fire near = f.Fire(new IntVec3(16, 0, 15));
        Tests.Check(s.FireNear(p.Position) && s.GroundFireCount == 3, "same-tick new ground fire updates snapshot");
        var update = f.Component!.GetType().GetMethod("UpdateFire")!;
        update.Invoke(f.Component, [-1, a.Position]); update.Invoke(f.Component, [-1, a.Position]);
        Tests.Check(s.GroundFireCount == 3, "duplicate decrement notifications cannot corrupt exact set");
        f.Map.thingGrid.Deregister(a); f.Map.listerThings.Remove(a);
        update.Invoke(f.Component, [-1, a.Position]);
        Tests.Check(s.GroundFireCount == 2 && s.GroundCells.Contains(f.Map.cellIndices[b.Position]), "one same-cell fire removed without losing other");
        near.parent = p;
        update.Invoke(f.Component, [0, near.Position]);
        Tests.Check(s.GroundFireCount == 1, "ground-to-attached transition reconciled");
        f.Map.thingGrid.Deregister(b); f.Map.listerThings.Remove(b);
        update.Invoke(f.Component, [-1, b.Position]);
        Tests.Check(s.GroundFireCount == 0, "last ground fire removal yields exact zero");
        Tests.Check(Harmony.GetPatchInfo(AccessTools.Method(typeof(Fire), nameof(Fire.SpawnSetup)))!.Owners.Contains(Bootstrap.Owner)
            && Harmony.GetPatchInfo(AccessTools.Method(typeof(Fire), nameof(Fire.DeSpawn)))!.Owners.Contains(Bootstrap.Owner), "actual fire lifecycle event hooks installed");

        // A normal empty faction cache does not prove pair hostility is false:
        // hostileToAll and mental states are explicitly included in the broad phase.
        Pawn all = f.Pawn(new IntVec3(16, 0, 15), true);
        all.kindDef.hostileToAll = true;
        f.Map.attackTargetsCache.UpdateTarget(all);
        Tests.Attacks.Clear(); Tests.Wait(f.Wait(p));
        Tests.Check(Tests.Attacks.Contains(all), "hostileToAll friendly-faction target works with empty attackTargetsCache");
        all.kindDef.hostileToAll = false;
        Fixture.Set(all.mindState.mentalStateHandler, "curStateInt", new ForcedHostility { pawn = all, def = new MentalStateDef { defName = "Forced", allowBeatfire = true } });
        f.Map.attackTargetsCache.UpdateTarget(all);
        Tests.Attacks.Clear(); Tests.Wait(f.Wait(p));
        Tests.Check(Tests.Attacks.Contains(all), "same-tick aggressive mental-state target works with empty faction cache");
    }
    private sealed class ForcedHostility : MentalState
    {
        public override bool ForceHostileTo(Thing t) => true;
        public override bool ForceHostileTo(Faction f) => true;
    }
}
