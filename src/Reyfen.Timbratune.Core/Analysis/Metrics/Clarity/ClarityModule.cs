using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Core.Analysis.Tracks;
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

    public void Measure(in TrackView tracks, RawAnalysisBuilder raw)
    {
        raw.Summary["hnr"] = tracks.Harmonicity.Mean();
        raw.HnrFrames = RawAnalysisAssembler.HnrFrames(tracks.Harmonicity);
    }

    public void Summarize(MetricContext context) => context.Metrics.VoiceQuality.HnrDb = Clean(context.Raw.Get("hnr"));

    /// <summary>The voiced harmonicity frames (time, dB); null when there are none (not measured).</summary>
    public object? DetailData(RawAnalysis raw) => raw.HnrFrames is { Count: > 0 } frames ? frames : null;

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, TrendSlice slice)
    {
        if (data is not IReadOnlyList<(double T, double Db)> frames) return;
        var w = window;
        var hnr = frames.Where(h => w.Contains(h.T)).Select(h => h.Db).ToList();
        slice.HnrDb = hnr.Count > 0 ? Clean(hnr.Average()) : null;
    }

    public void BuildSeries(in TrackView tracks, FrameSeriesBuffers buffers)
    {
        var harmonicity = tracks.Harmonicity;
        var hnr = buffers.Hnr;
        hnr.Clear();
        for (var i = 0; i < harmonicity.Db.Count; i++)
            if (harmonicity.Db[i] != HarmonicityContour.Unvoiced) hnr.Add(new(harmonicity.Grid.IndexToX(i), harmonicity.Db[i]));
    }

    public void WriteSeries(in TrackView tracks, FrameSeriesBuffers buffers, IDictionary<string, FrameSeries> series) =>
        series[TakeSeries.Hnr] = FrameSeriesBuilder.Series(buffers.Hnr, "dB", tracks.Harmonicity.Grid.Step, 2, "voiced harmonicity frames");
}
