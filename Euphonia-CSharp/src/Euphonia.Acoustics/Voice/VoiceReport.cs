using Euphonia.Acoustics.Numerics;

namespace Euphonia.Acoustics.Voice;

/// <summary>
/// Cycle-to-cycle perturbation measures from pulse times, with the
/// definitions of the Praat manual (Voice 2. Jitter; Voice 3. Shimmer); see
/// also Farrús, Hernando &amp; Ejarque (2007).
/// </summary>
public static class VoiceReport
{
    /// <summary>
    /// Jitter (local): mean absolute difference between consecutive periods,
    /// divided by the mean period. Periods outside [shortestPeriod, longestPeriod],
    /// or pairs whose ratio exceeds <paramref name="maximumPeriodFactor"/>, are skipped.
    /// </summary>
    /// <returns>A fraction (×100 for %), or NaN if fewer than 3 usable periods.</returns>
    public static double JitterLocal(IReadOnlyList<double> pulses, double shortestPeriod = 0.0001,
        double longestPeriod = 0.02, double maximumPeriodFactor = 1.3)
    {
        var periods = pulses.Count - 1;
        if (periods < 2) return double.NaN;
        var sum = new CompensatedSum();
        for (var i = 1; i < pulses.Count - 1; i++)
        {
            var p1 = pulses[i] - pulses[i - 1];
            var p2 = pulses[i + 1] - pulses[i];
            if (IsPeriodPair(p1, p2, shortestPeriod, longestPeriod, maximumPeriodFactor)) sum.Add(Math.Abs(p1 - p2));
            else periods--;
        }
        if (periods < 2) return double.NaN;
        return sum.Value / (periods - 1) / MeanPeriod(pulses, shortestPeriod, longestPeriod, maximumPeriodFactor);
    }

    /// <summary>
    /// Shimmer (local): mean absolute difference between the amplitudes of
    /// consecutive periods, divided by the mean amplitude. Each period's
    /// amplitude is the RMS of the sound around the pulse under an asymmetric
    /// Hann window reaching 20% of the adjacent periods.
    /// </summary>
    public static double ShimmerLocal(IReadOnlyList<double> pulses, Sound sound, double shortestPeriod = 0.0001,
        double longestPeriod = 0.02, double maximumPeriodFactor = 1.3, double maximumAmplitudeFactor = 1.6)
    {
        if (pulses.Count < 3) return double.NaN; // too few pulses
        var times = new List<double>();
        var amplitudes = new List<double>();
        for (var i = 1; i < pulses.Count - 1; i++)
        {
            var p1 = pulses[i] - pulses[i - 1];
            var p2 = pulses[i + 1] - pulses[i];
            if (!IsPeriodPair(p1, p2, shortestPeriod, longestPeriod, maximumPeriodFactor)) continue;
            var amplitude = HannWindowedRms(sound, pulses[i], 0.2 * p1, 0.2 * p2);
            if (!(amplitude > 0)) continue;
            times.Add(pulses[i]);
            amplitudes.Add(amplitude);
        }

        var numerator = new CompensatedSum();
        var count = 0;
        for (var i = 1; i < amplitudes.Count; i++)
        {
            var period = times[i] - times[i - 1];
            if (shortestPeriod != longestPeriod && (period < shortestPeriod || period > longestPeriod)) continue;
            double a1 = amplitudes[i - 1], a2 = amplitudes[i];
            if ((a1 > a2 ? a1 / a2 : a2 / a1) > maximumAmplitudeFactor) continue;
            numerator.Add(Math.Abs(a1 - a2));
            count++;
        }
        if (count < 1) return double.NaN;

        // The reference normalizes by the mean of all amplitudes except the last one.
        var denominator = new CompensatedSum();
        for (var i = 0; i < amplitudes.Count - 1; i++) denominator.Add(amplitudes[i]);
        var meanAmplitude = denominator.Value / (amplitudes.Count - 1);
        return meanAmplitude == 0 ? double.NaN : numerator.Value / count / meanAmplitude;
    }

    private static bool IsPeriodPair(double p1, double p2, double shortest, double longest, double maxFactor)
    {
        if (shortest == longest) return true;
        var factor = p1 > p2 ? p1 / p2 : p2 / p1;
        return p1 >= shortest && p1 <= longest && p2 >= shortest && p2 <= longest && factor <= maxFactor;
    }

    /// <summary>
    /// Mean of the intervals that count as periods: inside the period bounds and
    /// not more than <paramref name="maxFactor"/> away from BOTH neighbours.
    /// </summary>
    private static double MeanPeriod(IReadOnlyList<double> pulses, double shortest, double longest, double maxFactor)
    {
        var sum = new CompensatedSum();
        var count = 0;
        for (var i = 0; i < pulses.Count - 1; i++)
        {
            var interval = pulses[i + 1] - pulses[i];
            if (shortest != longest)
            {
                if (interval <= 0 || interval < shortest || interval > longest) continue;
                if (!double.IsNaN(maxFactor) && maxFactor >= 1)
                {
                    double? previous = i >= 1 ? pulses[i] - pulses[i - 1] : null;
                    double? next = i + 2 < pulses.Count ? pulses[i + 2] - pulses[i + 1] : null;
                    var previousFactor = Factor(interval, previous);
                    var nextFactor = Factor(interval, next);
                    if (previousFactor > maxFactor && nextFactor > maxFactor) continue;
                }
            }
            sum.Add(interval);
            count++;
        }
        return count > 0 ? sum.Value / count : double.NaN;

        static double Factor(double interval, double? neighbour)
        {
            if (neighbour is not { } n || n <= 0) return double.NaN; // NaN never exceeds the limit
            var f = interval / n;
            return f > 0 && f < 1 ? 1 / f : f;
        }
    }

    private static double HannWindowedRms(Sound sound, double tmid, double widthLeft, double widthRight)
    {
        var grid = sound.Grid;
        var (from, to) = grid.WindowIndices(tmid - widthLeft, tmid + widthRight);
        if (to - from + 1 < 3) return double.NaN;
        var sumSquares = new CompensatedSum();
        var windowSquares = new CompensatedSum();
        for (var i = from; i <= to; i++)
        {
            var t = grid.IndexToX(i);
            var width = t < tmid ? widthLeft : widthRight;
            var w = 0.5 + 0.5 * Math.Cos(Math.PI * (t - tmid) / width);
            var sample = sound.ChannelCount == 1 ? sound.Channel(0)[i] : 0.5 * (sound.Channel(0)[i] + sound.Channel(1)[i]);
            var v = sample * w;
            sumSquares.Add(v * v);
            windowSquares.Add(w * w);
        }
        return Math.Sqrt(sumSquares.Value / windowSquares.Value);
    }
}
