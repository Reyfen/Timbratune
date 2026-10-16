using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Models;
using static Reyfen.Timbratune.Core.Analysis.AnalysisPostProcessor;
using PitchSection = Reyfen.Timbratune.Core.Models.Pitch;

namespace Reyfen.Timbratune.Core.Analysis.Metrics.Pitch;

/// <summary>
/// Pitch: mean, median, minimum, maximum, range and SD of the voiced frames; the 10 ms contour
/// (0 Hz where unvoiced) that the phrasing reads; mean pitch per phrase and per trend slice, the
/// pitch SD per slice; and the pitch series (every pitch frame, null where unvoiced).
/// </summary>
public sealed class PitchModule : IMetricModule
{
    public string Name => "pitch";

    public TrackSet RequiredTracks => TrackSet.Pitch;

    public void Measure(in TrackView tracks, RawAnalysisBuilder raw)
    {
        var pitch = tracks.Pitch;
        raw.Summary["pitch_mean"] = pitch.Mean();
        raw.Summary["pitch_median"] = pitch.Quantile(0.5);
        raw.Summary["pitch_min"] = pitch.Minimum();
        raw.Summary["pitch_max"] = pitch.Maximum();
        raw.Summary["pitch_sd"] = pitch.StandardDeviation();
        raw.Contour = RawAnalysisAssembler.Contour(pitch);
    }

    public void Summarize(MetricContext context)
    {
        var raw = context.Raw;
        var minF0 = raw.Get("pitch_min");
        var maxF0 = raw.Get("pitch_max");
        context.Metrics.Pitch = new PitchSection
        {
            MeanHz = Clean(raw.Get("pitch_mean")),
            MedianHz = Clean(raw.Get("pitch_median")),
            MinHz = Clean(minF0),
            MaxHz = Clean(maxF0),
            RangeHz = double.IsNaN(minF0) || double.IsNaN(maxF0) ? null : Clean(maxF0 - minF0),
            SdHz = Clean(raw.Get("pitch_sd")),
        };
    }

    public void FillPhrase(DetailInputs inputs, object? data, Phrase phrase, PhraseMetrics metrics)
    {
        var hz = inputs.WindowHz;
        metrics.MeanHz = hz.Count > 0 ? Round(hz.Average(), 1) : null;
    }

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, TrendSlice slice)
    {
        var hz = inputs.WindowHz;
        slice.MeanHz = hz.Count > 0 ? Round(hz.Average(), 1) : null;
        slice.PitchSdHz = Clean(SampleSd(hz));
    }

    public void WriteSeries(in TrackView tracks, FrameSeriesBuffers buffers, IDictionary<string, FrameSeries> series)
    {
        var pitch = tracks.Pitch;
        var frames = new FrameSeries { Unit = "Hz", StepS = pitch.Grid.Step, Description = "every pitch frame; null = unvoiced" };
        for (var i = 0; i < pitch.FrameCount; i++)
        {
            var hz = pitch.ValueInFrame(i);
            frames.T.Add(Math.Round(pitch.Grid.IndexToX(i), 3));
            frames.Values.Add(pitch.IsVoiced(i) && double.IsFinite(hz) ? Math.Round(hz, 1) : null);
        }
        series[TakeSeries.Pitch] = frames;
    }
}
