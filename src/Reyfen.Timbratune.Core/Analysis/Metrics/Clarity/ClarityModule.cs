using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Models;
using static Reyfen.Timbratune.Core.Analysis.AnalysisPostProcessor;

namespace Reyfen.Timbratune.Core.Analysis.Metrics.Clarity;

/// <summary>
/// Clarity: the harmonics-to-noise ratio, as the mean over the take's harmonicity frames, a trend
/// column (mean HNR of the voiced frames per slice) and the HNR series (voiced frames).
/// </summary>
public sealed class ClarityModule : IMetricModule
{
    public string Name => "clarity";

    public TrackSet RequiredTracks => TrackSet.Harmonicity;

    public static readonly MetricInfo Hnr = new()
    {
        Name = "hnr", Title = "Clarity (HNR)", Unit = "dB", Zones = Domain.Zones.Hnr, Lo = 0, Hi = 30,
        Take = r => r.VoiceQuality.HnrDb,
        Columns = MetricColumns.Trend, ColumnKey = "hnr_db",
        Card = new(_ => "higher = clearer, lower = breathier · runs lower on full passages than a held vowel"),
        Compare = new(v => v.VoiceQuality.HnrDb, Gendered: false, "higher = clearer, lower = breathier · a skill cue, not a gender cue"),
        Trend = new("higher = clearer, lower = breathier", "Chart7") { MeasuredSinceVersion = 1 },
        Live = new("median over about 1 s", LiveSmoothing.Frame),
        Chip = new("HNR dB"),
    };

    private static readonly IReadOnlyList<MetricInfo> All = MetricInfo.Numbered(Hnr);

    public IReadOnlyList<MetricInfo> Metrics => All;

    public void Measure(in TrackView tracks, RawAnalysisBuilder raw)
    {
        raw.Summary["hnr"] = tracks.Harmonicity.Mean();
        raw.HnrFrames = RawAnalysisAssembler.HnrFrames(tracks.Harmonicity);
    }

    public void Summarize(MetricContext context) => context.Metrics.VoiceQuality.HnrDb = Clean(context.Raw.Get("hnr"));

    /// <summary>The voiced harmonicity frames (time, dB); null when there are none (not measured).</summary>
    public object? DetailData(RawAnalysis raw) => raw.HnrFrames is { Count: > 0 } frames ? frames : null;

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, MetricValues columns)
    {
        if (data is not IReadOnlyList<(double T, double Db)> frames) return;
        var w = window;
        var hnr = frames.Where(h => w.Contains(h.T)).Select(h => h.Db).ToList();
        columns[Hnr] = hnr.Count > 0 ? Clean(hnr.Average()) : null;
    }

    public void BuildSeries(in TrackView tracks, SeriesLines lines)
    {
        var harmonicity = tracks.Harmonicity;
        var hnr = lines[Hnr];
        hnr.Clear();
        for (var i = 0; i < harmonicity.Db.Count; i++)
            if (harmonicity.Db[i] != HarmonicityContour.Unvoiced) hnr.Add(new(harmonicity.Grid.IndexToX(i), harmonicity.Db[i]));
    }

    public void WriteSeries(in TrackView tracks, SeriesLines lines, IDictionary<string, FrameSeries> series) =>
        series[TakeSeries.Hnr] = FrameSeriesBuilder.Series(lines[Hnr], "dB", tracks.Harmonicity.Grid.Step, 2, "voiced harmonicity frames");
}
