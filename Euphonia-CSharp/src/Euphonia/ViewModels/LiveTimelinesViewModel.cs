using Euphonia.Analysis;
using Euphonia.Core.Analysis;
using Euphonia.Core.Domain;
using Euphonia.Core.Models;

namespace Euphonia.ViewModels;

/// <summary>One live metric chart: its smoothed line, zones, fixed scale and visible time range.</summary>
public sealed record TimelineViewModel(
    string Title,
    string Caption,
    IReadOnlyList<TimedValue> Line,
    IReadOnlyList<Zone> Zones,
    double Lo,
    double Hi,
    double AxisStart,
    double AxisDuration);

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
/// </summary>
public sealed class LiveTimelinesViewModel
{
    private static readonly SmoothingSpec LoudnessSmoothing = new(WindowStatistic.EnergyMean, 0.5, 5, 0.3);
    private static readonly SmoothingSpec FrameSmoothing = new(WindowStatistic.Median, 0.5, 5, 0.4);
    private static readonly SmoothingSpec VowelSmoothing = new(WindowStatistic.Median, 1.0, 6, 0.8) { PolishRadius = 4 };
    private static readonly SmoothingSpec MovementSmoothing = new(WindowStatistic.StandardDeviation, 1.0, 10, 0.4);
    private static readonly SmoothingSpec JitterSmoothing = new(WindowStatistic.Median, 1.5, 1, 3.0) { PolishRadius = 4 };

    public LiveTimelinesViewModel(LiveSnapshot snapshot, double? window = null)
    {
        Elapsed = snapshot.Elapsed;
        if (window is { } w)
        {
            var end = Math.Max(w, snapshot.Elapsed);
            AxisStart = end - w;
            AxisDuration = w;
        }
        else
        {
            AxisStart = 0;
            AxisDuration = AxisFor(snapshot.Elapsed);
        }
        Detail = snapshot.Result?.Detail;

        var s = snapshot.Series;
        var pitch = Voiced(Detail);
        var floor = Detail?.RegisterFloorHz ?? AnalysisPostProcessor.DefaultRegisterFloorHz;
        var melody = pitch.Where(p => p.Value >= floor).Select(p => new TimedValue(p.T, Semitones(p.Value))).ToList();
        Timelines =
        [
            Make(MetricKey.Loudness, "energy average over about 1 s", s.Loudness, LoudnessSmoothing),
            Make(MetricKey.Sd, "pitch movement over about 2 s", pitch, MovementSmoothing),
            Make(MetricKey.Hnr, "median over about 1 s", s.Hnr, FrameSmoothing),
            Make(MetricKey.F2, "on loud vowels, median over about 2 s", s.F2, VowelSmoothing),
            Make(MetricKey.F3, "on loud vowels, median over about 2 s", s.F3, VowelSmoothing),
            Make(MetricKey.Weight, "median over about 2 s", s.Weight, VowelSmoothing),
            new TimelineViewModel("In-register melody", "in-register pitch movement over about 2 s (st)",
                Smooth(melody, MovementSmoothing), Zones.Melody, 0, 7, AxisStart, AxisDuration),
            Make(MetricKey.Jitter, "median of finished voiced stretches over about 3 s", s.Jitter, JitterSmoothing),
        ];
    }

    public double Elapsed { get; }
    public double AxisStart { get; }
    public double AxisDuration { get; }
    public RecordingDetail? Detail { get; }
    public IReadOnlyList<Zone> PitchZones => Zones.Pitch;
    public IReadOnlyList<TimelineViewModel> Timelines { get; }

    /// <summary>10 s, then 20, 30, … — the next multiple of 10 s at or above the elapsed time.</summary>
    public static double AxisFor(double elapsed) => Math.Max(10, 10 * Math.Ceiling(elapsed / 10 - 1e-9));

    private TimelineViewModel Make(MetricKey key, string caption, IReadOnlyList<TimedValue> points, SmoothingSpec spec)
    {
        var m = Metrics.All[key];
        return new TimelineViewModel(m.Title, caption, Smooth(points, spec), m.Zones, m.Lo, m.Hi, AxisStart, AxisDuration);
    }

    /// <summary>Only what is on screen is smoothed (plus the window reaching into it).</summary>
    private List<TimedValue> Smooth(IReadOnlyList<TimedValue> points, SmoothingSpec spec) =>
        RecentWindow.Smooth(RecentWindow.From(points, AxisStart - spec.HalfWidth - spec.MaxGap), spec, AxisStart);

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
