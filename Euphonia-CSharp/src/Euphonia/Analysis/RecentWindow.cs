using Euphonia.Core.Analysis;

namespace Euphonia.Analysis;

/// <summary>How the values inside a smoothing window are combined.</summary>
public enum WindowStatistic
{
    /// <summary>Robust to the odd wrong frame (formant jumps, HNR spikes).</summary>
    Median,
    /// <summary>Mean of 10^(dB/10), back in dB — how loudness is averaged.</summary>
    EnergyMean,
    /// <summary>Sample standard deviation — how much the value moves.</summary>
    StandardDeviation,
}

/// <summary>
/// Smoothed lines for the live timelines. Each value is the statistic over a window
/// <em>centred</em> on its time, evaluated on a fixed time grid. Near the live end the
/// window only has the past, so the newest values are a quick provisional estimate;
/// as more audio arrives the window fills in and those values are corrected in place.
/// Lines break where the data has a gap (a <see cref="double.NaN"/> value), and a
/// window never reaches across one.
/// </summary>
public sealed record SmoothingSpec(
    WindowStatistic Statistic,
    double HalfWidth,
    int MinimumCount,
    double MaxGap,
    double Step = 0.05)
{
    /// <summary>Radius (grid steps) of the light mean applied after the window statistic.</summary>
    public int PolishRadius { get; init; } = 2;
}

public static class RecentWindow
{
    /// <summary>
    /// The smoothed line over <paramref name="points"/> (sorted by time). The points are
    /// split into stretches wherever two neighbours are more than MaxGap apart; each
    /// stretch is smoothed on its own (a window never reaches across a pause) on the grid
    /// k·step, plus its first and last point's times so the line reaches "now". Stretches
    /// are separated by a NaN, and grid times before <paramref name="from"/> are skipped.
    /// </summary>
    public static List<TimedValue> Smooth(IReadOnlyList<TimedValue> points, SmoothingSpec spec, double from = 0)
    {
        var raw = new List<TimedValue>();
        var scratch = new double[16];
        var first = 0;
        while (first < points.Count)
        {
            var last = first;
            while (last + 1 < points.Count && points[last + 1].T - points[last].T <= spec.MaxGap) last++;
            if (points[last].T >= from)
            {
                if (raw.Count > 0) raw.Add(new TimedValue(points[first].T, double.NaN));
                if (scratch.Length < last - first + 1) scratch = new double[last - first + 1];
                SmoothStretch(points, first, last, spec, from, raw, scratch);
            }
            first = last + 1;
        }
        return Polish(raw, spec.PolishRadius);
    }

    private static void SmoothStretch(IReadOnlyList<TimedValue> points, int first, int last, SmoothingSpec spec, double from,
        List<TimedValue> output, double[] scratch)
    {
        var t0 = points[first].T;
        var t1 = points[last].T;
        var times = new List<double> { t0 };
        for (var k = (long)Math.Floor(t0 / spec.Step) + 1; k * spec.Step < t1 - 1e-9; k++) times.Add(k * spec.Step);
        if (t1 > t0) times.Add(t1);

        int lo = first, hi = first;
        foreach (var g in times)
        {
            while (lo <= last && points[lo].T < g - spec.HalfWidth) lo++;
            while (hi <= last && points[hi].T <= g + spec.HalfWidth) hi++;
            if (g < from) continue;
            var n = hi - lo;
            output.Add(new TimedValue(g, n < spec.MinimumCount ? double.NaN : Statistic(points, lo, n, spec.Statistic, scratch)));
        }
    }

    private static double Statistic(IReadOnlyList<TimedValue> points, int lo, int n, WindowStatistic statistic, double[] scratch)
    {
        switch (statistic)
        {
            case WindowStatistic.Median:
            {
                var span = scratch.AsSpan(0, n);
                for (var i = 0; i < n; i++) span[i] = points[lo + i].Value;
                span.Sort();
                return n % 2 == 1 ? span[n / 2] : 0.5 * (span[n / 2 - 1] + span[n / 2]);
            }
            case WindowStatistic.EnergyMean:
            {
                double sum = 0;
                for (var i = 0; i < n; i++) sum += Math.Pow(10, 0.1 * points[lo + i].Value);
                return sum > 0 ? 10 * Math.Log10(sum / n) : -300;
            }
            default:
            {
                if (n < 2) return double.NaN;
                double sum = 0, sumSquares = 0;
                for (var i = 0; i < n; i++)
                {
                    var v = points[lo + i].Value;
                    sum += v;
                    sumSquares += v * v;
                }
                return Math.Sqrt(Math.Max(0, (sumSquares - sum * sum / n) / (n - 1)));
            }
        }
    }

    /// <summary>A short centred mean over neighbouring grid values, never across a break.</summary>
    private static List<TimedValue> Polish(List<TimedValue> line, int radius)
    {
        if (radius <= 0) return line;
        var result = new List<TimedValue>(line.Count);
        for (var i = 0; i < line.Count; i++)
        {
            if (double.IsNaN(line[i].Value))
            {
                result.Add(line[i]);
                continue;
            }
            double sum = line[i].Value;
            var n = 1;
            for (var j = i - 1; j >= Math.Max(0, i - radius) && !double.IsNaN(line[j].Value); j--) { sum += line[j].Value; n++; }
            for (var j = i + 1; j <= Math.Min(line.Count - 1, i + radius) && !double.IsNaN(line[j].Value); j++) { sum += line[j].Value; n++; }
            result.Add(new TimedValue(line[i].T, sum / n));
        }
        return result;
    }

    /// <summary>The points from the first one at or after <paramref name="from"/> (binary search).</summary>
    public static IReadOnlyList<TimedValue> From(IReadOnlyList<TimedValue> points, double from)
    {
        int lo = 0, hi = points.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (points[mid].T < from) lo = mid + 1;
            else hi = mid;
        }
        if (lo == 0) return points;
        var result = new List<TimedValue>(points.Count - lo);
        for (var i = lo; i < points.Count; i++) result.Add(points[i]);
        return result;
    }
}
