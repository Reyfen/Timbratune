using Reyfen.Timbratune.Core.Analysis;

namespace Reyfen.Timbratune.Analysis;

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
/// <para>
/// Layers, most to least certain: the saved full analysis (after Stop); the live frame
/// analysis (the points); a window with enough data on both sides ("settled"); a window
/// missing part of its future (the newest ~half window); and a prediction. The unsettled
/// tail is blended toward a damped trend of the settled line (<see cref="TrendDamping"/>
/// of its recent slope) — the less future its window has, the more it leans on that
/// prediction — and the ongoing stretch is extended to "now" by the same prediction, so
/// the current point is always shown and moves steadily, still leaning the right way.
/// </para>
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

    /// <summary>Fraction of the settled line's recent slope the prediction follows (0 = flat, 1 = full trend).</summary>
    public double TrendDamping { get; init; } = 0.5;

    /// <summary>Weight kept by an unsettled window's own value at the very end (the rest is the prediction).</summary>
    public double MinimumTrust { get; init; } = 0.15;

    /// <summary>How much settled line (s) the trend's slope is fitted over.</summary>
    public double TrendSeconds { get; init; } = 1.0;

    /// <summary>How much of its future half-width a window needs to count as settled (1 = all of it).</summary>
    public double SettledCoverage { get; init; } = 0.5;

    /// <summary>
    /// When set, the line breaks where one grid step jumps by more than this ratio (e.g. an
    /// octave slip in pitch), instead of drawing a near-vertical connection. Positive values only.
    /// </summary>
    public double? MaxStepRatio { get; init; }
}

public static class RecentWindow
{
    /// <summary>
    /// The smoothed line over <paramref name="points"/> (sorted by time). The points are
    /// split into stretches wherever two neighbours are more than MaxGap apart; each
    /// stretch is smoothed on its own (a window never reaches across a pause) on the grid
    /// k·step, plus its first and last point's times so the line reaches "now". Stretches
    /// are separated by a NaN, and grid times before <paramref name="from"/> are skipped.
    /// When <paramref name="now"/> is given and the last stretch is still going (its last
    /// point is at most MaxGap before now), the line is extended to now by prediction.
    /// </summary>
    public static List<TimedValue> Smooth(IReadOnlyList<TimedValue> points, SmoothingSpec spec, double from = 0, double? now = null)
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
                var stretch = new List<TimedValue>();
                SmoothStretch(points, first, last, spec, from, stretch, scratch);
                if (spec.MaxStepRatio is { } ratio) stretch = BreakJumps(stretch, ratio);
                var ongoing = last == points.Count - 1 && now is { } n && n - points[last].T <= spec.MaxGap ? now : null;
                Predict(stretch, points[last].T, spec, ongoing);
                raw.AddRange(stretch);
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

    private static List<TimedValue> BreakJumps(List<TimedValue> line, double ratio)
    {
        var result = new List<TimedValue>(line.Count);
        for (var i = 0; i < line.Count; i++)
        {
            if (i > 0 && line[i - 1].Value is var a && line[i].Value is var b && !double.IsNaN(a) && !double.IsNaN(b)
                && a > 0 && b > 0 && Math.Max(a, b) / Math.Min(a, b) > ratio)
                result.Add(new TimedValue(0.5 * (line[i - 1].T + line[i].T), double.NaN));
            result.Add(line[i]);
        }
        return result;
    }

    /// <summary>
    /// Blends the stretch's unsettled tail (windows missing future data) toward a damped
    /// trend of its settled part, and extends it to <paramref name="now"/> when given.
    /// </summary>
    private static void Predict(List<TimedValue> line, double end, SmoothingSpec spec, double? now)
    {
        // The last settled value: its window saw a full half-width of future.
        var settled = -1;
        for (var i = line.Count - 1; i >= 0; i--)
        {
            if (line[i].T <= end - spec.SettledCoverage * spec.HalfWidth + 1e-9 && !double.IsNaN(line[i].Value))
            {
                settled = i;
                break;
            }
        }
        if (settled < 0)
        {
            // Too short to have settled yet: hold the newest value until it has.
            if (now is { } t && FindLast(line) is { } newest && t > line[^1].T) line.Add(new TimedValue(t, newest.Value));
            return;
        }

        var anchor = line[settled];
        var slope = Slope(line, settled, spec.TrendSeconds);
        double Prediction(double t) => anchor.Value + spec.TrendDamping * slope * (t - anchor.T);

        for (var i = settled + 1; i < line.Count; i++)
        {
            var g = line[i].T;
            var coverage = Math.Clamp((end - g) / (spec.SettledCoverage * spec.HalfWidth), 0, 1);
            var trust = spec.MinimumTrust + (1 - spec.MinimumTrust) * coverage;
            var own = line[i].Value;
            line[i] = new TimedValue(g, double.IsNaN(own) ? Prediction(g) : trust * own + (1 - trust) * Prediction(g));
        }
        if (now is { } current && current > line[^1].T)
        {
            // The newest window's own estimate carries its minimum trust into "now" as well.
            var own = line[^1].Value;
            line.Add(new TimedValue(current, spec.MinimumTrust * own + (1 - spec.MinimumTrust) * Prediction(current)));
        }
    }

    /// <summary>Least-squares slope of the line over the <paramref name="seconds"/> up to index <paramref name="at"/>.</summary>
    private static double Slope(List<TimedValue> line, int at, double seconds)
    {
        double st = 0, sv = 0, stt = 0, stv = 0;
        var n = 0;
        for (var i = at; i >= 0 && line[i].T >= line[at].T - seconds; i--)
        {
            if (double.IsNaN(line[i].Value)) break;
            var t = line[i].T - line[at].T;
            st += t;
            sv += line[i].Value;
            stt += t * t;
            stv += t * line[i].Value;
            n++;
        }
        var denominator = n * stt - st * st;
        return n < 3 || denominator <= 0 ? 0 : (n * stv - st * sv) / denominator;
    }

    private static TimedValue? FindLast(List<TimedValue> line)
    {
        for (var i = line.Count - 1; i >= 0; i--)
            if (!double.IsNaN(line[i].Value)) return line[i];
        return null;
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
