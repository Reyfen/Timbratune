using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Analysis.Metrics;

/// <summary>
/// One metric family (REFACTOR-PLAN §2.2): everything about it, from the tracks to the saved
/// take. Modules never call each other; they share data only through the tracks and
/// <see cref="RawAnalysis"/>. They hold no per-take state (one instance serves the saved and
/// the live analysis at once), so all their methods are pure functions of their inputs.
/// <list type="bullet">
/// <item><see cref="Measure"/>: tracks → its raw values (the full analysis and full live updates).</item>
/// <item><see cref="Summarize"/>: raw values → its section of the take (also the Praat reference
/// engine's path, which has raw values but no tracks).</item>
/// <item><see cref="DetailData"/>, <see cref="FillPhrase"/>, <see cref="FillTrend"/>: its columns of the
/// per-phrase breakdown and the time trends (by its own <see cref="Metrics"/>).</item>
/// <item><see cref="Metrics"/>: what it shows, and how (the UI follows the registry).</item>
/// <item><see cref="BuildSeries"/>: its per-frame series (live hot path: refills pooled lists, no allocation);
/// <see cref="WriteSeries"/>: their saved form.</item>
/// </list>
/// Every method has a default that does nothing, so a module implements only what it has.
/// </summary>
public interface IMetricModule
{
    /// <summary>A stable name (series keys, settings, analytics).</summary>
    string Name { get; }

    /// <summary>The tracks it reads.</summary>
    TrackSet RequiredTracks { get; }

    /// <summary>What it measures and shows, numbered with <see cref="MetricInfo.Numbered"/> (the registry gives them ids).</summary>
    IReadOnlyList<MetricInfo> Metrics => [];

    /// <summary>Its raw values from the tracks.</summary>
    void Measure(in TrackView tracks, RawAnalysisBuilder raw) { }

    /// <summary>Its section of <see cref="MetricContext.Metrics"/> (and, for phrasing, the detail).</summary>
    void Summarize(MetricContext context) { }

    /// <summary>What its phrase and trend columns are computed from; null = not measured (an older take rebuilt from its contour).</summary>
    object? DetailData(RawAnalysis raw) => null;

    /// <summary>Its columns of one phrase (<see cref="DetailInputs.WindowHz"/> holds the phrase's voiced pitch).</summary>
    void FillPhrase(DetailInputs inputs, object? data, Phrase phrase, MetricValues columns) { }

    /// <summary>Its columns of one trend slice (<see cref="DetailInputs.WindowHz"/> holds the slice's voiced pitch).</summary>
    void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, MetricValues columns) { }

    /// <summary>Its per-frame series into its pooled <paramref name="lines"/> (cleared and refilled).</summary>
    void BuildSeries(in TrackView tracks, SeriesLines lines) { }

    /// <summary>Its saved series, from what <see cref="BuildSeries"/> put in <paramref name="lines"/>.</summary>
    void WriteSeries(in TrackView tracks, SeriesLines lines, IDictionary<string, FrameSeries> series) { }
}

/// <summary>What <see cref="IMetricModule.Summarize"/> works on: the raw values and the take being filled.</summary>
public sealed class MetricContext(RawAnalysis raw, Recording metrics, double registerFloorHz)
{
    public RawAnalysis Raw { get; } = raw;
    public Recording Metrics { get; } = metrics;
    public double RegisterFloorHz { get; } = registerFloorHz;

    /// <summary>The contour, phrases and register summary; set by the phrasing module's <c>Summarize</c>.</summary>
    public RecordingDetail? Detail { get; set; }
}

/// <summary>
/// The shared inputs of the phrase breakdown and the trends: the detail, its voiced pitch frames,
/// each module's <see cref="IMetricModule.DetailData"/> (by registry index), and the voiced pitch
/// of the phrase or slice being filled (computed once per window and shared).
/// </summary>
public sealed class DetailInputs
{
    public required RecordingDetail Detail { get; init; }

    /// <summary>The detail's voiced pitch frames (time, Hz).</summary>
    public required List<(double T, double Hz)> Voiced { get; init; }

    /// <summary>Per module (registry order): its <see cref="IMetricModule.DetailData"/>.</summary>
    public required object?[] Data { get; init; }

    /// <summary>The voiced pitch (Hz) inside the current phrase or slice.</summary>
    public List<double> WindowHz { get; internal set; } = [];
}

/// <summary>A trend slice's span: from <see cref="Start"/>, to <see cref="End"/> (included for the last slice).</summary>
public readonly record struct TrendWindow(double Start, double End, bool IsLast)
{
    public bool Contains(double x) => Start <= x && (x < End || IsLast && x <= End);
}

/// <summary>
/// <see cref="RawAnalysis"/> being assembled: each module adds its scalars and frames. The
/// pipeline sets duration and sampling frequency.
/// </summary>
public sealed class RawAnalysisBuilder
{
    public Dictionary<string, double> Summary { get; } = new(StringComparer.Ordinal);
    public IReadOnlyList<RawAnalysis.FormantRow> Formants { get; set; } = [];
    public int FormantFramesTried { get; set; }
    public IReadOnlyList<RawAnalysis.WeightRow> WeightFrames { get; set; } = [];
    public IReadOnlyList<(double X, double Y)> Ltas { get; set; } = [];
    public IReadOnlyList<(double T, double Hz)> Contour { get; set; } = [];
    public IReadOnlyList<(double Start, double End)> Sounding { get; set; } = [];
    public IReadOnlyList<(double T, double Db)> HnrFrames { get; set; } = [];
    public IReadOnlyList<(double T, double Db)> IntensityFrames { get; set; } = [];
    public IReadOnlyList<double> Pulses { get; set; } = [];

    public RawAnalysis Build() => new()
    {
        Summary = Summary,
        Formants = Formants,
        FormantFramesTried = FormantFramesTried,
        WeightFrames = WeightFrames,
        Ltas = Ltas,
        Contour = Contour,
        Sounding = Sounding,
        HnrFrames = HnrFrames,
        IntensityFrames = IntensityFrames,
        Pulses = Pulses,
    };
}
