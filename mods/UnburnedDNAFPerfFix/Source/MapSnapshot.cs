using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace UnburnedDNAFPerfFix;

public sealed class MapSnapshot
{
    private static readonly ConditionalWeakTable<Map, MapSnapshot> Maps = new();
    internal static Action<Map, int>? GroundCountChanged;
    private int tick = int.MinValue;
    private CellRect fireReach = CellRect.Empty;
    private CellRect meleeReach = CellRect.Empty;
    private bool hasMelee;
    private bool unrestrictedMelee;
    public int FireCount { get; private set; }
    public int GroundFireCount { get; private set; }
    public int Builds { get; private set; }
    public HashSet<int> GroundCells { get; } = new();

    public static MapSnapshot Get(Map map)
    {
        MapSnapshot s = Maps.GetValue(map, static _ => new());
        int now = Find.TickManager.TicksGame;
        if (s.tick != now) s.Build(map, now);
        return s;
    }

    private void Build(Map map, int now)
    {
        tick = now;
        Builds++;
        FireCount = GroundFireCount = 0;
        GroundCells.Clear();
        fireReach = meleeReach = CellRect.Empty;
        hasMelee = false;
        // A pair-hostility postfix cannot bypass the later IsActiveThreatTo(faction)
        // condition. Unknown changes to that condition itself disable only this gate.
        unrestrictedMelee = Patched(nameof(GenHostility.IsActiveThreatTo))
            || Patched(nameof(GenHostility.HostileTo), [typeof(Thing), typeof(Faction)]);
        foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Fire))
            if (thing is Fire fire) AddFire(map, fire);
        foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned) AddPawn(pawn);
        GroundCountChanged?.Invoke(map, GroundFireCount);
    }

    private static bool Patched(string name, Type[]? args = null)
    {
        var patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(GenHostility), name, args));
        return patches != null && patches.Owners.Any(o => o != Bootstrap.Owner);
    }

    private void AddFire(Map map, Fire fire)
    {
        Expand(ref fireReach, fire.OccupiedRect().ExpandedBy(1), FireCount++ != 0);
        if (fire.parent == null)
        {
            GroundFireCount++;
            GroundCells.Add(map.cellIndices.CellToIndex(fire.Position));
        }
    }

    // The exact melee branch ends in IsActiveThreatTo(target, player), whose first
    // condition is target.HostileTo(player). Pair-only hostility (including mod
    // postfixes) alone can never make that branch succeed. Keep transitional guest
    // and mental-state targets broadly, and update on hostility/job/predator events.
    private static bool Candidate(Pawn p) => p.InMentalState || p.IsPrisoner
        || p.IsSlave || p.kindDef.hostileToAll || p.RaceProps.predator
        || (p.Faction != null && p.Faction != Faction.OfPlayer && p.Faction.HostileTo(Faction.OfPlayer))
        || (p.Faction == null && p.RaceProps.Humanlike && Faction.OfPlayer.def.hostileToFactionlessHumanlikes)
        || p.HostileTo(Faction.OfPlayer);

    private void AddPawn(Pawn pawn)
    {
        if (!Candidate(pawn)) return;
        Expand(ref meleeReach, pawn.OccupiedRect().ExpandedBy(1), hasMelee);
        hasMelee = true;
    }

    private static void Expand(ref CellRect rect, CellRect add, bool exists)
    {
        if (!exists) { rect = add; return; }
        rect.minX = Math.Min(rect.minX, add.minX); rect.maxX = Math.Max(rect.maxX, add.maxX);
        rect.minZ = Math.Min(rect.minZ, add.minZ); rect.maxZ = Math.Max(rect.maxZ, add.maxZ);
    }

    public bool FireNear(IntVec3 p) => FireCount != 0 && fireReach.Contains(p);
    public bool MayMelee(Pawn p) => unrestrictedMelee || p.Faction != Faction.OfPlayer
        || (hasMelee && meleeReach.Contains(p.Position));

    internal static void Install(Harmony h)
    {
        // Incrementally *expand* within the tick instead of rebuilding: a newly spawned
        // enemy/fire or a moving target must never be hidden by a negative snapshot.
        h.Patch(AccessTools.Method(typeof(ThingGrid), nameof(ThingGrid.Register)),
            postfix: new HarmonyMethod(typeof(MapSnapshot), nameof(Registered)));
        h.Patch(AccessTools.Method(typeof(AttackTargetsCache), nameof(AttackTargetsCache.UpdateTarget)),
            postfix: new HarmonyMethod(typeof(MapSnapshot), nameof(TargetChanged)));
        h.Patch(AccessTools.Method(typeof(Faction), "TookDamageFromPredator"),
            postfix: new HarmonyMethod(typeof(MapSnapshot), nameof(PredatorChanged)));
        h.Patch(AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
            postfix: new HarmonyMethod(typeof(MapSnapshot), nameof(JobChanged)));
        h.Patch(AccessTools.Method(typeof(Fire), nameof(Fire.SpawnSetup)),
            postfix: new HarmonyMethod(typeof(MapSnapshot), nameof(FiresChanged)));
        h.Patch(AccessTools.Method(typeof(Fire), nameof(Fire.DeSpawn)),
            prefix: new HarmonyMethod(typeof(MapSnapshot), nameof(BeforeDespawn)),
            postfix: new HarmonyMethod(typeof(MapSnapshot), nameof(AfterDespawn)));
        h.Patch(AccessTools.Method(typeof(Fire), nameof(Fire.AttachTo)),
            postfix: new HarmonyMethod(typeof(MapSnapshot), nameof(FiresChanged)));
    }

    private static void Registered(Thing t)
    {
        if (t.Map is not { } map || !Maps.TryGetValue(map, out MapSnapshot? s) || s.tick != Find.TickManager.TicksGame) return;
        if (t is Pawn p) s.AddPawn(p);
        else if (t is Fire) RefreshFires(map);
    }

    private static void TargetChanged(IAttackTarget t)
    {
        if (t is Pawn p && p.Spawned && Maps.TryGetValue(p.Map, out MapSnapshot? s)) s.AddPawn(p);
    }
    private static void PredatorChanged(Pawn predator) => TargetChanged(predator);
    private static void JobChanged(Pawn ___pawn)
    {
        if (___pawn.CurJob?.def == JobDefOf.PredatorHunt) TargetChanged(___pawn);
    }

    private static void BeforeDespawn(Fire __instance, out Map? __state) => __state = __instance.Map;
    private static void AfterDespawn(Map? __state) { if (__state != null) RefreshFires(__state); }
    private static void FiresChanged(Fire __instance) { if (__instance.Map is { } m) RefreshFires(m); }

    // This is an event update, not a new per-tick threat scan. Also catches parent changes,
    // duplicate spawn/despawn callbacks, and multiple ground fires sharing a cell.
    internal static void RefreshFires(Map map)
    {
        if (!Maps.TryGetValue(map, out MapSnapshot? s)) return;
        s.FireCount = s.GroundFireCount = 0;
        s.fireReach = CellRect.Empty;
        s.GroundCells.Clear();
        foreach (Thing t in map.listerThings.ThingsOfDef(ThingDefOf.Fire)) if (t is Fire f) s.AddFire(map, f);
        GroundCountChanged?.Invoke(map, s.GroundFireCount);
    }
}
