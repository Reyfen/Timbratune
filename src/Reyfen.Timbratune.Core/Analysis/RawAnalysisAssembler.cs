using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Spectral;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Analysis.Tracks;

namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// The selection steps of analyze.py on top of the tracks: voiced frames,
/// the loud vowel-core gate, subsampling, the F1 gate, weight frames, the contour
/// and the scalar summary. Shared by the full and the live analysis.
/// </summary>
public static class RawAnalysisAssembler
{
    public const double FormantCeiling = 5500;

    /// <summary>The raw values of a take from its tracks: each registered metric module adds its own.</summary>
    public static RawAnalysis Assemble(in TrackView t) => Assemble(t, MetricRegistry.Default);

    public static RawAnalysis Assemble(in TrackView t, MetricRegistry registry)
    {
        var raw = new RawAnalysisBuilder();
        raw.Summary["duration"] = t.Duration;
        raw.Summary["sampling_frequency"] = t.SamplingFrequency;
        foreach (var module in registry.Modules) module.Measure(t, raw);
        return raw.Build();
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

    /// <summary>The 10 ms pitch track as (time, Hz), 0 Hz where unvoiced (analyze_register() uses a 10 ms step; the track has exactly that step).</summary>
    public static List<(double T, double Hz)> Contour(PitchContour pitch)
    {
        var contour = new List<(double, double)>(pitch.FrameCount);
        for (var i = 0; i < pitch.FrameCount; i++)
        {
            var hz = pitch.ValueInFrame(i);
            contour.Add((pitch.Grid.IndexToX(i), double.IsNaN(hz) ? 0 : hz));
        }
        return contour;
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

    public static List<(double T, double F0)> VoicedFrames(PitchContour pitch) => VoicedFrames(pitch, []);

    /// <summary>The same, filling <paramref name="into"/> (cleared first) so the live analysis can reuse one list.</summary>
    public static List<(double T, double F0)> VoicedFrames(PitchContour pitch, List<(double T, double F0)> into)
    {
        into.Clear();
        for (var i = 0; i < pitch.FrameCount; i++)
            if (pitch.IsVoiced(i)) into.Add((pitch.Grid.IndexToX(i), pitch.ValueInFrame(i)));
        return into;
    }

    /// <summary>Loud voiced frames within this many dB of the loudness reference count as vowel core.</summary>
    public const double LoudWindowDb = 10;

    /// <summary>
    /// The loudest this fraction of the intensity frames is ignored when finding the loudness
    /// reference, so one click or pop can't set the bar (a 50 ms click left a 64 s take with 3
    /// loud frames and no formants at all). 0 = the loudest frame itself, as in analyze.py.
    /// </summary>
    public const double LoudOutlierFraction = 0.01;

    /// <summary>
    /// vowel_formants(): the voiced frames within 10 dB of the loudness reference (intensity
    /// read at the first frame at or after each time), subsampled to ≤ 300.
    /// </summary>
    public static List<double> SelectVowelCoreTimes(List<(double T, double F0)> voiced, IntensityContour intensity,
        double outlierFraction = LoudOutlierFraction) =>
        Subsample(LoudVoicedTimes(voiced, intensity, [], outlierFraction), 300);

    /// <summary>
    /// The selected times with a plausible F1 (250–1000 Hz). With Legacy, measured at both ceilings
    /// (all 5500 Hz rows, then all 5000 Hz rows); otherwise on <see cref="TrackView.Formants"/>, each row
    /// tagged with the ceiling that holds at its time.
    /// </summary>
    public static List<RawAnalysis.FormantRow> VowelCoreFormants(List<double> selected, in TrackView tracks)
    {
        if (tracks.FormantMethod == Ceilings.FormantCeilingMethod.Legacy)
            return VowelCoreFormants(selected, [(tracks.FormantCeilings[0], tracks.FormantCandidates[0]), (tracks.FormantCeilings[1], tracks.FormantCandidates[1])]);
        var rows = new List<RawAnalysis.FormantRow>();
        var path = tracks.CeilingPath;
        foreach (var t in selected)
        {
            if (PlausibleRow(tracks.Formants, t, path is null ? Ceilings.FormantCeilings.FallbackHz : tracks.FormantCeilings[path.At(t)]) is { } row)
                rows.Add(row);
        }
        return rows;
    }

    private static RawAnalysis.FormantRow? PlausibleRow(IFormantTrack track, double t, double ceiling)
    {
        double f1 = track.ValueAtTime(1, t), f2 = track.ValueAtTime(2, t), f3 = track.ValueAtTime(3, t);
        if (!(f1 > 0 && f2 > 0 && f3 > 0)) return null; // also rejects NaN
        if (f1 < 250 || f1 > 1000) return null;
        return new RawAnalysis.FormantRow(ceiling, t, f1, f2, f3);
    }

    /// <summary>The selected times with a plausible F1 (250–1000 Hz) — measured at each formant ceiling.</summary>
    public static List<RawAnalysis.FormantRow> VowelCoreFormants(List<double> selected, (double Ceiling, IFormantTrack Track)[] ceilings)
    {
        var rows = new List<RawAnalysis.FormantRow>();
        foreach (var (ceiling, track) in ceilings)
        {
            foreach (var t in selected)
                if (PlausibleRow(track, t, ceiling) is { } row) rows.Add(row);
        }
        return rows;
    }

    /// <summary>Times of voiced frames within <see cref="LoudWindowDb"/> of the loudness reference.</summary>
    public static List<double> LoudVoicedTimes(List<(double T, double F0)> voiced, IntensityContour intensity,
        double outlierFraction = LoudOutlierFraction) =>
        LoudVoicedTimes(voiced, intensity, [], outlierFraction);

    /// <summary>The same, filling <paramref name="candidates"/> (cleared first).</summary>
    public static List<double> LoudVoicedTimes(List<(double T, double F0)> voiced, IntensityContour intensity, List<double> candidates,
        double outlierFraction = LoudOutlierFraction, List<double>? scratch = null)
    {
        var db = intensity.Db;
        var loudFloor = LoudReference(db, outlierFraction, scratch) - LoudWindowDb;
        candidates.Clear();
        var p = 0;
        foreach (var (t, _) in voiced)
        {
            while (p < db.Count - 1 && intensity.Grid.IndexToX(p) < t) p++;
            if (db.Count > 0 && db[p] >= loudFloor) candidates.Add(t);
        }
        return candidates;
    }

    /// <summary>
    /// The loudest intensity value once the loudest <paramref name="outlierFraction"/> of the
    /// frames (rounded down: none below 100 frames) is set aside; NaN if there are none.
    /// </summary>
    public static double LoudReference(IReadOnlyList<double> db, double outlierFraction, List<double>? scratch = null)
    {
        if (outlierFraction <= 0)
        {
            var max = double.NaN;
            foreach (var v in db)
                if (!double.IsNaN(v) && !(v <= max)) max = v;
            return max;
        }
        var sorted = scratch ?? [];
        sorted.Clear();
        foreach (var v in db)
            if (!double.IsNaN(v)) sorted.Add(v);
        if (sorted.Count == 0) return double.NaN;
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(sorted).Sort();
        return sorted[sorted.Count - 1 - (int)Math.Floor(outlierFraction * sorted.Count)];
    }

    /// <summary>spectral_weight(): weight rows for ≤ 250 voiced frames.</summary>
    public static List<RawAnalysis.WeightRow> WeightFrames(List<(double T, double F0)> voiced, Func<double, double, RawAnalysis.WeightRow?> weightRow)
    {
        var frames = Subsample(voiced, 250);
        var rows = new RawAnalysis.WeightRow?[frames.Count];
        Parallel.For(0, frames.Count, Parallelism.Options, k => rows[k] = weightRow(frames[k].T, frames[k].F0));
        return rows.Where(r => r.HasValue).Select(r => r!.Value).ToList();
    }

    /// <summary>
    /// One weight row: a Hamming-windowed narrowband spectrum (window max(25 ms, 3 periods))
    /// gives H1 (peak near F0) and A3 (peak near the harmonic closest to F3), alongside
    /// F1–F3 and their bandwidths from the formant track (<see cref="TrackView.Formants"/>).
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
