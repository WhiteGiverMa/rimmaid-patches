using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WhiteGiver.MechanitorBandwidthBar;

/// <summary>
/// Replaces the vanilla mechanitor bandwidth gizmo (one shrinking cell per point of
/// bandwidth) with a capacity bar. Vanilla keeps adding rows and shrinking cells until
/// they are a few pixels wide, and the final <c>ContractedBy(2f)</c> turns them into
/// zero or negative rects once the bandwidth grows past roughly forty.
/// </summary>
public sealed class MechanitorBandwidthBarMod : Mod
{
    private const string HarmonyId = "whitegiver.mechanitor.bandwidthbar";
    private const float HeaderHeight = 20f;
    private const float BarHeight = 18f;
    private const float FooterHeight = 20f;

    private static readonly Color BarOutlineColor = new(0.05f, 0.05f, 0.05f);
    private static readonly Color EmptyBarColor = new(0.16f, 0.16f, 0.16f);
    private static readonly Color FilledColor = ColorLibrary.Yellow;
    private static readonly Color ExcessColor = ColorLibrary.Red;
    private static readonly Color TickColor = new(0f, 0f, 0f, 0.3f);
    private static readonly Color TotalMarkerColor = new(1f, 1f, 1f, 0.9f);
    private static readonly Color StripeColor = new(0f, 0f, 0f, 0.25f);
    private static readonly Color FreeColor = new(0.72f, 0.72f, 0.72f);
    private static readonly Color GestationColor = new(0.85f, 0.72f, 0.3f);

    private static AccessTools.FieldRef<MechanitorBandwidthGizmo, Pawn_MechanitorTracker>? trackerRef;

    public MechanitorBandwidthBarMod(ModContentPack content) : base(content)
    {
        if (TryInstall(out string error))
        {
            Log.Message("[Mechanitor Bandwidth Bar] Bandwidth progress bar applied to MechanitorBandwidthGizmo.");
        }
        else
        {
            Log.Error("[Mechanitor Bandwidth Bar] " + error);
        }
    }

    /// <summary>Applies the Harmony patch; returns false with a reason instead of throwing.</summary>
    public static bool TryInstall(out string error)
    {
        error = string.Empty;
        try
        {
            MethodInfo? target = AccessTools.Method(
                typeof(MechanitorBandwidthGizmo),
                nameof(MechanitorBandwidthGizmo.GizmoOnGUI));
            MethodInfo? prefix = AccessTools.Method(
                typeof(MechanitorBandwidthBarMod),
                nameof(DrawBandwidthGizmo));
            FieldInfo? trackerField = AccessTools.Field(typeof(MechanitorBandwidthGizmo), "tracker");

            if (target == null || prefix == null || trackerField == null)
            {
                error =
                    "Could not resolve MechanitorBandwidthGizmo.GizmoOnGUI or its tracker field; " +
                    "the vanilla gizmo was left untouched.";
                return false;
            }

            trackerRef =
                AccessTools.FieldRefAccess<MechanitorBandwidthGizmo, Pawn_MechanitorTracker>("tracker");
            new Harmony(HarmonyId).Patch(target, prefix: new HarmonyMethod(prefix));
            return true;
        }
        catch (Exception exception)
        {
            error = "Harmony patch failed: " + exception;
            return false;
        }
    }

    public static bool DrawBandwidthGizmo(
        MechanitorBandwidthGizmo __instance,
        Vector2 topLeft,
        float maxWidth,
        ref GizmoResult __result)
    {
        __result = new GizmoResult(GizmoState.Clear);
        if (trackerRef == null)
        {
            return true;
        }

        Pawn_MechanitorTracker tracker = trackerRef(__instance);
        if (tracker == null)
        {
            return true;
        }

        Draw(__instance, tracker, topLeft, maxWidth);
        return false;
    }

    private static void Draw(
        MechanitorBandwidthGizmo gizmo,
        Pawn_MechanitorTracker tracker,
        Vector2 topLeft,
        float maxWidth)
    {
        int used = tracker.UsedBandwidth;
        int total = tracker.TotalBandwidth;
        int gestation = tracker.UsedBandwidthFromGestation;

        Rect rect = new(topLeft.x, topLeft.y, gizmo.GetWidth(maxWidth), Gizmo.Height);
        Widgets.DrawWindowBackground(rect);

        Rect inner = rect.ContractedBy(6f);
        Rect header = new(inner.x, inner.y, inner.width, HeaderHeight);
        Rect barFrame = new(inner.x, header.yMax + 2f, inner.width, BarHeight);
        Rect bar = barFrame.ContractedBy(1f);
        Rect footer = new(inner.x, barFrame.yMax + 2f, inner.width, FooterHeight);

        BandwidthLayout layout = BandwidthLayout.Compute(used, total, gestation, bar.width);

        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;
        Widgets.Label(header, "Bandwidth".Translate());

        Color previousColor = GUI.color;
        Text.Anchor = TextAnchor.UpperRight;
        if (layout.IsOverCapacity)
        {
            GUI.color = ExcessColor;
        }

        Widgets.Label(header, used.ToString("F0") + " / " + total.ToString("F0"));
        GUI.color = previousColor;

        Widgets.DrawBoxSolid(barFrame, BarOutlineColor);
        Widgets.DrawBoxSolid(bar, EmptyBarColor);

        if (layout.UsedFraction > 0f)
        {
            Widgets.DrawBoxSolid(
                new Rect(bar.x, bar.y, bar.width * layout.UsedFraction, bar.height),
                FilledColor);
        }

        if (layout.GestationFraction > 0f)
        {
            DrawGestationStripes(
                new Rect(bar.x, bar.y, bar.width * layout.GestationFraction, bar.height));
        }

        if (layout.IsOverCapacity)
        {
            Widgets.DrawBoxSolid(
                new Rect(
                    bar.x + bar.width * layout.TotalFraction,
                    bar.y,
                    bar.width * (1f - layout.TotalFraction),
                    bar.height),
                ExcessColor);
            Widgets.DrawBoxSolid(
                new Rect(bar.x + bar.width * layout.TotalFraction - 1f, bar.y, 2f, bar.height),
                TotalMarkerColor);
        }

        foreach (float fraction in layout.TickFractions)
        {
            Widgets.DrawBoxSolid(new Rect(bar.x + bar.width * fraction, bar.y, 1f, bar.height), TickColor);
        }

        DrawFooter(footer, layout);

        Text.Anchor = TextAnchor.UpperLeft;
        TooltipHandler.TipRegion(rect, BuildTooltip(tracker, used, total));
    }

    private static void DrawFooter(Rect footer, BandwidthLayout layout)
    {
        Color previousColor = GUI.color;
        Text.Anchor = TextAnchor.MiddleLeft;
        GUI.color = layout.IsOverCapacity ? ExcessColor : FreeColor;
        Widgets.Label(
            footer,
            (layout.IsOverCapacity
                ? "WhiteGiver.BandwidthBar.Over".Translate(layout.Over)
                : "WhiteGiver.BandwidthBar.Free".Translate(layout.Free)));
        GUI.color = previousColor;

        if (layout.Gestation > 0)
        {
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = GestationColor;
            Widgets.Label(footer, "WhiteGiver.BandwidthBar.Gestating".Translate(layout.Gestation));
            GUI.color = previousColor;
        }
    }

    private static void DrawGestationStripes(Rect rect)
    {
        const float stripeHeight = 2f;
        const float gap = 2f;
        for (float y = rect.y + gap; y < rect.yMax - stripeHeight; y += stripeHeight + gap)
        {
            Widgets.DrawBoxSolid(new Rect(rect.x, y, rect.width, stripeHeight), StripeColor);
        }
    }

    private static TaggedString BuildTooltip(Pawn_MechanitorTracker tracker, int used, int total)
    {
        TaggedString tip = "Bandwidth".Translate().Colorize(ColoredText.TipSectionTitleColor)
            + ": " + used + " / " + total + "\n\n" + "BandwidthGizmoTip".Translate();

        int fromSubjects = tracker.UsedBandwidthFromSubjects;
        if (fromSubjects > 0)
        {
            tip += "\n\n" + "BandwidthUsage".Translate() + ": " + fromSubjects;
            IEnumerable<string> entries = from pawn in tracker.OverseenPawns
                where !pawn.IsGestating()
                group pawn by pawn.kindDef into groupByKind
                select groupByKind.Key.LabelCap + " x" + groupByKind.Count()
                    + " (+" + groupByKind.Sum(mech => mech.GetStatValue(StatDefOf.BandwidthCost)) + ")";
            tip += "\n\n" + entries.ToLineList(" - ");
        }

        int fromGestation = tracker.UsedBandwidthFromGestation;
        if (fromGestation > 0)
        {
            tip += "\n\n" + "MechGestationBandwidthUsed".Translate() + ": " + fromGestation;
            IEnumerable<string> entries = from pawn in tracker.OverseenPawns
                where pawn.IsGestating()
                group pawn by pawn.kindDef into groupByKind
                select groupByKind.Key.LabelCap + " x" + groupByKind.Count()
                    + " (+" + groupByKind.Sum(mech => mech.GetStatValue(StatDefOf.BandwidthCost)) + ")";
            tip += "\n\n" + entries.ToLineList(" - ");
        }

        return tip;
    }
}
