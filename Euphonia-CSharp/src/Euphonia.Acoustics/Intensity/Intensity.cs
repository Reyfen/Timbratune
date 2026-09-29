using Euphonia.Acoustics.Numerics;

namespace Euphonia.Acoustics.Intensity;

/// <summary>An intensity contour in dB (re 2·10⁻⁵ Pa), one value per frame.</summary>
public sealed class IntensityContour
{
    internal IntensityContour(TimeGrid grid, double[] db)
    {
        Grid = grid;
        Db = db;
    }

    public TimeGrid Grid { get; }
    public IReadOnlyList<double> Db { get; }

    /// <summary>Mean over all frames, averaged in the energy domain and converted back to dB.</summary>
    public double MeanEnergyDb()
    {
        if (Db.Count == 0) return double.NaN;
        var sum = new CompensatedSum();
        foreach (var db in Db) sum.Add(Math.Pow(10, 0.1 * db));
        return 10 * Math.Log10(sum.Value / Db.Count);
    }

    /// <summary>Largest value, with parabolic refinement of interior local maxima.</summary>
    public double Maximum() => Extremum(maximum: true);

    /// <summary>Smallest value, with parabolic refinement of interior local minima.</summary>
    public double Minimum() => Extremum(maximum: false);

    private double Extremum(bool maximum)
    {
        var y = Db;
        var n = y.Count;
        if (n == 0) return double.NaN;
        var best = maximum ? Math.Max(y[0], y[n - 1]) : Math.Min(y[0], y[n - 1]);
        for (var i = 1; i < n - 1; i++)
        {
            var isExtremum = maximum
                ? y[i] > y[i - 1] && y[i] >= y[i + 1]
                : y[i] < y[i - 1] && y[i] <= y[i + 1];
            if (!isExtremum) continue;
            var (value, _) = PeakRefinement.Parabolic(y[i - 1], y[i], y[i + 1]);
            best = maximum ? Math.Max(best, value) : Math.Min(best, value);
        }
        return best;
    }
}

/// <summary>
/// Short-term intensity: squared samples weighted by a Kaiser–Bessel window
/// (Kaiser 1974) spanning 6.4 periods of the minimum pitch, mean-subtracted per
/// window, as described in the Praat manual (Sound: To Intensity…).
/// </summary>
public static class IntensityAnalyzer
{
    private const double ReferencePressureSquared = 4e-10; // (2·10⁻⁵ Pa)²

    /// <param name="minimumPitch">Lowest pitch to be smoothed out (Hz); sets the window length.</param>
    /// <param name="timeStep">Seconds between frames; 0 = a quarter of the effective window (0.8 / minimumPitch).</param>
    /// <param name="subtractMean">Remove the DC offset of each window before squaring.</param>
    public static IntensityContour Analyze(Sound sound, double minimumPitch, double timeStep = 0, bool subtractMean = true)
    {
        if (timeStep <= 0) timeStep = 0.8 / minimumPitch;
        var halfWindow = 3.2 / minimumPitch;
        var physicalWindow = 2 * halfWindow;
        var grid = sound.Grid;
        var dx = grid.Step;

        var halfSamples = (int)Math.Floor(halfWindow / dx);
        var window = new double[2 * halfSamples + 1];
        const double beta = 2 * Math.PI * Math.PI + 0.5;
        for (var k = -halfSamples; k <= halfSamples; k++)
        {
            var x = k * dx / halfWindow;
            window[k + halfSamples] = Windows.BesselI0(beta * Math.Sqrt(Math.Max(0, 1 - x * x)));
        }

        var frames = TimeGrid.ShortTermFrames(grid, physicalWindow, timeStep);
        var db = new double[frames.Count];
        var segment = new double[window.Length];
        for (var f = 0; f < frames.Count; f++)
        {
            var centre = Stats.RoundHalfUp(grid.XToIndex(frames.IndexToX(f)) + 1) - 1;
            var from = Math.Max(0, centre - halfSamples);
            var to = Math.Min(grid.Count - 1, centre + halfSamples);
            var count = to - from + 1;
            var weighted = new CompensatedSum();
            var weights = new CompensatedSum();
            for (var ch = 0; ch < sound.ChannelCount; ch++)
            {
                var samples = sound.Channel(ch);
                var seg = segment.AsSpan(0, count);
                samples.Slice(from, count).CopyTo(seg);
                if (subtractMean)
                {
                    var mean = Stats.Mean(seg);
                    for (var i = 0; i < count; i++) seg[i] -= mean;
                }
                for (var i = 0; i < count; i++)
                {
                    var w = window[from + i - centre + halfSamples];
                    weighted.Add(seg[i] * seg[i] * w);
                    weights.Add(w);
                }
            }
            var power = weighted.Value / weights.Value / ReferencePressureSquared;
            db[f] = power < 1e-30 ? -300 : 10 * Math.Log10(power);
        }
        return new IntensityContour(frames, db);
    }
}
