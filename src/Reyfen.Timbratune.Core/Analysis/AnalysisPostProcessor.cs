using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// The statistics half of analyze.py: everything it does with numpy /
/// statistics on top of the raw measurements. Kept pure (no I/O) so it can be
/// unit-tested and reused by any future analysis engine.
/// </summary>
public static class AnalysisPostProcessor
{
    public const double DefaultRegisterFloorHz = 130.0;
    public const double SemitoneRefHz = 100.0;
    private const double FormantCeiling = 5500.0;

    public static AnalysisResult Process(RawAnalysis raw, double registerFloorHz = DefaultRegisterFloorHz)
    {
        var minF0 = raw.Get("pitch_min");
        var maxF0 = raw.Get("pitch_max");
        var jitter = raw.Get("jitter_local");
        var shimmer = raw.Get("shimmer_local");
        var formants = VowelFormants(raw.Formants);

        var metrics = new Recording
        {
            DurationS = Clean(raw.Get("duration")),
            Pitch = new Pitch
            {
                MeanHz = Clean(raw.Get("pitch_mean")),
                MedianHz = Clean(raw.Get("pitch_median")),
                MinHz = Clean(minF0),
                MaxHz = Clean(maxF0),
                RangeHz = double.IsNaN(minF0) || double.IsNaN(maxF0) ? null : Clean(maxF0 - minF0),
                SdHz = Clean(raw.Get("pitch_sd")),
            },
            Formants = new Formants
            {
                F1Hz = Clean(formants.F1),
                F2Hz = Clean(formants.F2),
                F3Hz = Clean(formants.F3),
            },
            VoiceQuality = new VoiceQuality
            {
                HnrDb = Clean(raw.Get("hnr")),
                JitterPct = Clean(jitter * 100),
                ShimmerPct = Clean(shimmer * 100),
            },
            Intensity = new Intensity
            {
                MeanDb = Clean(raw.Get("intensity_mean")),
                MinDb = Clean(raw.Get("intensity_min")),
                MaxDb = Clean(raw.Get("intensity_max")),
            },
            Weight = SpectralWeight(raw.WeightFrames, raw.Ltas, raw.Get("sampling_frequency")),
        };

        var (detail, register) = AnalyzeRegister(raw.Contour, raw.Sounding, raw.Get("duration"), registerFloorHz);
        metrics.Register = register;
        var fs = raw.Get("sampling_frequency");
        detail.PhraseMetrics = PhraseBreakdown(detail,
            formants.Kept.Select(r => (r.T, r.F2)).ToList(),
            raw.WeightFrames.Select(w => (w.T, CorrectedH1A3(w, fs))).Where(p => double.IsFinite(p.Item2)).ToList());
        return new AnalysisResult(metrics, detail);
    }

    // ------------------------------------------------------------------
    // per-phrase breakdown (C#-port addition) — the "trends within this take"
    // ------------------------------------------------------------------

    /// <summary>
    /// One entry per phrase in <paramref name="detail"/>.Phrases: pitch, melody
    /// and ending pitch from the 10 ms contour; F2 / weight from the frames
    /// (time, value) that fall inside the phrase, when given. Pass null for
    /// those to rebuild the pitch-based values of an older take.
    /// </summary>
    public static List<PhraseMetrics> PhraseBreakdown(
        RecordingDetail detail,
        IReadOnlyList<(double T, double F2)>? f2Frames = null,
        IReadOnlyList<(double T, double H1A3c)>? weightFrames = null)
    {
        var floor = detail.RegisterFloorHz;
        var voiced = detail.Frames.T.Zip(detail.Frames.Hz)
            .Where(p => p.Second is > 0)
            .Select(p => (T: p.First, Hz: p.Second!.Value))
            .ToList();

        var result = new List<PhraseMetrics>();
        foreach (var phrase in detail.Phrases)
        {
            bool Inside(double t) => phrase.Start <= t && t <= phrase.End;
            var hz = voiced.Where(v => Inside(v.T)).Select(v => v.Hz).ToList();
            var inReg = hz.Where(h => h >= floor).Select(HzToSt).ToList();
            var f2 = f2Frames?.Where(f => Inside(f.T)).Select(f => f.F2).ToList();
            var weight = weightFrames?.Where(w => Inside(w.T)).Select(w => w.H1A3c).ToList();

            result.Add(new PhraseMetrics
            {
                Start = phrase.Start,
                End = phrase.End,
                MeanHz = hz.Count > 0 ? Round(hz.Average(), 1) : null,
                MelodySt = Clean(SampleSd(inReg)),
                OffsetHz = phrase.OffsetHz,
                F2Hz = f2 is { Count: > 0 } ? Clean(Median(f2)) : null,
                WeightDb = weight is { Count: > 0 } ? Clean(weight.Average()) : null,
            });
        }
        return result;
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
        foreach (var ceiling in new[] { FormantCeiling, 5000.0 })
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
        var chosen = best ?? Gate(FormantCeiling);
        return (Median(chosen.Select(r => r.F1).ToList()), Median(chosen.Select(r => r.F2).ToList()),
            Median(chosen.Select(r => r.F3).ToList()), chosen);
    }

    // ------------------------------------------------------------------
    // spectral_weight(): Iseli–Alwan corrected H1*–A3*, plus LTAS tilt
    // ------------------------------------------------------------------
    internal static Weight SpectralWeight(
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

        return new Weight
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

    // ------------------------------------------------------------------
    // analyze_register(): contour, phrases, register stability
    // ------------------------------------------------------------------
    internal static (RecordingDetail Detail, Register Headline) AnalyzeRegister(
        IReadOnlyList<(double T, double Hz)> contour,
        IReadOnlyList<(double Start, double End)> spans,
        double duration,
        double floor)
    {
        var frames = new Frames();
        var voiced = new List<(double T, double Hz)>();
        foreach (var (t, hz) in contour)
        {
            frames.T.Add(Round(t, 3));
            frames.Hz.Add(hz > 0 ? Round(hz, 1) : null);
            if (hz > 0) voiced.Add((t, hz));
        }

        var phrases = new List<Phrase>();
        // [sub_count, total] per third of a phrase: onset, mid, offset
        var sub = new int[3];
        var total = new int[3];
        foreach (var (t0, t1) in spans)
        {
            var pv = voiced.Where(v => t0 <= v.T && v.T <= t1).ToList();
            if (pv.Count == 0) continue;
            var dur = t1 - t0;
            if (dur == 0) dur = 1e-9;

            var onsetWin = pv.Where(v => v.T - t0 <= 0.08).Select(v => v.Hz).ToList();
            if (onsetWin.Count == 0) onsetWin.Add(pv[0].Hz);
            var offsetWin = pv.Where(v => t1 - v.T <= 0.12).Select(v => v.Hz).ToList();
            if (offsetWin.Count == 0) offsetWin.Add(pv[^1].Hz);
            var onsetHz = onsetWin.Average();
            var offsetHz = offsetWin.Average();
            var hzs = pv.Select(v => v.Hz).ToList();

            phrases.Add(new Phrase
            {
                Start = Round(t0, 3),
                End = Round(t1, 3),
                OnsetHz = Round(onsetHz, 1),
                OffsetHz = Round(offsetHz, 1),
                MinHz = Round(hzs.Min(), 1),
                StartedInRegister = onsetHz >= floor,
                EndedInRegister = offsetHz >= floor,
                SubRegisterPct = Round(100.0 * hzs.Count(h => h < floor) / hzs.Count, 1),
            });

            foreach (var (t, h) in pv)
            {
                var rel = (t - t0) / dur;
                var b = rel < 1.0 / 3 ? 0 : rel < 2.0 / 3 ? 1 : 2;
                total[b]++;
                if (h < floor) sub[b]++;
            }
        }

        double? Pct(int b) => total[b] > 0 ? Round(100.0 * sub[b] / total[b], 1) : null;

        var allHz = voiced.Select(v => v.Hz).ToList();
        var inReg = allHz.Where(h => h >= floor).ToList();
        var landed = phrases.Count(p => p.EndedInRegister);

        var headline = new Register
        {
            FloorHz = floor,
            InRegisterPct = allHz.Count > 0 ? Round(100.0 * inReg.Count / allHz.Count, 1) : null,
            SemitonesSd = Clean(SampleSd(allHz.Select(HzToSt).ToList())),
            InRegisterSemitonesSd = Clean(SampleSd(inReg.Select(HzToSt).ToList())),
            OnsetSubPct = Pct(0),
            MidSubPct = Pct(1),
            OffsetSubPct = Pct(2),
            PhrasesLandedPct = phrases.Count > 0 ? Math.Round(100.0 * landed / phrases.Count, MidpointRounding.ToEven) : null,
            NPhrases = phrases.Count,
        };
        var detail = new RecordingDetail
        {
            RegisterFloorHz = floor,
            SemitoneRefHz = SemitoneRefHz,
            DurationS = Round(duration, 2),
            TimeStep = 0.01,
            Frames = frames,
            Phrases = phrases,
            Summary = headline,
        };
        return (detail, headline);
    }

    // ------------------------------------------------------------------
    // helpers (Python semantics)
    // ------------------------------------------------------------------

    /// <summary>analyze.py clean(): round to 2 dp; NaN/inf → null.</summary>
    internal static double? Clean(double? value) =>
        value is { } v && double.IsFinite(v) ? Round(v, 2) : null;

    /// <summary>Python round(x, n) — banker's rounding.</summary>
    internal static double Round(double value, int digits) => Math.Round(value, digits, MidpointRounding.ToEven);

    internal static double HzToSt(double hz) => 12.0 * Math.Log2(hz / SemitoneRefHz);

    /// <summary>statistics.median — mean of the middle two for even counts.</summary>
    internal static double? Median(List<double> values)
    {
        if (values.Count == 0) return null;
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>statistics.pstdev.</summary>
    internal static double PopulationSd(List<double> values)
    {
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / values.Count);
    }

    /// <summary>statistics.stdev (sample), or null with fewer than two values.</summary>
    internal static double? SampleSd(List<double> values)
    {
        if (values.Count < 2) return null;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    /// <summary>np.polyfit(x, y, 1)[0] — least-squares slope.</summary>
    internal static double LinearSlope(IReadOnlyList<(double X, double Y)> pts)
    {
        var mx = pts.Average(p => p.X);
        var my = pts.Average(p => p.Y);
        double sxy = 0, sxx = 0;
        foreach (var (x, y) in pts)
        {
            sxy += (x - mx) * (y - my);
            sxx += (x - mx) * (x - mx);
        }
        return sxy / sxx;
    }
}

/// <summary>
/// One analyzed take: <see cref="Metrics"/> carries every metric plus Register
/// (id/label/date/audio are filled in by the store); <see cref="Detail"/> is the
/// analysis/&lt;id&gt;.json payload.
/// </summary>
public sealed record AnalysisResult(Recording Metrics, RecordingDetail Detail);
