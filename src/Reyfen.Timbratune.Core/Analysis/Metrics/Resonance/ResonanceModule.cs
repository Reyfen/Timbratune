using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Models;
using static Reyfen.Timbratune.Core.Analysis.AnalysisPostProcessor;

namespace Reyfen.Timbratune.Core.Analysis.Metrics.Resonance;

/// <summary>
/// Resonance (F1–F3): vowel_formants() of analyze.py — the vowel-core rows (≤ 300 loud voiced
/// moments with a plausible F1), the F2 stability gate, their medians and the usable share; F2 per
/// phrase, F2/F3 per trend slice (from the kept rows); and the F1–F3 series on the gated frames.
/// The formants follow the ceiling path (Ceilings/CeilingPathFinder); with the Legacy method the
/// rows are measured at both 5500 and 5000 Hz and the ceiling with the steadier F2 wins.
/// </summary>
public sealed class ResonanceModule : IMetricModule
{
    public string Name => "resonance";

    public TrackSet RequiredTracks => TrackSet.Formants | TrackSet.FormantPath | TrackSet.LoudVowelGate | TrackSet.VowelFormantRows;

    /// <summary>F1 mostly follows the vowel (mouth openness): saved and shown in the resonance section only.</summary>
    public static readonly MetricInfo F1 = new()
    {
        Name = "f1", Title = "Resonance (F1)", Unit = "Hz",
        Take = r => r.Formants.F1Hz,
    };

    /// <summary>
    /// Perceived size: the apparent vocal-tract length from the spacing of F3 and F4 (Reby &amp; McComb 2003), the
    /// main resonance number after pitch for how listeners hear a voice (RESEARCH-ALGORITHMS §3.3). It is what a
    /// voice sounds like in size, not the speaker's body size.
    /// </summary>
    public static readonly MetricInfo Size = new()
    {
        Name = "size", Title = "Perceived size", Unit = "cm", Zones = Domain.Zones.Size, Lo = 13, Hi = 21,
        Take = r => r.Formants.VtlCm,
        Columns = MetricColumns.Trend,
        Card = new(_ => "how big the resonance sounds: the vocal-tract length your F3–F4 spacing implies · smaller reads " +
                        "lighter and more feminine · it's the size listeners *hear*, not your body's size"),
        Compare = new(v => v.Formants?.VtlCm, Gendered: true,
            "apparent vocal-tract length from F3–F4 · smaller = lighter, more feminine resonance · the second gender cue after pitch"),
        Trend = new("perceived size · lower = smaller, lighter resonance", "Chart11") { MeasuredSinceVersion = TakeTrends.CurrentVersion },
        Live = new("on loud vowels, median over about 2 s", LiveSmoothing.Vowel),
        Chip = new("size cm"),
    };

    public static readonly MetricInfo F2 = new()
    {
        Name = "f2", Title = "Resonance (F2)", Unit = "Hz", Zones = Domain.Zones.F2, Lo = 1100, Hi = 2000,
        Take = r => r.Formants.F2Hz,
        Columns = MetricColumns.Trend | MetricColumns.Phrase, ColumnKey = "f2_hz",
        Compare = new(v => v.Formants?.F2Hz, Gendered: true,
            "brightness cue · median over vowel nuclei · higher = brighter (reads feminine) · depends on what you say, so compare takes of the same text"),
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

    private static readonly IReadOnlyList<MetricInfo> All = MetricInfo.Numbered(F1, Size, F2, F3);

    /// <summary>Which formant ceiling(s) the take was measured with (the take page's resonance section).</summary>
    public static string CeilingNote(Recording r)
    {
        var f = r.Formants;
        if (f.Method != Formants.PathMethod || f.CeilingHz is not { } main) return "measured at a fixed 5500/5000 Hz formant ceiling (before v0.4.0)";
        if (f.CeilingSwitches is > 0 && f.CeilingLowestHz is { } lo && f.CeilingHighestHz is { } hi)
            return $"formant ceiling followed the voice: {lo:0}–{hi:0} Hz, {f.CeilingSwitches} switch{(f.CeilingSwitches == 1 ? "" : "es")}, mostly {main:0} Hz";
        return $"formant ceiling {main:0} Hz";
    }

    public IReadOnlyList<MetricInfo> Metrics => All;

    /// <summary>The summary keys of the ceiling path (Hz, Hz, Hz, count); absent with Legacy.</summary>
    public const string CeilingKey = "formant_ceiling", CeilingLowestKey = "formant_ceiling_lowest",
        CeilingHighestKey = "formant_ceiling_highest", CeilingSwitchesKey = "formant_ceiling_switches";

    public void Measure(in TrackView tracks, RawAnalysisBuilder raw)
    {
        raw.FormantMethod = tracks.FormantMethod;
        if (tracks.CeilingPath is { } path)
        {
            raw.Summary[CeilingKey] = tracks.FormantCeilings[path.Dominant];
            raw.Summary[CeilingLowestKey] = tracks.FormantCeilings[path.Lowest];
            raw.Summary[CeilingHighestKey] = tracks.FormantCeilings[path.Highest];
            raw.Summary[CeilingSwitchesKey] = path.Switches;
        }
        var derived = tracks.Derived;
        if (derived.Tracks.Has(TrackSet.VowelFormantRows))
        {
            (raw.Formants, raw.FormantFramesTried) = (derived.VowelFormantRows, derived.FormantFramesTried);
            return;
        }
        // Tracks made without the derived rows: select them here, as the track stage would.
        var voiced = derived.Tracks.Has(TrackSet.VoicedFrames) ? derived.Voiced : RawAnalysisAssembler.VoicedFrames(tracks.Pitch);
        var selected = RawAnalysisAssembler.SelectVowelCoreTimes(voiced, tracks.Intensity, derived.LoudOutlierFraction);
        (raw.Formants, raw.FormantFramesTried) = (RawAnalysisAssembler.VowelCoreFormants(selected, tracks), selected.Count);
    }

    public void Summarize(MetricContext context)
    {
        var raw = context.Raw;
        var formants = VowelFormants(raw.Formants, raw.FormantMethod);
        var switches = raw.Get(CeilingSwitchesKey);
        // Size from all vowel-core rows: the F2 jump gate is about F2, and leaving its rows out made the size
        // of high voices less steady (EARS high-pitch women +0.30 cm against +0.21 cm, take-to-take ICC 0.91 against 0.92).
        var deltaF = raw.FormantMethod == FormantCeilingMethod.Path ? FormantSpacing(raw.Formants) : null;
        context.Metrics.Formants = new Formants
        {
            Method = raw.FormantMethod == FormantCeilingMethod.Path ? Formants.PathMethod : null,
            F1Hz = Clean(formants.F1),
            F2Hz = Clean(formants.F2),
            F3Hz = Clean(formants.F3),
            UsablePct = UsableFormantPct(raw),
            CeilingHz = Clean(raw.Get(CeilingKey)),
            CeilingLowestHz = Clean(raw.Get(CeilingLowestKey)),
            CeilingHighestHz = Clean(raw.Get(CeilingHighestKey)),
            CeilingSwitches = double.IsFinite(switches) ? (int)switches : null,
            DeltaFHz = Clean(deltaF),
            VtlCm = deltaF is { } d ? Clean(Math.Round(SpeedOfSoundCmPerS / (2 * d), 2)) : null,
        };
    }

    /// <summary>What the phrase and trend columns come from: the kept rows, and every row's spacing (ceiling path only).</summary>
    private sealed record DetailRows(IReadOnlyList<(double T, double F2, double F3)> Kept, IReadOnlyList<(double T, double DeltaF)> Spacing);

    /// <summary>The module's own detail data, or kept rows handed in directly (<see cref="AnalysisPostProcessor.PhraseBreakdown(RecordingDetail, IReadOnlyList{ValueTuple{double, double}}?, IReadOnlyList{ValueTuple{double, double}}?)"/>).</summary>
    private static DetailRows? RowsOf(object? data) => data switch
    {
        DetailRows rows => rows,
        IReadOnlyList<(double T, double F2, double F3)> kept => new DetailRows(kept, []),
        _ => null,
    };

    public object? DetailData(RawAnalysis raw)
    {
        var kept = VowelFormants(raw.Formants, raw.FormantMethod).Kept.Select(r => (r.T, r.F2, r.F3)).ToList();
        var spacing = raw.FormantMethod == FormantCeilingMethod.Path
            ? raw.Formants.Where(r => r.F3 > 0 && r.F4 > r.F3).Select(r => (r.T, Spacing(r.F3, r.F4))).ToList()
            : [];
        return new DetailRows(kept, spacing);
    }

    public void FillPhrase(DetailInputs inputs, object? data, Phrase phrase, MetricValues columns)
    {
        if (RowsOf(data) is not { } rows) return;
        var f2 = rows.Kept.Where(f => phrase.Start <= f.T && f.T <= phrase.End).Select(f => f.F2).ToList();
        columns[F2] = f2.Count > 0 ? Clean(Median(f2)) : null;
    }

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, MetricValues columns)
    {
        if (RowsOf(data) is not { } rows) return;
        var w = window;
        var formants = rows.Kept.Where(f => w.Contains(f.T)).ToList();
        columns[F2] = formants.Count > 0 ? Clean(Median(formants.Select(f => f.F2).ToList())) : null;
        columns[F3] = formants.Count > 0 ? Clean(Median(formants.Select(f => f.F3).ToList())) : null;
        var spacing = rows.Spacing.Where(s => w.Contains(s.T)).Select(s => s.DeltaF).ToList();
        columns[Size] = spacing.Count > 0 && Median(spacing) is { } d ? Clean(Math.Round(SpeedOfSoundCmPerS / (2 * d), 2)) : null;
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
        var size = lines[Size];
        f1?.Clear();
        f2.Clear();
        f3.Clear();
        size.Clear();
        var measureSize = t.FormantMethod == FormantCeilingMethod.Path;
        var track = t.Formants;
        for (var i = 0; i < loudTimes.Count; i++)
        {
            var time = loudTimes[i];
            double v1 = track.ValueAtTime(1, time), v2 = track.ValueAtTime(2, time), v3 = track.ValueAtTime(3, time);
            if (!(v1 >= 250 && v1 <= 1000 && v2 > 0 && v3 > 0)) continue;
            f1?.Add(new(time, v1));
            f2.Add(new(time, v2));
            f3.Add(new(time, v3));
            if (measureSize && track.ValueAtTime(4, time) is var v4 && v4 > v3) size.Add(new(time, SpeedOfSoundCmPerS / (2 * Spacing(v3, v4))));
        }
    }

    public void WriteSeries(in TrackView tracks, SeriesLines lines, IDictionary<string, FrameSeries> series)
    {
        var step = tracks.Pitch.Grid.Step;
        series[TakeSeries.F1] = FrameSeriesBuilder.Series(lines[F1], "Hz", step, 1, FrameSeriesBuilder.FormantFrames);
        series[TakeSeries.F2] = FrameSeriesBuilder.Series(lines[F2], "Hz", step, 1, FrameSeriesBuilder.FormantFrames);
        series[TakeSeries.F3] = FrameSeriesBuilder.Series(lines[F3], "Hz", step, 1, FrameSeriesBuilder.FormantFrames);
        if (lines[Size].Count > 0)
            series[TakeSeries.Size] = FrameSeriesBuilder.Series(lines[Size], "cm", step, 2, "apparent vocal-tract length from F3-F4 on " + FrameSeriesBuilder.FormantFrames);
    }

    /// <summary>The speed of sound in the warm, humid vocal tract (cm/s), as in Reby &amp; McComb (2003).</summary>
    public const double SpeedOfSoundCmPerS = 35000;

    /// <summary>
    /// The formant spacing ΔF (Hz): per moment the least-squares fit of Fi = (2i − 1)/2 · ΔF through F3 and F4
    /// (Reby &amp; McComb 2003; F3–F4 depend least on the vowel, RESEARCH-ALGORITHMS §3.2), then the median;
    /// null without moments that have both.
    /// </summary>
    internal static double? FormantSpacing(IReadOnlyList<RawAnalysis.FormantRow> rows)
    {
        var spacings = new List<double>(rows.Count);
        foreach (var r in rows)
            if (r.F3 > 0 && r.F4 > r.F3) spacings.Add(Spacing(r.F3, r.F4));
        return spacings.Count > 0 ? Median(spacings) : null;
    }

    /// <summary>One moment's ΔF: F3 = 2.5 ΔF and F4 = 3.5 ΔF fitted by least squares.</summary>
    private static double Spacing(double f3, double f4) => (2.5 * f3 + 3.5 * f4) / (2.5 * 2.5 + 3.5 * 3.5);

    /// <summary>Fewer sampled moments than this say too little about how reliable the formants are.</summary>
    internal const int MinFramesForUsable = 30;

    /// <summary>Below this usable share (%) the take's formants are flagged as shaky (clean speech measured 93–98 %, noisy takes 58–61 %).</summary>
    public const double UnreliableFormantsBelowPct = 75;

    /// <summary>
    /// Share (%) of the sampled loud voiced moments with a usable F1 (Legacy: at the 5500 Hz ceiling);
    /// null if there were fewer than <see cref="MinFramesForUsable"/> (or none were counted).
    /// </summary>
    internal static double? UsableFormantPct(RawAnalysis raw)
    {
        if (raw.FormantFramesTried < MinFramesForUsable) return null;
        var usable = raw.FormantMethod == FormantCeilingMethod.Legacy
            ? raw.Formants.Count(r => r.Ceiling == RawAnalysisAssembler.FormantCeiling)
            : raw.Formants.Count;
        return Math.Round(100.0 * usable / raw.FormantFramesTried, 1);
    }

    // ------------------------------------------------------------------
    // vowel_formants(): F2 stability gate + ceiling choice + medians
    // ------------------------------------------------------------------
    /// <summary>
    /// The F2 stability gate on the rows, then the medians. Legacy: per ceiling, and the ceiling with the
    /// steadier F2 wins. Otherwise the rows are one track (the ceiling path), gated in time order.
    /// </summary>
    /// <returns>Median F1–F3 plus the vowel-core frames they came from (for per-phrase F2).</returns>
    internal static (double? F1, double? F2, double? F3, List<RawAnalysis.FormantRow> Kept) VowelFormants(
        IReadOnlyList<RawAnalysis.FormantRow> rows, FormantCeilingMethod method = FormantCeilingMethod.Legacy)
    {
        List<RawAnalysis.FormantRow> Gate(double? ceiling)
        {
            var kept = new List<RawAnalysis.FormantRow>();
            double? prevF2 = null;
            foreach (var r in rows)
            {
                if (ceiling is { } c && r.Ceiling != c) continue;
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

        if (method != FormantCeilingMethod.Legacy)
        {
            var kept = Gate(null);
            return (Median(kept.Select(r => r.F1).ToList()), Median(kept.Select(r => r.F2).ToList()),
                Median(kept.Select(r => r.F3).ToList()), kept);
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
