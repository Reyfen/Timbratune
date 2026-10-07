using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Spectral;

namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// Frame-level measurements of a take (whole or so far). Everything the
/// <see cref="RawAnalysisAssembler"/> needs, whether it came from the full
/// analysis or from the live trackers.
/// </summary>
public sealed record FrameTracks
{
    public required double Duration { get; init; }
    public required double SamplingFrequency { get; init; }
    public required PitchContour Pitch { get; init; }
    public required IntensityContour Intensity { get; init; }
    public required double HnrMean { get; init; }
    public required double Jitter { get; init; }
    public required double Shimmer { get; init; }
    public required IFormantTrack Formants5500 { get; init; }
    public required IFormantTrack Formants5000 { get; init; }
    /// <summary>
    /// Weight row for a voiced frame (time, F0), or null when it can't be measured.
    /// Must be thread-safe: rows are computed in parallel.
    /// </summary>
    public required Func<double, double, RawAnalysis.WeightRow?> WeightRow { get; init; }
    public required IReadOnlyList<(double X, double Y)> Ltas { get; init; }
    public required IReadOnlyList<(double Start, double End)> Sounding { get; init; }
    /// <summary>The harmonicity track, for the per-slice HNR of the trends (optional).</summary>
    public HarmonicityContour? Harmonicity { get; init; }
    /// <summary>Glottal pulse times, for the per-slice jitter of the trends (optional).</summary>
    public IReadOnlyList<double> Pulses { get; init; } = [];
}

/// <summary>
/// The selection steps of analyze.py on top of the frame tracks: voiced frames,
/// the loud vowel-core gate, subsampling, the F1 gate, weight frames, the contour
/// and the scalar summary. Shared by the full and the live analysis.
/// </summary>
public static class RawAnalysisAssembler
{
    public const double FormantCeiling = 5500;

    public static RawAnalysis Assemble(FrameTracks t)
    {
        var pitch = t.Pitch;
        var intensity = t.Intensity;
        var voiced = VoicedFrames(pitch);

        var summary = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["duration"] = t.Duration,
            ["sampling_frequency"] = t.SamplingFrequency,
            ["pitch_mean"] = pitch.Mean(),
            ["pitch_median"] = pitch.Quantile(0.5),
            ["pitch_min"] = pitch.Minimum(),
            ["pitch_max"] = pitch.Maximum(),
            ["pitch_sd"] = pitch.StandardDeviation(),
            ["hnr"] = t.HnrMean,
            ["jitter_local"] = t.Jitter,
            ["shimmer_local"] = t.Shimmer,
            ["intensity_mean"] = intensity.MeanEnergyDb(),
            ["intensity_min"] = intensity.Minimum(),
            ["intensity_max"] = intensity.Maximum(),
        };

        // analyze_register() uses a 10 ms step; the pitch track has exactly that step.
        var contour = new List<(double, double)>(pitch.FrameCount);
        for (var i = 0; i < pitch.FrameCount; i++)
        {
            var hz = pitch.ValueInFrame(i);
            contour.Add((pitch.Grid.IndexToX(i), double.IsNaN(hz) ? 0 : hz));
        }

        return new RawAnalysis
        {
            Summary = summary,
            Formants = VowelCoreFormants(voiced, intensity, [(FormantCeiling, t.Formants5500), (5000, t.Formants5000)]),
            WeightFrames = WeightFrames(voiced, t.WeightRow),
            Ltas = t.Ltas,
            Contour = contour,
            Sounding = t.Sounding,
            HnrFrames = HnrFrames(t.Harmonicity),
            IntensityFrames = Enumerable.Range(0, intensity.Db.Count).Select(i => (intensity.Grid.IndexToX(i), intensity.Db[i])).ToList(),
            Pulses = t.Pulses,
        };
    }

    /// <summary>(time, dB) of the voiced harmonicity frames.</summary>
    public static List<(double T, double Db)> HnrFrames(HarmonicityContour? harmonicity)
    {
        var frames = new List<(double, double)>();
        if (harmonicity is null) return frames;
        for (var i = 0; i < harmonicity.Db.Count; i++)
            if (harmonicity.Db[i] != HarmonicityContour.Unvoiced) frames.Add((harmonicity.Grid.IndexToX(i), harmonicity.Db[i]));
        return frames;
    }

    /// <summary>Sounding stretches of speech (silence detection on the intensity contour).</summary>
    public static IReadOnlyList<(double Start, double End)> SoundingIntervals(IntensityContour intensity) =>
        SilenceDetector.Detect(intensity, -25, 0.1, 0.05).Where(i => i.IsSounding).Select(i => (i.Start, i.End)).ToList();

    /// <summary>LTAS samples at 100, 200, … 5000 Hz (for the spectral tilt).</summary>
    public static IReadOnlyList<(double X, double Y)> LtasPoints(Ltas ltas)
    {
        var points = new List<(double, double)>();
        for (var f = 100; f <= 5000; f += 100)
        {
            var v = ltas.ValueAtFrequency(f);
            if (!double.IsNaN(v)) points.Add((f, v));
        }
        return points;
    }

    public static List<(double T, double F0)> VoicedFrames(PitchContour pitch)
    {
        var voiced = new List<(double, double)>();
        for (var i = 0; i < pitch.FrameCount; i++)
            if (pitch.IsVoiced(i)) voiced.Add((pitch.Grid.IndexToX(i), pitch.ValueInFrame(i)));
        return voiced;
    }

    /// <summary>
    /// vowel_formants(): voiced frames within 10 dB of the loudest intensity frame
    /// (intensity read at the first frame at or after each time), subsampled to ≤ 300,
    /// with plausible F1 (250–1000 Hz) — measured at each formant ceiling.
    /// </summary>
    public static List<RawAnalysis.FormantRow> VowelCoreFormants(List<(double T, double F0)> voiced, IntensityContour intensity,
        (double Ceiling, IFormantTrack Track)[] ceilings)
    {
        var selected = Subsample(LoudVoicedTimes(voiced, intensity), 300);
        var rows = new List<RawAnalysis.FormantRow>();
        foreach (var (ceiling, track) in ceilings)
        {
            foreach (var t in selected)
            {
                double f1 = track.ValueAtTime(1, t), f2 = track.ValueAtTime(2, t), f3 = track.ValueAtTime(3, t);
                if (!(f1 > 0 && f2 > 0 && f3 > 0)) continue; // also rejects NaN
                if (f1 < 250 || f1 > 1000) continue;
                rows.Add(new RawAnalysis.FormantRow(ceiling, t, f1, f2, f3));
            }
        }
        return rows;
    }

    /// <summary>Times of voiced frames within 10 dB of the loudest intensity frame.</summary>
    public static List<double> LoudVoicedTimes(List<(double T, double F0)> voiced, IntensityContour intensity)
    {
        var db = intensity.Db;
        var loudFloor = db.Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max() - 10;
        var candidates = new List<double>();
        var p = 0;
        foreach (var (t, _) in voiced)
        {
            while (p < db.Count - 1 && intensity.Grid.IndexToX(p) < t) p++;
            if (db.Count > 0 && db[p] >= loudFloor) candidates.Add(t);
        }
        return candidates;
    }

    /// <summary>spectral_weight(): weight rows for ≤ 250 voiced frames.</summary>
    public static List<RawAnalysis.WeightRow> WeightFrames(List<(double T, double F0)> voiced, Func<double, double, RawAnalysis.WeightRow?> weightRow)
    {
        var frames = Subsample(voiced, 250);
        var rows = new RawAnalysis.WeightRow?[frames.Count];
        Parallel.For(0, frames.Count, k => rows[k] = weightRow(frames[k].T, frames[k].F0));
        return rows.Where(r => r.HasValue).Select(r => r!.Value).ToList();
    }

    /// <summary>
    /// One weight row: a Hamming-windowed narrowband spectrum (window max(25 ms, 3 periods))
    /// gives H1 (peak near F0) and A3 (peak near the harmonic closest to F3), alongside
    /// F1–F3 and their bandwidths from the 5500 Hz formant track.
    /// </summary>
    public static RawAnalysis.WeightRow? MeasureWeightRow(Sound sound, IFormantTrack formants, double t, double f0)
    {
        var window = Math.Max(0.025, 3.0 / f0);
        double t0 = t - window / 2, t1 = t + window / 2;
        if (t0 < 0 || t1 > sound.Duration) return null;

        var f = new double[4];
        var b = new double[4];
        for (var n = 1; n <= 3; n++)
        {
            f[n] = formants.ValueAtTime(n, t);
            b[n] = formants.BandwidthAtTime(n, t);
            if (!(f[n] > 0 && b[n] > 0)) return null;
        }

        var spectrum = Spectrum.FromSound(sound.ExtractPart(t0, t1, WindowShape.Hamming));
        if (HarmonicPeakDb(spectrum, f0, f0) is not { } h1) return null;
        var k3 = Math.Max(1, Math.Floor(f[3] / f0 + 0.5));
        if (HarmonicPeakDb(spectrum, k3 * f0, f0) is not { } a3) return null;
        return new RawAnalysis.WeightRow(t, f0, f[1], f[2], f[3], b[1], b[2], b[3], h1, a3, k3);
    }

    /// <summary>_harmonic_db(): highest bin power (dB) within ±max(F0/2, 20 Hz) of the target; null if any bin there is 0.</summary>
    private static double? HarmonicPeakDb(Spectrum spectrum, double target, double f0)
    {
        if (target <= 0 || f0 <= 0) return null;
        var half = Math.Max(0.5 * f0, 20);
        double lo = target - half, hi = target + half;
        var from = Math.Max(0, (int)Math.Floor(lo / spectrum.BinWidth) - 1);
        var to = Math.Min(spectrum.BinCount - 1, (int)Math.Ceiling(hi / spectrum.BinWidth) + 1);
        double? best = null;
        for (var i = from; i <= to; i++)
        {
            var freq = spectrum.FrequencyOfBin(i);
            if (freq < lo || freq > hi) continue;
            var power = spectrum.Power(i);
            if (!(power > 0)) return null;
            var db = 10 * Math.Log10(power);
            if (best is null || db > best) best = db;
        }
        return best;
    }

    /// <summary>Keeps items[⌊k·n/max⌋] for k &lt; max when there are more than max items.</summary>
    public static List<T> Subsample<T>(List<T> items, int max)
    {
        if (items.Count <= max) return items;
        var step = (double)items.Count / max;
        var result = new List<T>(max);
        for (var k = 0; k < max; k++) result.Add(items[(int)Math.Floor(k * step)]);
        return result;
    }
}
