using Reyfen.Timbratune.Acoustics.Voice;
using Reyfen.Timbratune.Core.Analysis.Tracks;
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

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, TrendSlice slice)
    {
        var w = window;
        var jitter = data is IReadOnlyList<double> pulses ? VoiceReport.JitterLocal(pulses.Where(w.Contains).ToList(), 0.0001, 0.02, 1.3) : double.NaN;
        slice.JitterPct = Clean(jitter * 100);
    }

    public void BuildSeries(in TrackView tracks, FrameSeriesBuffers buffers)
    {
        var jitter = buffers.Jitter;
        jitter.Clear();
        // Live, the pulse tracker measured each finished stretch; saved, they're cut from all pulses.
        foreach (var s in tracks.StretchJitter ?? FrameSeriesBuilder.StretchJitter(tracks.Pitch, tracks.Pulses))
            if (double.IsFinite(s.Jitter)) jitter.Add(new TimedValue(s.End, s.Jitter * 100));
    }

    public void WriteSeries(in TrackView tracks, FrameSeriesBuffers buffers, IDictionary<string, FrameSeries> series) =>
        series[TakeSeries.Jitter] = FrameSeriesBuilder.Series(buffers.Jitter, "%", null, 3, "local jitter per voiced stretch, at the stretch end");
}
