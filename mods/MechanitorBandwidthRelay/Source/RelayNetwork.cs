using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.MechanitorBandwidthRelay;

internal readonly record struct RelayLink(Pawn? Donor, Pawn? Recipient, int Requested)
{
    internal bool HasPair => Donor != null && Recipient != null && Donor != Recipient;
}

public sealed class RelayNetwork : GameComponent
{
    private sealed class Allocation
    {
        internal readonly Dictionary<Pawn, int> Offsets = new();
        internal readonly Dictionary<Building_BandwidthRelay, int> Grants = new();
    }

    private static readonly ConditionalWeakTable<Game, RelayNetwork> Games = new();
    internal static RelayNetwork? CurrentNetwork => Current.Game is { } game && Games.TryGetValue(game, out var network) ? network : null;

    private readonly SortedSet<Building_BandwidthRelay> relays = new(Comparer<Building_BandwidthRelay>.Create(
        (a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber)));
    private readonly HashSet<Pawn> members = new();
    private readonly HashSet<Pawn> pendingAudits = new();
    private Allocation allocation = new();
    private bool ready;
    private bool auditing;
    private bool reconciling;

    public RelayNetwork(Game game) => Games.AddOrUpdate(game, this);

    public override void StartedNewGame() => Start();
    public override void LoadedGame() => Start();

    private void Start()
    {
        ready = true;
        Reconcile();
        AuditPending();
    }

    public override void GameComponentTick()
    {
        // Match the vanilla band node's check interval for changes without a tracker notification.
        if (relays.Count != 0 && Find.TickManager.TicksGame % 60 == 0)
            Reconcile();
        AuditPending();
    }

    internal void Track(Building_BandwidthRelay relay, bool keep)
    {
        if (keep && relay.Link is { HasPair: true, Requested: > 0 })
            relays.Add(relay);
        else
            relays.Remove(relay);
        IndexMembers();
        Changed();
    }

    internal void Changed()
    {
        Reconcile();
        AuditPending();
    }

    private void IndexMembers()
    {
        members.Clear();
        foreach (var relay in relays)
        {
            members.Add(relay.Link.Donor!);
            members.Add(relay.Link.Recipient!);
        }
    }

    internal int Offset(Pawn pawn) => Volatile.Read(ref allocation).Offsets.GetValueOrDefault(pawn);
    internal int Grant(Building_BandwidthRelay relay) => Volatile.Read(ref allocation).Grants.GetValueOrDefault(relay);

    internal int Available(Building_BandwidthRelay relay, Pawn donor, Pawn recipient) =>
        Calculate(relay, new RelayLink(donor, recipient, int.MaxValue), assumeEditedPower: true).Grants.GetValueOrDefault(relay);

    internal List<string> OverCapacity(Building_BandwidthRelay relay, RelayLink link)
    {
        Allocation before = Calculate();
        Allocation after = Calculate(relay, link);
        List<string> warnings = new();
        foreach (var pair in before.Offsets)
            CheckLoss(pair.Key, pair.Value);
        foreach (Pawn pawn in after.Offsets.Keys)
            if (!before.Offsets.ContainsKey(pawn)) CheckLoss(pawn, 0);
        return warnings;

        void CheckLoss(Pawn pawn, int previousOffset)
        {
            int offset = after.Offsets.GetValueOrDefault(pawn);
            if (offset >= previousOffset || pawn.mechanitor is not { } tracker) return;
            int capacity = RelayPatches.NativeCapacity(tracker) + offset;
            int committed = Committed(tracker);
            if (committed > capacity)
                warnings.Add("WGBandwidthRelay.OverCapacity".Translate(pawn.LabelShortCap, committed, capacity));
        }
    }

    private Allocation Calculate(Building_BandwidthRelay? edited = null, RelayLink replacement = default, bool assumeEditedPower = false)
    {
        Allocation next = new();
        Dictionary<Pawn, int> spare = new();
        bool inserted = edited == null;
        foreach (var relay in relays)
        {
            if (!inserted && relay.thingIDNumber >= edited!.thingIDNumber)
            {
                Allocate(edited, replacement, assumeEditedPower);
                inserted = true;
            }
            if (relay != edited)
                Allocate(relay, relay.Link, false);
        }
        if (!inserted)
            Allocate(edited!, replacement, assumeEditedPower);
        return next;

        void Allocate(Building_BandwidthRelay relay, RelayLink link, bool assumePower)
        {
            if (link.Requested <= 0 || !relay.CanMaintain(link.Donor, link.Recipient) || (!assumePower && !relay.Powered))
                return;

            Pawn donor = link.Donor!;
            Pawn recipient = link.Recipient!;
            if (!spare.TryGetValue(donor, out int remaining))
            {
                Pawn_MechanitorTracker tracker = donor.mechanitor;
                remaining = Math.Max(0, RelayPatches.NativeCapacity(tracker) - Committed(tracker));
            }
            int amount = Math.Min(remaining, link.Requested);
            spare[donor] = remaining - amount;
            if (amount == 0) return;

            next.Grants.Add(relay, amount);
            next.Offsets[donor] = next.Offsets.GetValueOrDefault(donor) - amount;
            next.Offsets[recipient] = next.Offsets.GetValueOrDefault(recipient) + amount;
        }
    }

    private static int Committed(Pawn_MechanitorTracker tracker)
    {
        float subjects = 0f;
        foreach (Pawn mech in tracker.OverseenPawns)
            if (!mech.IsGestating()) subjects += mech.GetStatValue(StatDefOf.BandwidthCost);

        // An Overseer relation is inserted after vanilla assigns the mech and audits the tracker.
        // Count that in-flight assignment too; round costs up so a fractional-cost mech stays funded.
        float assigned = 0f;
        foreach (var group in tracker.controlGroups)
            foreach (var mech in group.AssignedMechs)
                if (mech.pawn is { } pawn && !pawn.IsGestating()) assigned += pawn.GetStatValue(StatDefOf.BandwidthCost);

        float committed = Math.Max(subjects, assigned);
        foreach (Bill_Mech bill in tracker.ActiveMechBills)
            committed += bill.BandwidthCost;
        return (int)MathF.Ceiling(committed);
    }

    private void Reconcile()
    {
        if (!ready || reconciling || auditing || (relays.Count == 0 && allocation.Offsets.Count == 0)) return;
        reconciling = true;
        try
        {
            if (relays.RemoveWhere(static relay => !relay.Spawned) != 0) IndexMembers();
            Allocation next = Calculate();
            Allocation previous = allocation;
            Volatile.Write(ref allocation, next);
            foreach (var pair in previous.Offsets)
                if (next.Offsets.GetValueOrDefault(pair.Key) != pair.Value) pendingAudits.Add(pair.Key);
            foreach (var pair in next.Offsets)
                if (previous.Offsets.GetValueOrDefault(pair.Key) != pair.Value) pendingAudits.Add(pair.Key);
        }
        finally { reconciling = false; }
    }

    internal bool BeforeAudit(Pawn pawn, out RelayNetwork? flush)
    {
        flush = null;
        if (auditing || reconciling || !members.Contains(pawn)) return true;
        if (!ready)
        {
            // Pawns are spawned and audited before the relay buildings are restored on load.
            pendingAudits.Add(pawn);
            return false;
        }
        Reconcile();
        pendingAudits.Remove(pawn);
        flush = this;
        return true;
    }

    internal void AuditPending()
    {
        if (!ready || auditing || reconciling || pendingAudits.Count == 0) return;
        auditing = true;
        try
        {
            foreach (Pawn pawn in pendingAudits)
                if (!pawn.Dead) pawn.mechanitor?.Notify_BandwidthChanged();
        }
        finally
        {
            pendingAudits.Clear();
            auditing = false;
        }
    }
}

public sealed class BandwidthRelayMod : Mod
{
    public BandwidthRelayMod(ModContentPack content) : base(content)
    {
        if (ModsConfig.BiotechActive)
            new Harmony("whitegiver.mechanitor.bandwidthrelay").PatchAll(typeof(BandwidthRelayMod).Assembly);
    }
}

internal static class RelayPatches
{
    [ThreadStatic] private static bool readingNative;

    internal static int NativeCapacity(Pawn_MechanitorTracker tracker)
    {
        // Preserve other mods' adjustments to the real tracker getter; exclude only our own link.
        bool previous = readingNative;
        readingNative = true;
        try { return tracker.TotalBandwidth; }
        finally { readingNative = previous; }
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.TotalBandwidth), MethodType.Getter)]
    private static class TotalBandwidthPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyAfter("whitegiver.dmssr.unifiedfix")]
        private static void Apply(Pawn_MechanitorTracker __instance, ref int __result)
        {
            if (!readingNative && __instance.Pawn is { } pawn && RelayNetwork.CurrentNetwork is { } network)
                __result += network.Offset(pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn_MechanitorTracker), nameof(Pawn_MechanitorTracker.Notify_BandwidthChanged))]
    private static class NotifyBandwidthPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(Pawn_MechanitorTracker __instance, out RelayNetwork? __state)
        {
            __state = null;
            return __instance.Pawn is not { } pawn || RelayNetwork.CurrentNetwork is not { } network || network.BeforeAudit(pawn, out __state);
        }

        // Vanilla's audit uses a shared scratch list. Do not audit a counterpart inside it.
        [HarmonyPostfix]
        private static void Finish(RelayNetwork? __state) => __state?.AuditPending();
    }
}
