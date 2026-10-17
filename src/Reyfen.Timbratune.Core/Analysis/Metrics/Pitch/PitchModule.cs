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

    /// <summary>The "reads feminine" threshold of the pitch zones, trends and contour.</summary>
    public const double FemininePitchHz = 165;

    /// <summary>Longest unvoiced gap inside a phrase that the pitch contours (live and saved) join with a straight line.</summary>
    public const double ContourBridgeGap = 0.5;

    public static readonly MetricInfo Mean = new()
    {
        Name = "pitch", Title = "Pitch (avg)", Unit = "Hz", Zones = Domain.Zones.Pitch, Lo = 100, Hi = 260,
        Take = r => r.Pitch.MeanHz,
        Columns = MetricColumns.Trend | MetricColumns.Phrase, ColumnKey = "mean_hz",
        Card = new(r => Domain.Zones.ZoneOf(Domain.Zones.Pitch, r.Pitch.MeanHz) is { } zone
            ? $"you're in the **{zone.Name}** zone — 165 Hz+ reads feminine to most ears 💕" : ""),
        Compare = new(v => v.Pitch.MeanHz, Gendered: true, "where you sit vs. real men's & women's voices · 165 Hz+ reads feminine"),
        Trend = new("pink band = feminine zone (165 Hz+)", "Chart1") { Band = new(FemininePitchHz, 260, "ZoneFem") },
        Chip = new("pitch Hz"),
    };

    public static readonly MetricInfo Range = new()
    {
        Name = "pitch_range", Title = "Pitch range", Unit = "Hz",
        Take = r => r.Pitch.RangeHz,
        Card = new(r => $"{Domain.Zones.Fmt(r.Pitch.MinHz)}–{Domain.Zones.Fmt(r.Pitch.MaxHz)} Hz · wider = more melodic & expressive"),
        Chip = new("range Hz") { Text = r => $"{Domain.Zones.Fmt(r.Pitch.MinHz)}–{Domain.Zones.Fmt(r.Pitch.MaxHz)}" },
    };

    public static readonly MetricInfo Variability = new()
    {
        Name = "pitch_sd", Title = "Pitch variability", Unit = "Hz", Zones = Domain.Zones.PitchSd, Lo = 0, Hi = 60,
        Take = r => r.Pitch.SdHz,
        Columns = MetricColumns.Trend, ColumnKey = "pitch_sd_hz",
        Card = new(_ => "how much your melody moves · ~20–40 Hz is lively, natural speech"),
        Compare = new(v => v.Pitch.SdHz, Gendered: false, "how much your melody moves · ~20–40 Hz is lively, natural speech"),
        Trend = new("how much your melody moves · ~20–40 Hz is lively, natural speech", "Chart9"),
        Live = new("pitch movement over about 2 s", LiveSmoothing.Movement) { Points = i => i.VoicedPitch },
        Chip = new("variab Hz"),
    };

    private static readonly IReadOnlyList<MetricInfo> All = MetricInfo.Numbered(Mean, Range, Variability);

    public IReadOnlyList<MetricInfo> Metrics => All;

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

    public void FillPhrase(DetailInputs inputs, object? data, Phrase phrase, MetricValues columns)
    {
        var hz = inputs.WindowHz;
        columns[Mean] = hz.Count > 0 ? Round(hz.Average(), 1) : null;
    }

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, MetricValues columns)
    {
        var hz = inputs.WindowHz;
        columns[Mean] = hz.Count > 0 ? Round(hz.Average(), 1) : null;
        columns[Variability] = Clean(SampleSd(hz));
    }

    public void WriteSeries(in TrackView tracks, SeriesLines lines, IDictionary<string, FrameSeries> series)
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
