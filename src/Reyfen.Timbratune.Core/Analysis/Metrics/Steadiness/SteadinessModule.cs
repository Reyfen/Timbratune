using Reyfen.Timbratune.Acoustics.Voice;
using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Domain;
using Reyfen.Timbratune.Core.Models;
using static Reyfen.Timbratune.Core.Analysis.AnalysisPostProcessor;

namespace Reyfen.Timbratune.Core.Analysis.Metrics.Steadiness;

/// <summary>
/// Steadiness: local jitter and shimmer of the glottal pulses (Praat's voice report settings:
/// periods 0.1–20 ms, period factor 1.3, amplitude factor 1.6), a trend column (jitter of the
/// pulses in each slice) and the jitter series (one point per finished voiced stretch).
/// </summary>
public sealed class SteadinessModule : IMetricModule
{
    public string Name => "steadiness";

    public TrackSet RequiredTracks => TrackSet.Pulses;

    public static readonly MetricInfo Jitter = new()
    {
        Name = "jitter", Title = "Steadiness (jitter)", Unit = "%", Zones = Domain.Zones.Jitter, Lo = 0, Hi = 3,
        Take = r => r.VoiceQuality.JitterPct,
        Columns = MetricColumns.Trend, ColumnKey = "jitter_pct",
        Card = new(r => $"lower = steadier · shimmer {Domain.Zones.Fmt(r.VoiceQuality.ShimmerPct)}% (under ~3.8% is steady)"),
        Compare = new(v => v.VoiceQuality.JitterPct, Gendered: false, "lower = steadier · a skill cue, not a gender cue"),
        Trend = new("lower = steadier", "Chart8") { MeasuredSinceVersion = 1 },
        Live = new("median of finished voiced stretches over about 3 s",
            new SmoothingSpec(WindowStatistic.Median, 1.5, 1, 3.0) { PolishRadius = 4 }),
    };

    private static readonly IReadOnlyList<MetricInfo> All = MetricInfo.Numbered(Jitter);

    public IReadOnlyList<MetricInfo> Metrics => All;

    public void Measure(in TrackView tracks, RawAnalysisBuilder raw)
    {
        raw.Summary["jitter_local"] = VoiceReport.JitterLocal(tracks.Pulses, 0.0001, 0.02, 1.3);
        raw.Summary["shimmer_local"] = VoiceReport.ShimmerLocal(tracks.Pulses, tracks.Sound, 0.0001, 0.02, 1.3, 1.6);
        raw.Pulses = tracks.Pulses;
    }

    public void Summarize(MetricContext context)
    {
        var quality = context.Metrics.VoiceQuality;
        quality.JitterPct = Clean(context.Raw.Get("jitter_local") * 100);
        quality.ShimmerPct = Clean(context.Raw.Get("shimmer_local") * 100);
    }

    /// <summary>The glottal pulse times; null when there are none (not measured).</summary>
    public object? DetailData(RawAnalysis raw) => raw.Pulses is { Count: > 0 } pulses ? pulses : null;

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, MetricValues columns)
    {
        var w = window;
        var jitter = data is IReadOnlyList<double> pulses ? VoiceReport.JitterLocal(pulses.Where(w.Contains).ToList(), 0.0001, 0.02, 1.3) : double.NaN;
        columns[Jitter] = Clean(jitter * 100);
    }

    public void BuildSeries(in TrackView tracks, SeriesLines lines)
    {
        var jitter = lines[Jitter];
        jitter.Clear();
        // Live, the pulse tracker measured each finished stretch; saved, they're cut from all pulses.
        foreach (var s in tracks.StretchJitter ?? FrameSeriesBuilder.StretchJitter(tracks.Pitch, tracks.Pulses))
            if (double.IsFinite(s.Jitter)) jitter.Add(new TimedValue(s.End, s.Jitter * 100));
    }

    public void WriteSeries(in TrackView tracks, SeriesLines lines, IDictionary<string, FrameSeries> series) =>
        series[TakeSeries.Jitter] = FrameSeriesBuilder.Series(lines[Jitter], "%", null, 3, "local jitter per voiced stretch, at the stretch end");
}
