using Euphonia.Acoustics;
using Euphonia.Acoustics.Formants;
using Euphonia.Acoustics.Intensity;
using Euphonia.Acoustics.Pitch;
using Euphonia.Acoustics.Spectral;
using Euphonia.Acoustics.Voice;

namespace Euphonia.Core.Analysis;

/// <summary>
/// The analysis engine: measures a take with Euphonia.Acoustics (pure C#, no
/// external programs) following the measurement steps of analyze.py, then
/// hands the raw values to <see cref="AnalysisPostProcessor"/>. Every setting
/// below matches the corresponding analyze.py / parselmouth call.
/// </summary>
public sealed class AcousticsAnalysisEngine : IAnalysisEngine
{
    private const double PitchFloor = 75, PitchCeiling = 500, FormantCeiling = 5500;

    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            Sound sound;
            using (var stream = File.OpenRead(wavPath)) sound = WavDecoder.Decode(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return AnalysisPostProcessor.Process(Measure(sound), registerFloorHz);
        }, cancellationToken);

    /// <summary>All raw measurements of one sound (independent analyses run in parallel).</summary>
    public static RawAnalysis Measure(Sound sound)
    {
        PitchContour pitch = null!;
        IntensityContour intensity = null!;
        HarmonicityContour harmonicity = null!;
        FormantContour formant5500 = null!, formant5000 = null!;
        Ltas ltas = null!;
        Parallel.Invoke(
            () => pitch = PitchAnalyzer.Autocorrelation(sound, 0, PitchFloor, PitchCeiling),
            () => intensity = IntensityAnalyzer.Analyze(sound, PitchFloor),
            () => harmonicity = HarmonicityAnalyzer.CrossCorrelation(sound, 0.01, PitchFloor, 0.1, 1.0),
            () => formant5500 = FormantAnalyzer.Burg(sound, 0, 5, FormantCeiling, 0.025, 50),
            () => formant5000 = FormantAnalyzer.Burg(sound, 0, 5, 5000, 0.025, 50),
            () => ltas = Ltas.FromSound(sound, 100));

        var pulses = PulseDetector.PeriodicCrossCorrelation(sound, pitch);
        var voiced = VoicedFrames(pitch);

        var summary = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["duration"] = sound.Duration,
            ["sampling_frequency"] = sound.SamplingFrequency,
            ["pitch_mean"] = pitch.Mean(),
            ["pitch_median"] = pitch.Quantile(0.5),
            ["pitch_min"] = pitch.Minimum(),
            ["pitch_max"] = pitch.Maximum(),
            ["pitch_sd"] = pitch.StandardDeviation(),
            ["hnr"] = harmonicity.Mean(),
            ["jitter_local"] = VoiceReport.JitterLocal(pulses, 0.0001, 0.02, 1.3),
            ["shimmer_local"] = VoiceReport.ShimmerLocal(pulses, sound, 0.0001, 0.02, 1.3, 1.6),
            ["intensity_mean"] = intensity.MeanEnergyDb(),
            ["intensity_min"] = intensity.Minimum(),
            ["intensity_max"] = intensity.Maximum(),
        };

        // analyze_register() uses a 10 ms step; the default step (3 periods / 75 Hz / 4) is the same.
        var contourPitch = 3.0 / PitchFloor / 4.0 == 0.01 ? pitch : PitchAnalyzer.Autocorrelation(sound, 0.01, PitchFloor, PitchCeiling);
        var contour = new List<(double, double)>(contourPitch.FrameCount);
        for (var i = 0; i < contourPitch.FrameCount; i++)
        {
            var hz = contourPitch.ValueInFrame(i);
            contour.Add((contourPitch.Grid.IndexToX(i), double.IsNaN(hz) ? 0 : hz));
        }

        var ltasPoints = new List<(double, double)>();
        for (var f = 100; f <= 5000; f += 100)
        {
            var v = ltas.ValueAtFrequency(f);
            if (!double.IsNaN(v)) ltasPoints.Add((f, v));
        }

        return new RawAnalysis
        {
            Summary = summary,
            Formants = VowelCoreFormants(voiced, intensity, [(FormantCeiling, formant5500), (5000, formant5000)]),
            WeightFrames = WeightFrames(sound, voiced, formant5500),
            Ltas = ltasPoints,
            Contour = contour,
            Sounding = SilenceDetector.Detect(intensity, -25, 0.1, 0.05)
                .Where(i => i.IsSounding).Select(i => (i.Start, i.End)).ToList(),
        };
    }

    private static List<(double T, double F0)> VoicedFrames(PitchContour pitch)
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
    private static List<RawAnalysis.FormantRow> VowelCoreFormants(List<(double T, double F0)> voiced, IntensityContour intensity,
        (double Ceiling, FormantContour Track)[] ceilings)
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
        var selected = Subsample(candidates, 300);

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

    /// <summary>
    /// spectral_weight(): for ≤ 250 voiced frames, a Hamming-windowed narrowband spectrum
    /// (window max(25 ms, 3 periods)) gives H1 (peak near F0) and A3 (peak near the harmonic
    /// closest to F3), alongside F1–F3 and their bandwidths from the 5500 Hz formant track.
    /// </summary>
    private static List<RawAnalysis.WeightRow> WeightFrames(Sound sound, List<(double T, double F0)> voiced, FormantContour formants)
    {
        var rows = new RawAnalysis.WeightRow?[Math.Min(voiced.Count, 250)];
        var frames = Subsample(voiced, 250);
        Parallel.For(0, frames.Count, k =>
        {
            var (t, f0) = frames[k];
            var window = Math.Max(0.025, 3.0 / f0);
            double t0 = t - window / 2, t1 = t + window / 2;
            if (t0 < 0 || t1 > sound.Duration) return;

            var f = new double[4];
            var b = new double[4];
            for (var n = 1; n <= 3; n++)
            {
                f[n] = formants.ValueAtTime(n, t);
                b[n] = formants.BandwidthAtTime(n, t);
                if (!(f[n] > 0 && b[n] > 0)) return;
            }

            var spectrum = Spectrum.FromSound(sound.ExtractPart(t0, t1, WindowShape.Hamming));
            if (HarmonicPeakDb(spectrum, f0, f0) is not { } h1) return;
            var k3 = Math.Max(1, Math.Floor(f[3] / f0 + 0.5));
            if (HarmonicPeakDb(spectrum, k3 * f0, f0) is not { } a3) return;
            rows[k] = new RawAnalysis.WeightRow(t, f0, f[1], f[2], f[3], b[1], b[2], b[3], h1, a3, k3);
        });
        return rows.Where(r => r.HasValue).Select(r => r!.Value).ToList();
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
    private static List<T> Subsample<T>(List<T> items, int max)
    {
        if (items.Count <= max) return items;
        var step = (double)items.Count / max;
        var result = new List<T>(max);
        for (var k = 0; k < max; k++) result.Add(items[(int)Math.Floor(k * step)]);
        return result;
    }
}
