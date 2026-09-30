using Euphonia.Analysis;
using Euphonia.Core.Analysis;
using Euphonia.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Euphonia.Core.Models;

namespace Euphonia.ViewModels;

/// <summary>
/// One live metric chart: fixed title, zones and scale, plus the smoothed line and visible
/// time range, which update in place so the chart (and its easing) survives each refresh.
/// </summary>
public sealed partial class TimelineViewModel(string title, string caption, IReadOnlyList<Zone> zones, double lo, double hi)
    : ObservableObject
{
    public string Title { get; } = title;
    public string Caption { get; } = caption;
    public IReadOnlyList<Zone> Zones { get; } = zones;
    public double Lo { get; } = lo;
    public double Hi { get; } = hi;

    [ObservableProperty] private IReadOnlyList<TimedValue> _line = [];
    [ObservableProperty] private double _axisStart;
    [ObservableProperty] private double _axisDuration = 10;
}

/// <summary>A choice for how much of the take the live graphs show.</summary>
public sealed record LiveWindowOption(string Label, double? Seconds)
{
    public static IReadOnlyList<LiveWindowOption> All { get; } =
    [
        new("whole take", null),
        new("last 10 s", 10),
        new("last 30 s", 30),
        new("last 60 s", 60),
        new("last 120 s", 120),
    ];

    public override string ToString() => Label;
}

/// <summary>
/// The live timelines shown while recording. With the whole take shown, the time axis
/// grows in 10-second steps (10 → 20 → 30 s …); with a window, the axis slides so it
/// always shows the last N seconds at a constant scale.
/// Each line is smoothed over a window centred on its time: the newest part is a quick
/// estimate from the past only, and it is corrected in place as the next audio arrives.
/// The unsettled tail leans on a damped trend prediction, which also carries the line to
/// "now" so the current point is always shown (see <see cref="SmoothingSpec"/>).
/// One instance lives for the whole recording; <see cref="Compute"/> does the work off the
/// UI thread and <see cref="Apply"/> updates the charts in place.
/// </summary>
public sealed partial class LiveTimelinesViewModel : ObservableObject
{
    // Loudness follows syllables (several rises and falls a second), which a trend can't
    // anticipate: measured, predicting it made the dot both jumpier and less accurate, so
    // its newest value is simply held until "now".
    private static readonly SmoothingSpec LoudnessSmoothing = new(WindowStatistic.EnergyMean, 0.5, 5, 0.3)
        { TrendDamping = 0, MinimumTrust = 1 };
    private static readonly SmoothingSpec FrameSmoothing = new(WindowStatistic.Median, 0.5, 5, 0.4);
    private static readonly SmoothingSpec VowelSmoothing = new(WindowStatistic.Median, 1.0, 6, 0.8) { PolishRadius = 4 };
    private static readonly SmoothingSpec MovementSmoothing = new(WindowStatistic.StandardDeviation, 1.0, 10, 0.4);
    private static readonly SmoothingSpec JitterSmoothing = new(WindowStatistic.Median, 1.5, 1, 3.0) { PolishRadius = 4 };

    /// <summary>What one refresh shows: the visible range, the pitch detail and every line (in <see cref="Timelines"/> order).</summary>
    public sealed record Frame(double Elapsed, double AxisStart, double AxisDuration, RecordingDetail? Detail, IReadOnlyList<TimedValue>[] Lines);

    public LiveTimelinesViewModel()
    {
        TimelineViewModel Metric(MetricKey key, string caption)
        {
            var m = Metrics.All[key];
            return new TimelineViewModel(m.Title, caption, m.Zones, m.Lo, m.Hi);
        }
        Timelines =
        [
            Metric(MetricKey.Loudness, "energy average over about 1 s"),
            Metric(MetricKey.Sd, "pitch movement over about 2 s"),
            Metric(MetricKey.Hnr, "median over about 1 s"),
            Metric(MetricKey.F2, "on loud vowels, median over about 2 s"),
            Metric(MetricKey.F3, "on loud vowels, median over about 2 s"),
            Metric(MetricKey.Weight, "median over about 2 s"),
            new TimelineViewModel("In-register melody", "in-register pitch movement over about 2 s (st)", Zones.Melody, 0, 7),
            Metric(MetricKey.Jitter, "median of finished voiced stretches over about 3 s"),
        ];
    }

    [ObservableProperty] private double _elapsed;
    [ObservableProperty] private double _axisStart;
    [ObservableProperty] private double _axisDuration = 10;
    [ObservableProperty] private RecordingDetail? _detail;

    public IReadOnlyList<Zone> PitchZones => Zones.Pitch;
    public IReadOnlyList<TimelineViewModel> Timelines { get; }

    /// <summary>10 s, then 20, 30, … — the next multiple of 10 s at or above the elapsed time.</summary>
    public static double AxisFor(double elapsed) => Math.Max(10, 10 * Math.Ceiling(elapsed / 10 - 1e-9));

    /// <summary>Everything a refresh needs, computed from a snapshot (safe on any thread).</summary>
    public static Frame Compute(LiveSnapshot snapshot, double? window = null)
    {
        double start, duration;
        if (window is { } w)
        {
            duration = w;
            start = Math.Max(w, snapshot.Elapsed) - w;
        }
        else
        {
            start = 0;
            duration = AxisFor(snapshot.Elapsed);
        }
        var detail = snapshot.Result?.Detail;
        var now = snapshot.Elapsed;
        List<TimedValue> Smooth(IReadOnlyList<TimedValue> points, SmoothingSpec spec) =>
            RecentWindow.Smooth(RecentWindow.From(points, start - spec.HalfWidth - spec.MaxGap), spec, start, now);

        var s = snapshot.Series;
        var pitch = Voiced(detail);
        var floor = detail?.RegisterFloorHz ?? AnalysisPostProcessor.DefaultRegisterFloorHz;
        var melody = pitch.Where(p => p.Value >= floor).Select(p => new TimedValue(p.T, Semitones(p.Value))).ToList();
        return new Frame(now, start, duration, detail,
        [
            Smooth(s.Loudness, LoudnessSmoothing),
            Smooth(pitch, MovementSmoothing),
            Smooth(s.Hnr, FrameSmoothing),
            Smooth(s.F2, VowelSmoothing),
            Smooth(s.F3, VowelSmoothing),
            Smooth(s.Weight, VowelSmoothing),
            Smooth(melody, MovementSmoothing),
            Smooth(s.Jitter, JitterSmoothing),
        ]);
    }

    /// <summary>Shows a computed frame (UI thread).</summary>
    public void Apply(Frame frame)
    {
        Elapsed = frame.Elapsed;
        AxisStart = frame.AxisStart;
        AxisDuration = frame.AxisDuration;
        Detail = frame.Detail;
        for (var i = 0; i < Timelines.Count; i++)
        {
            var t = Timelines[i];
            t.AxisStart = frame.AxisStart;
            t.AxisDuration = frame.AxisDuration;
            t.Line = frame.Lines[i];
        }
    }

    private static List<TimedValue> Voiced(RecordingDetail? detail)
    {
        var result = new List<TimedValue>();
        if (detail is null) return result;
        for (var i = 0; i < detail.Frames.T.Count && i < detail.Frames.Hz.Count; i++)
            if (detail.Frames.Hz[i] is { } hz) result.Add(new TimedValue(detail.Frames.T[i], hz));
        return result;
    }

    private static double Semitones(double hz) => 12 * Math.Log2(hz / AnalysisPostProcessor.SemitoneRefHz);
}
