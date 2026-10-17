using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Analysis.Metrics.Clarity;
using Reyfen.Timbratune.Core.Analysis.Metrics.Loudness;
using Reyfen.Timbratune.Core.Analysis.Metrics.Resonance;
using Reyfen.Timbratune.Core.Analysis.Metrics.Steadiness;
using Reyfen.Timbratune.Core.Analysis.Metrics.Weight;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// The statistics half of analyze.py: everything it does with numpy /
/// statistics on top of the raw measurements. Kept pure (no I/O) so it can be
/// unit-tested and reused by any future analysis engine.
/// </summary>
public static class AnalysisPostProcessor
{
    public const double DefaultRegisterFloorHz = 130.0;
    public const double SemitoneRefHz = 100.0;

    public static AnalysisResult Process(RawAnalysis raw, double registerFloorHz = DefaultRegisterFloorHz) =>
        Process(raw, registerFloorHz, MetricRegistry.Default);

    /// <summary>
    /// The take from its raw values: each metric module fills its section; the phrasing module
    /// makes the detail (contour, phrases, register); then every module fills its columns of
    /// the per-phrase breakdown and the time trends.
    /// </summary>
    public static AnalysisResult Process(RawAnalysis raw, double registerFloorHz, MetricRegistry registry)
    {
        var metrics = new Recording { DurationS = Clean(raw.Get("duration")) };
        var context = new MetricContext(raw, metrics, registerFloorHz);
        foreach (var module in registry.Modules) module.Summarize(context);
        var detail = context.Detail ?? throw new InvalidOperationException("No metric module made the take's detail.");

        var data = new object?[registry.Modules.Count];
        for (var i = 0; i < data.Length; i++) data[i] = registry.Modules[i].DetailData(raw);
        var inputs = Inputs(detail, data);
        detail.PhraseMetrics = PhraseBreakdown(inputs, registry);
        detail.Trends = TimeTrends(inputs, registry);
        return new AnalysisResult(metrics, detail);
    }

    private static DetailInputs Inputs(RecordingDetail detail, object?[] data) => new()
    {
        Detail = detail,
        Voiced = detail.Frames.T.Zip(detail.Frames.Hz)
            .Where(p => p.Second is > 0)
            .Select(p => (T: p.First, Hz: p.Second!.Value))
            .ToList(),
        Data = data,
    };

    /// <summary>
    /// The detail inputs from explicit frames (the public <see cref="TimeTrends(RecordingDetail, IReadOnlyList{ValueTuple{double, double, double}}?, IReadOnlyList{ValueTuple{double, double}}?, IReadOnlyList{ValueTuple{double, double}}?, IReadOnlyList{double}?, IReadOnlyList{ValueTuple{double, double}}?)"/>
    /// and <see cref="PhraseBreakdown(RecordingDetail, IReadOnlyList{ValueTuple{double, double}}?, IReadOnlyList{ValueTuple{double, double}}?)"/>):
    /// each frame list goes to the module that owns it.
    /// </summary>
    private static DetailInputs Inputs(RecordingDetail detail, MetricRegistry registry,
        IReadOnlyList<(double T, double F2, double F3)>? formantFrames, IReadOnlyList<(double T, double H1A3c)>? weightFrames,
        IReadOnlyList<(double T, double Db)>? hnrFrames, IReadOnlyList<double>? pulses, IReadOnlyList<(double T, double Db)>? intensityFrames)
    {
        var data = new object?[registry.Modules.Count];
        Put<WeightModule>(weightFrames);
        Put<ResonanceModule>(formantFrames);
        Put<SteadinessModule>(pulses is { Count: > 0 } ? pulses : null);
        Put<ClarityModule>(hnrFrames is { Count: > 0 } ? hnrFrames : null);
        Put<LoudnessModule>(intensityFrames is { Count: > 0 } ? intensityFrames : null);
        return Inputs(detail, data);

        void Put<T>(object? value) where T : IMetricModule
        {
            var i = registry.IndexOf<T>();
            if (i >= 0) data[i] = value;
        }
    }

    // ------------------------------------------------------------------
    // trends within the take (C#-port addition): ~10 slices of whole seconds
    // ------------------------------------------------------------------

    /// <summary>How many points the trend charts aim for, at most.</summary>
    public const int TrendPointTarget = 10;

    /// <summary>Slice lengths (s) the trends may use: each a "round" number of seconds.</summary>
    private static readonly int[] TrendSteps = [1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 30, 40, 45, 60, 90, 120, 180, 240, 300, 600, 900, 1200, 1800, 3600];

    /// <summary>
    /// The shortest round slice length that cuts the take into at most
    /// <see cref="TrendPointTarget"/> points (27.6 s → 3 s → 3, 6, … 27).
    /// </summary>
    public static int TrendStep(double duration)
    {
        foreach (var step in TrendSteps)
            if (Math.Floor(duration / step) <= TrendPointTarget) return step;
        return (int)Math.Ceiling(duration / TrendPointTarget);
    }

    /// <summary>
    /// The take cut into slices of <see cref="TrendStep"/> seconds, one point per slice at
    /// Step, 2·Step, … ≤ duration. Each slice is centred on its point; the first starts at
    /// 0 and the last runs to the end, so every frame counts exactly once. Pitch values
    /// come from the 10 ms contour, the rest from the frames given (null = not measured,
    /// e.g. rebuilding an older take from its contour only).
    /// </summary>
    public static TakeTrends TimeTrends(
        RecordingDetail detail,
        IReadOnlyList<(double T, double F2, double F3)>? formantFrames = null,
        IReadOnlyList<(double T, double H1A3c)>? weightFrames = null,
        IReadOnlyList<(double T, double Db)>? hnrFrames = null,
        IReadOnlyList<double>? pulses = null,
        IReadOnlyList<(double T, double Db)>? intensityFrames = null) =>
        TimeTrends(Inputs(detail, MetricRegistry.Default, formantFrames, weightFrames, hnrFrames, pulses, intensityFrames), MetricRegistry.Default);

    private static TakeTrends TimeTrends(DetailInputs inputs, MetricRegistry registry)
    {
        var detail = inputs.Detail;
        var duration = detail.DurationS;
        var step = TrendStep(duration);
        var count = (int)Math.Floor(duration / step + 1e-9);

        var trends = new TakeTrends { StepS = step, Version = TakeTrends.CurrentVersion };
        for (var k = 1; k <= count; k++)
        {
            var t = k * step;
            var start = k == 1 ? 0 : t - step / 2.0;
            var end = k == count ? duration : t + step / 2.0;
            var window = new TrendWindow(start, end, k == count);
            inputs.WindowHz = inputs.Voiced.Where(v => window.Contains(v.T)).Select(v => v.Hz).ToList();
            var slice = new TrendSlice(registry.Count) { T = t, Start = Round(start, 3), End = Round(end, 3) };
            for (var i = 0; i < registry.Modules.Count; i++)
                registry.Modules[i].FillTrend(inputs, inputs.Data[i], window, registry.ValuesOf(i, slice.Values));
            trends.Points.Add(slice);
        }
        return trends;
    }

    // ------------------------------------------------------------------
    // per-phrase breakdown (C#-port addition) — the "trends within this take"
    // ------------------------------------------------------------------

    /// <summary>
    /// One entry per phrase in <paramref name="detail"/>.Phrases: pitch, melody
    /// and ending pitch from the 10 ms contour; F2 / weight from the frames
    /// (time, value) that fall inside the phrase, when given. Pass null for
    /// those to rebuild the pitch-based values of an older take.
    /// </summary>
    public static List<PhraseMetrics> PhraseBreakdown(
        RecordingDetail detail,
        IReadOnlyList<(double T, double F2)>? f2Frames = null,
        IReadOnlyList<(double T, double H1A3c)>? weightFrames = null) =>
        PhraseBreakdown(Inputs(detail, MetricRegistry.Default, f2Frames?.Select(f => (f.T, f.F2, double.NaN)).ToList(), weightFrames, null, null, null),
            MetricRegistry.Default);

    private static List<PhraseMetrics> PhraseBreakdown(DetailInputs inputs, MetricRegistry registry)
    {
        var result = new List<PhraseMetrics>();
        foreach (var phrase in inputs.Detail.Phrases)
        {
            inputs.WindowHz = inputs.Voiced.Where(v => phrase.Start <= v.T && v.T <= phrase.End).Select(v => v.Hz).ToList();
            var metrics = new PhraseMetrics(registry.Count) { Start = phrase.Start, End = phrase.End };
            for (var i = 0; i < registry.Modules.Count; i++)
                registry.Modules[i].FillPhrase(inputs, inputs.Data[i], phrase, registry.ValuesOf(i, metrics.Values));
            result.Add(metrics);
        }
        return result;
    }

    // ------------------------------------------------------------------
    // helpers (Python semantics)
    // ------------------------------------------------------------------

    /// <summary>analyze.py clean(): round to 2 dp; NaN/inf → null.</summary>
    internal static double? Clean(double? value) =>
        value is { } v && double.IsFinite(v) ? Round(v, 2) : null;

    /// <summary>Python round(x, n) — banker's rounding.</summary>
    internal static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.ToEven);

    internal static double HzToSt(double hz) => 12.0 * Math.Log2(hz / SemitoneRefHz);

    /// <summary>statistics.median — mean of the middle two for even counts.</summary>
    internal static double? Median(List<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>statistics.pstdev.</summary>
    internal static double PopulationSd(List<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }

    /// <summary>statistics.stdev (sample), or null with fewer than two values.</summary>
    internal static double? SampleSd(List<double> values)
    {
        if (values.Count < 2) return null;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    /// <summary>np.polyfit(x, y, 1)[0] — least-squares slope.</summary>
    internal static double LinearSlope(IReadOnlyList<(double X, double Y)> pts)
    {
        var mx = pts.Average(p => p.X);
        var my = pts.Average(p => p.Y);
        double sxy = 0, sxx = 0;
        foreach (var (x, y) in pts)
        {
            sxy += (x - mx) * (y - my);
            sxx += (x - mx) * (x - mx);
        }
        return sxy / sxx;
    }
}

/// <summary>
/// One analyzed take: <see cref="Metrics"/> carries every metric plus Register
/// (id/label/date/audio are filled in by the store); <see cref="Detail"/> is the
/// analysis/&lt;id&gt;.json payload; <see cref="Series"/> the per-frame lists saved beside
/// it (full analysis only; the store writes them to a file and nothing keeps them after).
/// </summary>
public sealed record AnalysisResult(Recording Metrics, RecordingDetail Detail, TakeSeries? Series = null);
