using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Models;
using static Reyfen.Timbratune.Core.Analysis.AnalysisPostProcessor;

namespace Reyfen.Timbratune.Core.Analysis.Metrics.Resonance;

/// <summary>
/// Resonance (F1–F3): vowel_formants() of analyze.py — the vowel-core rows (≤ 300 loud voiced
/// moments with a plausible F1, at both ceilings), the F2 stability gate and the ceiling with the
/// steadier F2, their medians, and the usable share at 5500 Hz; F2 per phrase, F2/F3 per trend
/// slice (from the kept rows); and the F1–F3 series on the gated frames (5500 Hz ceiling).
/// </summary>
public sealed class ResonanceModule : IMetricModule
{
    public string Name => "resonance";

    public TrackSet RequiredTracks => TrackSet.Formants | TrackSet.LoudVowelGate | TrackSet.VowelFormantRows;

    /// <summary>F1 mostly follows the vowel (mouth openness): saved and shown in the resonance section only.</summary>
    public static readonly MetricInfo F1 = new()
    {
        Name = "f1", Title = "Resonance (F1)", Unit = "Hz",
        Take = r => r.Formants.F1Hz,
    };

    public static readonly MetricInfo F2 = new()
    {
        Name = "f2", Title = "Resonance (F2)", Unit = "Hz", Zones = Domain.Zones.F2, Lo = 1100, Hi = 2000,
        Take = r => r.Formants.F2Hz,
        Columns = MetricColumns.Trend | MetricColumns.Phrase, ColumnKey = "f2_hz",
        Compare = new(v => v.Formants?.F2Hz, Gendered: true,
            "main brightness cue · median over vowel nuclei · higher = smaller, brighter tract (reads feminine)"),
        Trend = new("brightness / vocal-tract size cue", "Chart4") { MeasuredSinceVersion = 1 },
        Live = new("on loud vowels, median over about 2 s", LiveSmoothing.Vowel),
        Chip = new("F2 Hz"),
    };

    public static readonly MetricInfo F3 = new()
    {
        Name = "f3", Title = "Resonance (F3)", Unit = "Hz", Zones = Domain.Zones.F3, Lo = 2100, Hi = 3400,
        Take = r => r.Formants.F3Hz,
        Columns = MetricColumns.Trend, ColumnKey = "f3_hz",
        Compare = new(v => v.Formants?.F3Hz, Gendered: true, "supports brightness · higher = brighter / lighter resonance"),
        Trend = new("supports brightness", "Chart6") { MeasuredSinceVersion = 1 },
        Live = new("on loud vowels, median over about 2 s", LiveSmoothing.Vowel),
    };

    private static readonly IReadOnlyList<MetricInfo> All = MetricInfo.Numbered(F1, F2, F3);

    public IReadOnlyList<MetricInfo> Metrics => All;

    public void Measure(in TrackView tracks, RawAnalysisBuilder raw)
    {
        var derived = tracks.Derived;
        if (derived.Tracks.Has(TrackSet.VowelFormantRows))
        {
            (raw.Formants, raw.FormantFramesTried) = (derived.VowelFormantRows, derived.FormantFramesTried);
            return;
        }
        // Tracks made without the derived rows: select them here, as the track stage would.
        var voiced = derived.Tracks.Has(TrackSet.VoicedFrames) ? derived.Voiced : RawAnalysisAssembler.VoicedFrames(tracks.Pitch);
        var selected = RawAnalysisAssembler.SelectVowelCoreTimes(voiced, tracks.Intensity, derived.LoudOutlierFraction);
        (raw.Formants, raw.FormantFramesTried) = (RawAnalysisAssembler.VowelCoreFormants(selected,
            [(RawAnalysisAssembler.FormantCeiling, tracks.Formants5500), (5000, tracks.Formants5000)]), selected.Count);
    }

    public void Summarize(MetricContext context)
    {
        var formants = VowelFormants(context.Raw.Formants);
        context.Metrics.Formants = new Formants
        {
            F1Hz = Clean(formants.F1),
            F2Hz = Clean(formants.F2),
            F3Hz = Clean(formants.F3),
            UsablePct = UsableFormantPct(context.Raw),
        };
    }

    /// <summary>The kept vowel-core rows as (time, F2, F3).</summary>
    public object? DetailData(RawAnalysis raw) => VowelFormants(raw.Formants).Kept.Select(r => (r.T, r.F2, r.F3)).ToList();

    public void FillPhrase(DetailInputs inputs, object? data, Phrase phrase, MetricValues columns)
    {
        if (data is not IReadOnlyList<(double T, double F2, double F3)> rows) return;
        var f2 = rows.Where(f => phrase.Start <= f.T && f.T <= phrase.End).Select(f => f.F2).ToList();
        columns[F2] = f2.Count > 0 ? Clean(Median(f2)) : null;
    }

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, MetricValues columns)
    {
        if (data is not IReadOnlyList<(double T, double F2, double F3)> rows) return;
        var w = window;
        var formants = rows.Where(f => w.Contains(f.T)).ToList();
        columns[F2] = formants.Count > 0 ? Clean(Median(formants.Select(f => f.F2).ToList())) : null;
        columns[F3] = formants.Count > 0 ? Clean(Median(formants.Select(f => f.F3).ToList())) : null;
    }

    public void BuildSeries(in TrackView t, SeriesLines lines)
    {
        var b = lines.Buffers;
        // The gate from the track stages when they made it (live), else derived here.
        var derived = t.Derived;
        IReadOnlyList<double> loudTimes;
        if (derived.Tracks.Has(TrackSet.LoudVowelGate)) loudTimes = derived.LoudTimes;
        else
        {
            var voiced = derived.Tracks.Has(TrackSet.VoicedFrames) ? derived.Voiced : RawAnalysisAssembler.VoicedFrames(t.Pitch, b.Voiced);
            loudTimes = RawAnalysisAssembler.LoudVoicedTimes(voiced, t.Intensity, b.LoudTimes, derived.LoudOutlierFraction, b.LoudScratch);
        }
        var f1 = b.ForSavedTake ? lines[F1] : null;
        var f2 = lines[F2];
        var f3 = lines[F3];
        f1?.Clear();
        f2.Clear();
        f3.Clear();
        var track = t.Formants5500;
        for (var i = 0; i < loudTimes.Count; i++)
        {
            var time = loudTimes[i];
            double v1 = track.ValueAtTime(1, time), v2 = track.ValueAtTime(2, time), v3 = track.ValueAtTime(3, time);
            if (!(v1 >= 250 && v1 <= 1000 && v2 > 0 && v3 > 0)) continue;
            f1?.Add(new(time, v1));
            f2.Add(new(time, v2));
            f3.Add(new(time, v3));
        }
    }

    public void WriteSeries(in TrackView tracks, SeriesLines lines, IDictionary<string, FrameSeries> series)
    {
        var step = tracks.Pitch.Grid.Step;
        series[TakeSeries.F1] = FrameSeriesBuilder.Series(lines[F1], "Hz", step, 1, FrameSeriesBuilder.FormantFrames);
        series[TakeSeries.F2] = FrameSeriesBuilder.Series(lines[F2], "Hz", step, 1, FrameSeriesBuilder.FormantFrames);
        series[TakeSeries.F3] = FrameSeriesBuilder.Series(lines[F3], "Hz", step, 1, FrameSeriesBuilder.FormantFrames);
    }

    /// <summary>Fewer sampled moments than this say too little about how reliable the formants are.</summary>
    internal const int MinFramesForUsable = 30;

    /// <summary>Below this usable share (%) the take's formants are flagged as shaky (clean speech measured 93–98 %, noisy takes 58–61 %).</summary>
    public const double UnreliableFormantsBelowPct = 75;

    /// <summary>
    /// Share (%) of the sampled loud voiced moments with a usable F1 at the 5500 Hz ceiling;
    /// null if there were fewer than <see cref="MinFramesForUsable"/> (or none were counted).
    /// </summary>
    internal static double? UsableFormantPct(RawAnalysis raw)
    {
        if (raw.FormantFramesTried < MinFramesForUsable) return null;
        var usable = raw.Formants.Count(r => r.Ceiling == RawAnalysisAssembler.FormantCeiling);
        return Math.Round(100.0 * usable / raw.FormantFramesTried, 1);
    }

    // ------------------------------------------------------------------
    // vowel_formants(): F2 stability gate + ceiling choice + medians
    // ------------------------------------------------------------------
    /// <returns>Median F1–F3 plus the vowel-core frames they came from (for per-phrase F2).</returns>
    internal static (double? F1, double? F2, double? F3, List<RawAnalysis.FormantRow> Kept) VowelFormants(
        IReadOnlyList<RawAnalysis.FormantRow> rows)
    {
        List<RawAnalysis.FormantRow> Gate(double ceiling)
        {
            var kept = new List<RawAnalysis.FormantRow>();
            double? prevF2 = null;
            foreach (var r in rows)
            {
                if (r.Ceiling != ceiling) continue;
                // Drop frames where F2 jumps > 150 Hz vs the previous frame;
                // prev_f2 advances even for dropped frames (as in analyze.py).
                if (prevF2 is { } prev && Math.Abs(r.F2 - prev) > 150.0)
                {
                    prevF2 = r.F2;
                    continue;
                }
                kept.Add(r);
                prevF2 = r.F2;
            }
            return kept;
        }

        List<RawAnalysis.FormantRow>? best = null;
        var bestSpread = double.PositiveInfinity;
        foreach (var ceiling in new[] { RawAnalysisAssembler.FormantCeiling, 5000.0 })
        {
            var fv = Gate(ceiling);
            if (fv.Count < 5) continue;
            var spread = PopulationSd(fv.Select(r => r.F2).ToList());
            if (spread < bestSpread)
            {
                bestSpread = spread;
                best = fv;
            }
        }
        var chosen = best ?? Gate(RawAnalysisAssembler.FormantCeiling);
        return (Median(chosen.Select(r => r.F1).ToList()), Median(chosen.Select(r => r.F2).ToList()),
            Median(chosen.Select(r => r.F3).ToList()), chosen);
    }
}
