using Reyfen.Timbratune.Analysis;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.ViewModels;

/// <summary>
/// One live metric chart: fixed title, zones and scale, plus the smoothed line, its current
/// value and the visible time range, which update in place so the chart (and its easing)
/// survives each refresh.
/// </summary>
public sealed partial class TimelineViewModel(string title, string caption, string unit, IReadOnlyList<Zone> zones, double lo, double hi,
    bool holdValue = false)
    : ObservableObject
{
    public string Title { get; } = title;
    public string Caption { get; } = caption;
    public string Unit { get; } = unit;
    /// <summary>Keep showing the last value however old it is (loudness: always measured, silence included).</summary>
    public bool HoldValue { get; } = holdValue;
    public IReadOnlyList<Zone> Zones { get; } = zones;
    public double Lo { get; } = lo;
    public double Hi { get; } = hi;

    [ObservableProperty] private IReadOnlyList<TimedValue> _line = [];
    /// <summary>The current point's value (the dot), e.g. "182 Hz"; empty before there is one, or once it is stale.</summary>
    [ObservableProperty] private string _valueText = "";
    /// <summary>The zone the current value sits in (its colour), or null when there is no value.</summary>
    [ObservableProperty] private ZoneColorKey? _valueZone;
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
    public static readonly SmoothingSpec LoudnessSmoothing = new(WindowStatistic.EnergyMean, 0.5, 5, 0.3)
        { TrendDamping = 0, MinimumTrust = 1 };
    public static readonly SmoothingSpec FrameSmoothing = new(WindowStatistic.Median, 0.5, 5, 0.4);
    public static readonly SmoothingSpec VowelSmoothing = new(WindowStatistic.Median, 1.0, 6, 0.8) { PolishRadius = 4 };
    public static readonly SmoothingSpec MovementSmoothing = new(WindowStatistic.StandardDeviation, 1.0, 10, 0.4);
    // Pitch keeps its intonation: a short median (about 0.2 s) removes single-frame octave
    // slips and softens path revisions; voiced bits from 30 ms are kept. The median never
    // reaches across an unvoiced gap over 60 ms (it would flip from one side's pitch to the
    // other's mid-gap), but within a phrase gaps up to 0.5 s are drawn as a straight line:
    // the tracker loses the voice on consonants and in fast pitch jumps (e.g. 0.25–0.3 s in
    // the middle of an upspeak rise), which used to split one phrase into pieces. Jumps no
    // voice makes in 20 ms still break the line (octave slips flip the median between
    // octaves). Like loudness, intonation turns too quickly for a trend: measured, holding
    // the newest value moved the dot less and stayed closer to the saved contour than predicting.
    public static readonly SmoothingSpec PitchSmoothing = new(WindowStatistic.Median, 0.1, 3, 0.06, Step: 0.02)
        { PolishRadius = 1, TrendDamping = 0, MaxStepRatio = Math.Pow(2, 3.0 / 12), BridgeGap = ContourBridgeGap }; // > 3 st in 20 ms = a slip

    /// <summary>Longest unvoiced gap inside a phrase that the pitch contours (live and saved) join with a straight line.</summary>
    public const double ContourBridgeGap = 0.5;

    public static readonly SmoothingSpec JitterSmoothing = new(WindowStatistic.Median, 1.5, 1, 3.0) { PolishRadius = 4 };

    /// <summary>What one refresh shows: the visible range, the pitch detail and every line (in <see cref="Timelines"/> order).</summary>
    public sealed record Frame(double Elapsed, double AxisStart, double AxisDuration, RecordingDetail? Detail,
        IReadOnlyList<TimedValue> PitchLine, IReadOnlyList<TimedValue>[] Lines);

    public LiveTimelinesViewModel()
    {
        TimelineViewModel Metric(MetricKey key, string caption, bool hold = false)
        {
            var m = Metrics.All[key];
            return new TimelineViewModel(m.Title, caption, m.Unit, m.Zones, m.Lo, m.Hi, hold);
        }
        Timelines =
        [
            Metric(MetricKey.Loudness, "energy average over about 1 s", hold: true),
            Metric(MetricKey.Sd, "pitch movement over about 2 s"),
            Metric(MetricKey.Hnr, "median over about 1 s"),
            Metric(MetricKey.F2, "on loud vowels, median over about 2 s"),
            Metric(MetricKey.F3, "on loud vowels, median over about 2 s"),
            Metric(MetricKey.Weight, "median over about 2 s"),
            new TimelineViewModel("In-register melody", "in-register pitch movement over about 2 s", "st", Zones.Melody, 0, 7),
            Metric(MetricKey.Jitter, "median of finished voiced stretches over about 3 s"),
        ];
    }

    [ObservableProperty] private double _elapsed;
    [ObservableProperty] private double _axisStart;
    [ObservableProperty] private double _axisDuration = 10;
    [ObservableProperty] private RecordingDetail? _detail;
    [ObservableProperty] private IReadOnlyList<TimedValue> _pitchLine = [];
    /// <summary>The live pitch now (the contour's dot), e.g. "182 Hz"; empty when there is none or it is stale.</summary>
    [ObservableProperty] private string _pitchText = "";
    [ObservableProperty] private ZoneColorKey? _pitchZone;

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
        var phraseEnds = detail?.Phrases.Select(p => p.End).ToList();
        var pitchLine = RecentWindow.Smooth(RecentWindow.From(pitch, start - PitchSmoothing.HalfWidth - ContourBridgeGap),
            PitchSmoothing, start, now, phraseEnds);
        return new Frame(now, start, duration, detail, pitchLine,
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
        PitchLine = frame.PitchLine;
        (PitchText, PitchZone) = CurrentValue(frame.PitchLine, "Hz", Zones.Pitch, frame.Elapsed, hold: false);
        for (var i = 0; i < Timelines.Count; i++)
        {
            var t = Timelines[i];
            t.AxisStart = frame.AxisStart;
            t.AxisDuration = frame.AxisDuration;
            t.Line = frame.Lines[i];
            (t.ValueText, t.ValueZone) = CurrentValue(frame.Lines[i], t.Unit, t.Zones, frame.Elapsed, t.HoldValue);
        }
    }

    /// <summary>A live value disappears this long after its metric was last measured (e.g. you stopped talking).</summary>
    public const double StaleAfterSeconds = 5;

    /// <summary>At or below this a dB value is the analysis' "no signal" marker, not a level.</summary>
    private const double NoSignalDb = -100;

    /// <summary>
    /// The line's newest value — where the chart puts its dot — with as many decimals as
    /// the unit needs to move visibly ("182 Hz", "64.3 dB", "2.4 st", "0.81 %"), and the
    /// zone it sits in (beyond the outer zones counts as those, as the chart's bands do).
    /// Empty before the first value, and <see cref="StaleAfterSeconds"/> after the last one
    /// unless <paramref name="hold"/>.
    /// </summary>
    public static (string Text, ZoneColorKey? Zone) CurrentValue(IReadOnlyList<TimedValue> line, string unit,
        IReadOnlyList<Zone> zones, double now, bool hold)
    {
        for (var i = line.Count - 1; i >= 0; i--)
        {
            var (t, v) = (line[i].T, line[i].Value);
            if (double.IsNaN(v)) continue;
            if (!hold && now - t > StaleAfterSeconds) break;
            // The analysis writes −300 dB for a frame with no signal at all (digital silence): nothing to show.
            if (unit == "dB" && v <= NoSignalDb) break;
            var format = unit switch { "Hz" => "0", "%" => "0.00", _ => "0.0" };
            return ($"{v.ToString(format, System.Globalization.CultureInfo.InvariantCulture)} {unit}", ZoneAt(zones, v));
        }
        return ("", null);
    }

    /// <summary>The zone holding <paramref name="v"/>; below the first zone counts as the first, above the last as the last.</summary>
    private static ZoneColorKey? ZoneAt(IReadOnlyList<Zone> zones, double v)
    {
        if (zones.Count == 0) return null;
        if (v < zones[0].From) return zones[0].Color;
        foreach (var z in zones)
            if (v < z.To) return z.Color;
        return zones[^1].Color;
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
