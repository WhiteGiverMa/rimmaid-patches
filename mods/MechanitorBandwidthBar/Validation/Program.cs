using WhiteGiver.MechanitorBandwidthBar;

const float PixelWidth = 122f;
int failures = 0;

void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        Console.WriteLine($"  ok   {name}");
        return;
    }

    failures++;
    Console.WriteLine($"  FAIL {name}{(detail is null ? string.Empty : " -> " + detail)}");
}

void CheckClose(string name, float actual, float expected)
{
    Check(name, MathF.Abs(actual - expected) < 1e-4f, $"{actual} != {expected}");
}

void CheckFractions(string name, BandwidthLayout layout)
{
    Check($"{name}: used fraction in range", layout.UsedFraction is >= 0f and <= 1f, layout.UsedFraction.ToString());
    Check($"{name}: total fraction in range", layout.TotalFraction is >= 0f and <= 1f, layout.TotalFraction.ToString());
    Check(
        $"{name}: gestation fraction in range",
        layout.GestationFraction is >= 0f and <= 1f,
        layout.GestationFraction.ToString());
    Check(
        $"{name}: gestation does not exceed used",
        layout.GestationFraction <= layout.UsedFraction + 1e-6f,
        $"{layout.GestationFraction} > {layout.UsedFraction}");

    float previous = -1f;
    for (int i = 0; i < layout.TickFractions.Length; i++)
    {
        float tick = layout.TickFractions[i];
        Check($"{name}: tick {i} inside bar", tick is > 0f and < 1f, tick.ToString());
        Check($"{name}: tick {i} increasing", tick > previous, $"{tick} <= {previous}");
        previous = tick;
    }
}

Console.WriteLine("BandwidthLayout");

BandwidthLayout small = BandwidthLayout.Compute(3, 5, 0, PixelWidth);
Check("small: scale", small.Scale == 5);
Check("small: free", small.Free == 2);
Check("small: not over", !small.IsOverCapacity);
CheckClose("small: used fraction", small.UsedFraction, 0.6f);
CheckClose("small: total fraction", small.TotalFraction, 1f);
Check("small: unit ticks", small.TickStep == 1 && small.TickFractions.Length == 4);
CheckFractions("small", small);

BandwidthLayout screenshot = BandwidthLayout.Compute(134, 175, 0, PixelWidth);
Check("134/175: scale", screenshot.Scale == 175);
Check("134/175: free", screenshot.Free == 41);
Check("134/175: not over", !screenshot.IsOverCapacity);
CheckClose("134/175: used fraction", screenshot.UsedFraction, 134f / 175f);
Check("134/175: readable tick step", screenshot.TickStep == 20 && screenshot.TickFractions.Length == 8);
CheckFractions("134/175", screenshot);

BandwidthLayout over = BandwidthLayout.Compute(200, 175, 0, PixelWidth);
Check("200/175: over", over.Over == 25 && over.IsOverCapacity);
Check("200/175: free is zero", over.Free == 0);
CheckClose("200/175: used fills bar", over.UsedFraction, 1f);
CheckClose("200/175: capacity marker", over.TotalFraction, 175f / 200f);
CheckFractions("200/175", over);

BandwidthLayout empty = BandwidthLayout.Compute(0, 0, 0, PixelWidth);
Check("0/0: scale floor", empty.Scale == 1);
Check("0/0: no ticks", empty.TickFractions.Length == 0);
CheckFractions("0/0", empty);

BandwidthLayout negative = BandwidthLayout.Compute(-5, -3, -2, PixelWidth);
Check("negative input clamped", negative is { Used: 0, Total: 0, Gestation: 0 });
CheckFractions("negative", negative);

BandwidthLayout gestating = BandwidthLayout.Compute(10, 20, 3, PixelWidth);
Check("gestating: counted in used", gestating.Gestation == 3);
CheckClose("gestating: fraction", gestating.GestationFraction, 0.15f);
CheckFractions("gestating", gestating);

BandwidthLayout clampedGestation = BandwidthLayout.Compute(5, 10, 99, PixelWidth);
Check("gestating: clamped to used", clampedGestation.Gestation == 5);
CheckFractions("gestating clamped", clampedGestation);

BandwidthLayout huge = BandwidthLayout.Compute(10_000_000, 10_000_000, 0, PixelWidth);
Check("huge: tick count capped", huge.TickFractions.Length == 0);
CheckFractions("huge", huge);

BandwidthLayout zeroWidth = BandwidthLayout.Compute(134, 175, 0, 0f);
Check("zero width: no ticks", zeroWidth.TickFractions.Length == 0);

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
