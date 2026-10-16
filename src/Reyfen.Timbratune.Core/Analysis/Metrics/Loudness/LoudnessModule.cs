using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Models;
using static Reyfen.Timbratune.Core.Analysis.AnalysisPostProcessor;

namespace Reyfen.Timbratune.Core.Analysis.Metrics.Loudness;

/// <summary>
/// Loudness: the intensity contour's mean (energy average), minimum and maximum, a trend column
/// (the energy average per slice), and the loudness series (every intensity frame).
/// </summary>
public sealed class LoudnessModule : IMetricModule
{
    public string Name => "loudness";

    public TrackSet RequiredTracks => TrackSet.Intensity;

    public void Measure(in TrackView tracks, RawAnalysisBuilder raw)
    {
        var intensity = tracks.Intensity;
        raw.Summary["intensity_mean"] = intensity.MeanEnergyDb();
        raw.Summary["intensity_min"] = intensity.Minimum();
        raw.Summary["intensity_max"] = intensity.Maximum();
        raw.IntensityFrames = Enumerable.Range(0, intensity.Db.Count).Select(i => (intensity.Grid.IndexToX(i), intensity.Db[i])).ToList();
    }

    public void Summarize(MetricContext context)
    {
        var raw = context.Raw;
        context.Metrics.Intensity = new Intensity
        {
            MeanDb = Clean(raw.Get("intensity_mean")),
            MinDb = Clean(raw.Get("intensity_min")),
            MaxDb = Clean(raw.Get("intensity_max")),
        };
    }

    /// <summary>The intensity frames (time, dB); null when there are none (not measured).</summary>
    public object? DetailData(RawAnalysis raw) => raw.IntensityFrames is { Count: > 0 } frames ? frames : null;

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, TrendSlice slice)
    {
        if (data is not IReadOnlyList<(double T, double Db)> frames) return;
        var w = window;
        var intensity = frames.Where(f => w.Contains(f.T)).Select(f => f.Db).ToList();
        slice.LoudnessDb = intensity.Count > 0 ? Clean(10 * Math.Log10(intensity.Average(db => Math.Pow(10, 0.1 * db)))) : null;
    }

    public void BuildSeries(in TrackView tracks, FrameSeriesBuffers buffers)
    {
        var loudness = buffers.Loudness;
        loudness.Clear();
        var intensity = tracks.Intensity;
        for (var i = 0; i < intensity.Db.Count; i++) loudness.Add(new(intensity.Grid.IndexToX(i), intensity.Db[i]));
    }

    public void WriteSeries(in TrackView tracks, FrameSeriesBuffers buffers, IDictionary<string, FrameSeries> series) =>
        series[TakeSeries.Loudness] = FrameSeriesBuilder.Series(buffers.Loudness, "dB", tracks.Intensity.Grid.Step, 2, "every intensity frame");
}
