using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Voice;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// Lists the per-frame series are written into. The live analysis keeps two sets and
/// refills them in turn (no whole-take lists allocated per update); the full analysis
/// uses one set once.
/// </summary>
public sealed class FrameSeriesBuffers
{
    /// <param name="metricCount">The registry's <see cref="MetricRegistry.Count"/>.</param>
    /// <param name="forSavedTake">Also the series only a saved take keeps (F1; the live graphs don't show it).</param>
    public FrameSeriesBuffers(int metricCount, bool forSavedTake = false)
    {
        Lines = new List<TimedValue>[metricCount];
        for (var i = 0; i < Lines.Length; i++) Lines[i] = [];
        ForSavedTake = forSavedTake;
    }

    /// <summary>One list per metric, by <see cref="MetricId.Index"/> (empty for metrics without a series).</summary>
    public List<TimedValue>[] Lines { get; }

    public bool ForSavedTake { get; }
    internal readonly List<(double T, double F0)> Voiced = [];
    internal readonly List<double> LoudTimes = [], LoudScratch = [];
    internal RawAnalysis.WeightRow?[] Rows = [];
}

/// <summary>
/// The per-frame series as the live graphs and the saved take's lists show them. Shared by
/// <see cref="LiveAnalyzer"/> and <see cref="AcousticsAnalysisEngine"/>, so both use the
/// same definitions:
/// loudness on every intensity frame, HNR on voiced frames, F1–F3 on loud voiced frames
/// with a plausible F1 (5500 Hz ceiling), weight (corrected H1*–A3*) on every measurable
/// voiced frame, and jitter per voiced stretch.
/// </summary>
public static class FrameSeriesBuilder
{
    /// <summary>Every module's series for these tracks, into <paramref name="b"/> (live: pooled, refilled).</summary>
    public static void Build(in TrackView t, FrameSeriesBuffers b) => Build(t, b, MetricRegistry.Default);

    public static void Build(in TrackView t, FrameSeriesBuffers b, MetricRegistry registry)
    {
        for (var i = 0; i < registry.Modules.Count; i++) registry.Modules[i].BuildSeries(t, registry.LinesOf(i, b));
    }

    /// <summary>
    /// Local jitter per voiced stretch of a whole take, from all its pulses: each stretch
    /// uses the pulses inside it (what the live analysis measures stretch by stretch).
    /// </summary>
    public static List<(double End, double Jitter)> StretchJitter(PitchContour pitch, IReadOnlyList<double> pulses)
    {
        var result = new List<(double, double)>();
        var inside = new List<double>();
        var p = 0;
        foreach (var (left, right) in PulseDetector.VoicedStretches(pitch))
        {
            while (p < pulses.Count && pulses[p] < left) p++;
            inside.Clear();
            for (var q = p; q < pulses.Count && pulses[q] <= right; q++) inside.Add(pulses[q]);
            result.Add((right, VoiceReport.JitterLocal(inside)));
        }
        return result;
    }
    /// <summary>
    /// The saved take's series from the full analysis' tracks (rounded as the saved contour
    /// is: times to 1 ms, Hz to 0.1, dB to 0.01, % to 0.001).
    /// </summary>
    public static TakeSeries ForTake(in TrackView t) => ForTake(t, MetricRegistry.Default);

    public static TakeSeries ForTake(in TrackView t, MetricRegistry registry)
    {
        if (t.Harmonicity is null) throw new ArgumentException("The tracks have no harmonicity.", nameof(t));
        var b = new FrameSeriesBuffers(registry.Count, forSavedTake: true);
        Build(t, b, registry);
        var written = new Dictionary<string, FrameSeries>(StringComparer.Ordinal);
        for (var i = 0; i < registry.Modules.Count; i++) registry.Modules[i].WriteSeries(t, registry.LinesOf(i, b), written);

        // series.json keeps its key order whatever the module order: the known keys first, as always.
        var take = new TakeSeries();
        foreach (var key in KeyOrder)
            if (written.Remove(key, out var known)) take.Series[key] = known;
        foreach (var (key, other) in written) take.Series[key] = other;
        return take;
    }

    private static readonly string[] KeyOrder =
        [TakeSeries.Pitch, TakeSeries.Loudness, TakeSeries.Hnr, TakeSeries.F1, TakeSeries.F2, TakeSeries.F3, TakeSeries.Weight, TakeSeries.Jitter];

    internal const string FormantFrames = "voiced pitch frames within 10 dB of the loudest frames (ignoring the top 1 %, so a click doesn't count), with F1 in 250-1000 Hz (5500 Hz ceiling)";

    internal static FrameSeries Series(List<TimedValue> points, string unit, double? step, int digits, string description)
    {
        var series = new FrameSeries { Unit = unit, StepS = step, Description = description };
        series.T.Capacity = series.Values.Capacity = points.Count;
        foreach (var p in points)
        {
            series.T.Add(Math.Round(p.T, 3));
            series.Values.Add(double.IsFinite(p.Value) ? Math.Round(p.Value, digits) : null);
        }
        return series;
    }
}
