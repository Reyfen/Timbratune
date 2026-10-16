using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Models;
using static Reyfen.Timbratune.Core.Analysis.AnalysisPostProcessor;
using WeightSection = Reyfen.Timbratune.Core.Models.Weight;

namespace Reyfen.Timbratune.Core.Analysis.Metrics.Weight;

/// <summary>
/// Vocal weight: spectral_weight() of analyze.py — the Iseli–Alwan corrected H1*–A3* (and the raw
/// H1–A3) averaged over ≤ 250 voiced frames, plus the LTAS tilt; the corrected value per phrase
/// and per trend slice; and the weight series on every measurable voiced frame.
/// </summary>
public sealed class WeightModule : IMetricModule
{
    public string Name => "weight";

    public TrackSet RequiredTracks => TrackSet.WeightFrames | TrackSet.Ltas;

    public void Measure(in TrackView tracks, RawAnalysisBuilder raw)
    {
        var derived = tracks.Derived;
        if (derived.Tracks.Has(TrackSet.WeightFrames)) raw.WeightFrames = derived.WeightFrames;
        else
        {
            var voiced = derived.Tracks.Has(TrackSet.VoicedFrames) ? derived.Voiced : RawAnalysisAssembler.VoicedFrames(tracks.Pitch);
            raw.WeightFrames = RawAnalysisAssembler.WeightFrames(voiced, tracks.WeightRow);
        }
        raw.Ltas = tracks.Ltas ?? [];
    }

    public void Summarize(MetricContext context) =>
        context.Metrics.Weight = SpectralWeight(context.Raw.WeightFrames, context.Raw.Ltas, context.Raw.Get("sampling_frequency"));

    /// <summary>The corrected H1*–A3* per weight frame (time, dB).</summary>
    public object? DetailData(RawAnalysis raw)
    {
        var fs = raw.Get("sampling_frequency");
        return raw.WeightFrames.Select(w => (w.T, CorrectedH1A3(w, fs))).Where(p => double.IsFinite(p.Item2)).ToList();
    }

    public void FillPhrase(DetailInputs inputs, object? data, Phrase phrase, PhraseMetrics metrics)
    {
        if (data is not IReadOnlyList<(double T, double H1A3c)> frames) return;
        var weight = frames.Where(w => phrase.Start <= w.T && w.T <= phrase.End).Select(w => w.H1A3c).ToList();
        metrics.WeightDb = weight.Count > 0 ? Clean(weight.Average()) : null;
    }

    public void FillTrend(DetailInputs inputs, object? data, in TrendWindow window, TrendSlice slice)
    {
        if (data is not IReadOnlyList<(double T, double H1A3c)> frames) return;
        var w = window;
        var weight = frames.Where(x => w.Contains(x.T)).Select(x => x.H1A3c).ToList();
        slice.WeightDb = weight.Count > 0 ? Clean(weight.Average()) : null;
    }

    public void BuildSeries(in TrackView t, FrameSeriesBuffers b)
    {
        // The voiced frames from the track stages when they made them (live), else derived here.
        var derived = t.Derived;
        var voiced = derived.Tracks.Has(TrackSet.VoicedFrames) ? derived.Voiced : RawAnalysisAssembler.VoicedFrames(t.Pitch, b.Voiced);
        var weightRow = t.WeightRow;
        if (b.Rows.Length < voiced.Count) b.Rows = new RawAnalysis.WeightRow?[Math.Max(voiced.Count, 2 * b.Rows.Length)];
        var rows = b.Rows;
        // Rows are memoised live, so only frames not yet measured cost anything.
        Parallel.For(0, voiced.Count, Parallelism.Options, k => rows[k] = weightRow(voiced[k].T, voiced[k].F0));
        var weight = b.Weight;
        weight.Clear();
        for (var k = 0; k < voiced.Count; k++)
        {
            if (rows[k] is not { } w) continue;
            var value = CorrectedH1A3(w, t.SamplingFrequency);
            if (double.IsFinite(value)) weight.Add(new(w.T, value));
        }
    }

    public void WriteSeries(in TrackView tracks, FrameSeriesBuffers buffers, IDictionary<string, FrameSeries> series) =>
        series[TakeSeries.Weight] = FrameSeriesBuilder.Series(buffers.Weight, "dB", tracks.Pitch.Grid.Step, 2,
            "corrected H1*-A3* on measurable voiced pitch frames");

    // ------------------------------------------------------------------
    // spectral_weight(): Iseli–Alwan corrected H1*–A3*, plus LTAS tilt
    // ------------------------------------------------------------------
    internal static WeightSection SpectralWeight(
        IReadOnlyList<RawAnalysis.WeightRow> frames,
        IReadOnlyList<(double X, double Y)> ltas,
        double fs)
    {
        var corrected = new List<double>();
        var raw = new List<double>();
        foreach (var w in frames)
        {
            var h1a3c = CorrectedH1A3(w, fs);
            var h1a3 = w.H1 - w.A3;
            if (double.IsFinite(h1a3c)) corrected.Add(h1a3c);
            if (double.IsFinite(h1a3)) raw.Add(h1a3);
        }

        double? tilt = null;
        var pts = ltas.Where(p => double.IsFinite(p.Y)).ToList();
        if (pts.Count >= 4) tilt = LinearSlope(pts) * 1000.0;

        return new WeightSection
        {
            H1a3cDb = Clean(corrected.Count > 0 ? corrected.Average() : null),
            H1a3Db = Clean(raw.Count > 0 ? raw.Average() : null),
            TiltDbKhz = Clean(tilt),
        };
    }

    /// <summary>H1* − A3* for one frame: both harmonics formant-corrected against F1–F3.</summary>
    internal static double CorrectedH1A3(RawAnalysis.WeightRow w, double fs)
    {
        var h1Corr = w.H1;
        var a3Corr = w.A3;
        foreach (var (fn, bn) in new[] { (w.F1, w.B1), (w.F2, w.B2), (w.F3, w.B3) })
        {
            h1Corr -= IseliCorrection(w.F0, fn, bn, fs);
            a3Corr -= IseliCorrection(w.K3 * w.F0, fn, bn, fs);
        }
        return h1Corr - a3Corr;
    }

    /// <summary>
    /// Iseli–Alwan (2007) vocal-tract correction for one formant, in dB — the
    /// boost a pole pair at <paramref name="fFormant"/> (bandwidth
    /// <paramref name="bwFormant"/>) adds at <paramref name="fHarmonic"/>.
    /// </summary>
    internal static double IseliCorrection(double fHarmonic, double fFormant, double bwFormant, double fs)
    {
        var r = Math.Exp(-Math.PI * bwFormant / fs);
        var omega = 2 * Math.PI * fFormant / fs;
        var omegaH = 2 * Math.PI * fHarmonic / fs;
        var num = Math.Pow(r * r - 2 * r * Math.Cos(omega) + 1, 2);
        var den = (r * r - 2 * r * Math.Cos(omega + omegaH) + 1)
                  * (r * r - 2 * r * Math.Cos(omega - omegaH) + 1);
        if (den <= 0 || num <= 0) return 0.0;
        return 10.0 * Math.Log10(num / den);
    }
}
