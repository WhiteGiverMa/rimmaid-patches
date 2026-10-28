using System;

namespace WhiteGiver.MechanitorBandwidthBar;

/// <summary>
/// Pure layout math for the mechanitor bandwidth bar. Deliberately free of Unity/Verse
/// types so the validation project can drive it offline with real bandwidth numbers.
/// </summary>
public sealed class BandwidthLayout
{
    /// <summary>Minimum distance in pixels between two tick marks.</summary>
    public const float MinTickSpacing = 8f;

    /// <summary>Above this many ticks the bar is drawn without ticks instead of looking like noise.</summary>
    public const int MaxTicks = 64;

    private static readonly int[] NiceSteps =
    {
        1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000,
        20000, 25000, 50000, 100000,
    };

    private BandwidthLayout(int used, int total, int gestation, int tickStep, float[] tickFractions)
    {
        Used = used;
        Total = total;
        Gestation = gestation;
        Scale = Math.Max(Math.Max(used, total), 1);
        Free = Math.Max(0, total - used);
        Over = Math.Max(0, used - total);
        IsOverCapacity = used > total;
        UsedFraction = used / (float)Scale;
        TotalFraction = total / (float)Scale;
        GestationFraction = gestation / (float)Scale;
        TickStep = tickStep;
        TickFractions = tickFractions;
    }

    public int Used { get; }

    public int Total { get; }

    public int Gestation { get; }

    /// <summary>Full range of the bar; never smaller than 1 so fractions stay finite.</summary>
    public int Scale { get; }

    public int Free { get; }

    public int Over { get; }

    public bool IsOverCapacity { get; }

    /// <summary>Filled width of all mechs, 0..1.</summary>
    public float UsedFraction { get; }

    /// <summary>Where the capacity marker sits, 0..1.</summary>
    public float TotalFraction { get; }

    /// <summary>Filled width taken by gestating mechs, 0..1.</summary>
    public float GestationFraction { get; }

    public int TickStep { get; }

    /// <summary>Fractional x positions (0..1) of interior tick marks.</summary>
    public float[] TickFractions { get; }

    public static BandwidthLayout Compute(int used, int total, int gestation, float pixelWidth)
    {
        used = Math.Max(0, used);
        total = Math.Max(0, total);
        gestation = Math.Clamp(gestation, 0, used);

        BandwidthLayout layout = new(used, total, gestation, 0, Array.Empty<float>());
        if (layout.Scale <= 1 || pixelWidth <= 0f)
        {
            return layout;
        }

        float unitPixels = pixelWidth / layout.Scale;
        int step = NiceSteps[NiceSteps.Length - 1];
        foreach (int candidate in NiceSteps)
        {
            step = candidate;
            if (candidate >= layout.Scale)
            {
                return layout; // no interior tick would fit
            }

            if (candidate * unitPixels >= MinTickSpacing)
            {
                break;
            }
        }

        int tickCount = (layout.Scale - 1) / step;
        if (tickCount <= 0 || tickCount > MaxTicks)
        {
            return layout;
        }

        float[] ticks = new float[tickCount];
        for (int i = 1; i <= tickCount; i++)
        {
            ticks[i - 1] = i * step / (float)layout.Scale;
        }

        return new BandwidthLayout(used, total, gestation, step, ticks);
    }
}
