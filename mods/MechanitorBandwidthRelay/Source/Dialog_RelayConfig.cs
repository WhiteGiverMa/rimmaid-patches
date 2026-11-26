using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace WhiteGiver.MechanitorBandwidthRelay;

internal sealed class Dialog_RelayConfig : Window
{
    private readonly Building_BandwidthRelay relay;
    private Pawn? donor;
    private Pawn? recipient;
    private string amount;

    internal Dialog_RelayConfig(Building_BandwidthRelay relay, Pawn? suggestedDonor)
    {
        this.relay = relay;
        RelayLink link = relay.Link;
        donor = link.Donor ?? suggestedDonor;
        recipient = link.Recipient;
        amount = link.Requested > 0 ? link.Requested.ToString() : "1";
        optionalTitle = "WGBandwidthRelay.Configure".Translate();
        doCloseX = true;
        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnAccept = false;
    }

    public override Vector2 InitialSize => new(530f, 412f);

    public override void DoWindowContents(Rect rect)
    {
        if (!relay.PlayerOwned || RelayNetwork.CurrentNetwork is not { } network)
        {
            Close();
            return;
        }

        const float labelWidth = 155f;
        const float rowHeight = 32f;
        DrawPawnRow(0f, forDonor: true);
        DrawPawnRow(42f, forDonor: false);
        if (Widgets.ButtonText(new Rect(rect.width - 86f, 42f, 86f, rowHeight), "WGBandwidthRelay.Swap".Translate(),
            active: relay.CanLink(recipient, donor)))
            (donor, recipient) = (recipient, donor);
        Widgets.Label(new Rect(0f, 87f, labelWidth, rowHeight), "WGBandwidthRelay.Amount".Translate());
        amount = Widgets.TextField(new Rect(labelWidth, 84f, 92f, rowHeight), amount, 10);

        string? pairError = relay.PairingError(donor, recipient);
        bool pairValid = pairError == null;
        int available = pairValid ? network.Available(relay, donor!, recipient!) : 0;
        if (Widgets.ButtonText(new Rect(labelWidth + 102f, 84f, rect.width - labelWidth - 102f, rowHeight),
            "WGBandwidthRelay.Max".Translate(), active: available > 0))
            amount = available.ToString();

        bool amountValid = int.TryParse(amount, out int requested) && requested > 0;
        int grant = pairValid && relay.Powered && amountValid ? System.Math.Min(available, requested) : 0;
        string preview = pairError != null ? pairError.Translate()
            : !amountValid ? "WGBandwidthRelay.AmountInvalid".Translate()
            : $"{"WGBandwidthRelay.Available".Translate(available)}\n{"WGBandwidthRelay.Upcoming".Translate(grant, requested)}";
        if (pairValid && !relay.Powered) preview += $"\n{"WGBandwidthRelay.NoPower".Translate()}";
        Widgets.Label(new Rect(0f, 124f, rect.width, 86f), preview);
        Widgets.Label(new Rect(0f, 213f, rect.width, 80f), relay.LinkSummary);

        float buttonY = rect.height - rowHeight;
        float buttonWidth = (rect.width - 20f) / 3f;
        if (Widgets.ButtonText(new Rect(0f, buttonY, buttonWidth, rowHeight), "WGBandwidthRelay.Clear".Translate(), active: relay.Link.Requested > 0))
            relay.ApplyFromDialog(default, applied: () => Close());
        if (Widgets.ButtonText(new Rect(buttonWidth + 10f, buttonY, buttonWidth, rowHeight), "CancelButton".Translate()))
            Close();
        if (Widgets.ButtonText(new Rect(rect.width - buttonWidth, buttonY, buttonWidth, rowHeight), "Confirm".Translate(), active: pairValid && amountValid))
            relay.ApplyFromDialog(new RelayLink(donor, recipient, requested), applied: () => Close());

        void DrawPawnRow(float y, bool forDonor)
        {
            Widgets.Label(new Rect(0f, y + 3f, labelWidth, rowHeight),
                (forDonor ? "WGBandwidthRelay.Donor" : "WGBandwidthRelay.Recipient").Translate());
            Pawn? current = forDonor ? donor : recipient;
            string name = current == null ? "WGBandwidthRelay.Select".Translate() : Building_BandwidthRelay.PawnLabel(current);
            float width = rect.width - labelWidth - (forDonor ? 0f : 96f);
            if (Widgets.ButtonText(new Rect(labelWidth, y, width, rowHeight), name))
                ChoosePawn(forDonor);
        }
    }

    private void ChoosePawn(bool forDonor)
    {
        string title = (forDonor ? "WGBandwidthRelay.Donor" : "WGBandwidthRelay.Recipient").Translate();
        Pawn? other = forDonor ? recipient : donor;
        List<FloatMenuOption> choices = new();
        foreach (Pawn pawn in relay.Map.mapPawns.AllPawnsSpawned)
        {
            if (pawn == other || !relay.Candidate(pawn)) continue;
            choices.Add(new FloatMenuOption(pawn.LabelShortCap, () =>
            {
                if (!relay.PlayerOwned || !relay.Candidate(pawn)) return;
                if (forDonor) donor = pawn;
                else recipient = pawn;
            }, pawn, Color.white));
        }
        if (choices.Count == 0) choices.Add(new FloatMenuOption("WGBandwidthRelay.NoCandidate".Translate(), null));
        Find.WindowStack.Add(new FloatMenu(choices, title));
    }
}
