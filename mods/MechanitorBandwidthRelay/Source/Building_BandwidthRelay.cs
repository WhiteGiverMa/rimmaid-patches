using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace WhiteGiver.MechanitorBandwidthRelay;

public sealed class Building_BandwidthRelay : Building
{
    private Pawn? donor;
    private Pawn? recipient;
    private int requested;

    internal RelayLink Link => new(donor, recipient, requested);
    internal bool PlayerOwned => Spawned && Faction.IsPlayerSafe();
    internal bool Powered => GetComp<CompPowerTrader>() is { PowerOn: true };

    private static bool Eligible(Pawn? pawn) => pawn is { Destroyed: false, Dead: false, IsPrisoner: false } &&
        pawn.Faction.IsPlayerSafe() && MechanitorUtility.IsMechanitor(pawn);

    internal bool Candidate(Pawn? pawn) => pawn is { Spawned: true, Suspended: false } && pawn.Map == Map && Eligible(pawn);

    internal bool CanLink(Pawn? source, Pawn? target) => PlayerOwned && source != target && Candidate(source) && Candidate(target);

    internal bool CanMaintain(Pawn? source, Pawn? target) => PlayerOwned && source != target && Eligible(source) && Eligible(target);

    internal string? PairingError(Pawn? source, Pawn? target)
    {
        if (requested > 0 && source == donor && target == recipient)
            return CanMaintain(source, target) ? null : "WGBandwidthRelay.Unavailable";
        return CanLink(source, target) ? null : "WGBandwidthRelay.InvalidSelection";
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_References.Look(ref donor, "bandwidthDonor");
        Scribe_References.Look(ref recipient, "bandwidthRecipient");
        Scribe_Values.Look(ref requested, "bandwidthRequested");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            RelayNetwork.CurrentNetwork?.Track(this, keep: true);
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        if (requested > 0) RelayNetwork.CurrentNetwork?.Track(this, keep: true);
    }

    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        base.DeSpawn(mode);
        if (requested > 0) RelayNetwork.CurrentNetwork?.Track(this, keep: false);
    }

    public override void Notify_MyMapRemoved()
    {
        base.Notify_MyMapRemoved();
        // Abandoning a map discards its buildings without calling DeSpawn.
        if (requested > 0) RelayNetwork.CurrentNetwork?.Track(this, keep: false);
    }

    public override void SetFaction(Faction newFaction, Pawn? recruiter = null)
    {
        base.SetFaction(newFaction, recruiter);
        if (Spawned && requested > 0) RelayNetwork.CurrentNetwork?.Changed();
    }

    protected override void ReceiveCompSignal(string signal)
    {
        base.ReceiveCompSignal(signal);
        if (Spawned && requested > 0 && signal is CompPowerTrader.PowerTurnedOnSignal or CompPowerTrader.PowerTurnedOffSignal)
            RelayNetwork.CurrentNetwork?.Changed();
    }

    public override IEnumerable<Gizmo> GetGizmos()
    {
        foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
        if (!PlayerOwned) yield break;
        yield return new Command_Action
        {
            defaultLabel = "WGBandwidthRelay.Configure".Translate(),
            defaultDesc = "WGBandwidthRelay.ConfigureDesc".Translate(),
            icon = def.uiIcon,
            action = () => OpenConfig(null),
            groupable = false
        };
    }

    public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn)
    {
        foreach (FloatMenuOption option in base.GetFloatMenuOptions(selPawn)) yield return option;
        if (PlayerOwned && Candidate(selPawn))
            yield return new FloatMenuOption("WGBandwidthRelay.Configure".Translate(), () => OpenConfig(selPawn), revalidateClickTarget: this);
    }

    public override string GetInspectString()
    {
        string inherited = base.GetInspectString();
        return inherited.NullOrEmpty() ? LinkSummary : $"{inherited.TrimEnd()}\n{LinkSummary}";
    }

    internal string LinkSummary
    {
        get
        {
            if (requested <= 0) return "WGBandwidthRelay.Unconfigured".Translate();
            int amount = RelayNetwork.CurrentNetwork?.Grant(this) ?? 0;
            string description = $"{"WGBandwidthRelay.Route".Translate(PawnLabel(donor), PawnLabel(recipient))}\n{"WGBandwidthRelay.Flow".Translate(amount, requested)}";
            string? reason = !CanMaintain(donor, recipient) ? "WGBandwidthRelay.Unavailable"
                : !Powered ? "WGBandwidthRelay.NoPower"
                : amount == 0 ? "WGBandwidthRelay.NoSpare"
                : amount < requested ? "WGBandwidthRelay.Limited" : null;
            return reason == null ? description : $"{description}\n{reason.Translate()}";
        }
    }

    internal static string PawnLabel(Pawn? pawn) => pawn != null ? pawn.LabelShortCap : "WGBandwidthRelay.Missing".Translate();

    private void OpenConfig(Pawn? suggestedDonor)
    {
        if (!PlayerOwned || (suggestedDonor != null && !Candidate(suggestedDonor))) return;
        Find.WindowStack.Add(new Dialog_RelayConfig(this, suggestedDonor));
    }

    internal void ApplyFromDialog(RelayLink link, bool confirmed = false, Action? applied = null)
    {
        RelayNetwork? network = RelayNetwork.CurrentNetwork;
        string? error = !PlayerOwned || network == null ? "WGBandwidthRelay.InvalidBuilding"
            : link.Requested != 0 ? PairingError(link.Donor, link.Recipient) : null;
        if (error != null)
        {
            Messages.Message(error.Translate(), MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        if (!confirmed && network!.OverCapacity(this, link) is { Count: > 0 } warnings)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "WGBandwidthRelay.ConfirmLoss".Translate(string.Join("\n", warnings)),
                () => ApplyFromDialog(link, confirmed: true, applied: applied), destructive: true));
            return;
        }

        Commit(link, network!);
        applied?.Invoke();
    }

    internal void Commit(RelayLink link, RelayNetwork network)
    {
        donor = link.Donor;
        recipient = link.Recipient;
        requested = link.Requested;
        network.Track(this, keep: true);
    }
}
