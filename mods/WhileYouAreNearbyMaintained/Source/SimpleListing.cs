using System;
using UnityEngine;
using Verse;

namespace MjRimMods.WhileYouAreNearby;

internal static class SimpleListing
{
    internal static void IntegerInput(Listing_Standard listing, ref int value, string key, int min = 0, int max = 999, string suffix = "")
    {
        Rect row = listing.GetRect(32f);
        TooltipHandler.TipRegion(row, (key + "Tooltips").Translate());
        Rect label = row.LeftPart(0.72f);
        Widgets.Label(label, key.Translate());
        var minus = new Rect(label.xMax + 2f, row.y, 22f, 28f);
        if (Widgets.ButtonImage(minus, TexButton.Minus)) value = Math.Max(min, value - 1);
        var field = new Rect(minus.xMax + 2f, row.y, 55f, 28f);
        string buffer = value.ToString();
        Widgets.TextFieldNumeric(field, ref value, ref buffer, min, max);
        var plus = new Rect(field.xMax + 2f, row.y, 22f, 28f);
        if (Widgets.ButtonImage(plus, TexButton.Plus)) value = Math.Min(max, value + 1);
        if (suffix.Length != 0) Widgets.Label(new Rect(plus.xMax + 2f, row.y, 20f, 28f), suffix);
    }
}
