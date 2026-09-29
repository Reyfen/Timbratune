using System.Globalization;
using System.Text;
using Euphonia.Core.Analysis;
using Euphonia.Core.Json;
using Euphonia.Core.Models;
using Xunit.Abstractions;

namespace Euphonia.Core.Tests;

/// <summary>
/// Runs the real Praat binary on the fixture clips and compares every metric
/// with what Euphonia-TypeScript/analyze.py (parselmouth) produced for the same
/// WAV. Skipped (passes with a note) when Praat isn't installed.
/// </summary>
public sealed class ParityTests(ITestOutputHelper output)
{
    public static TheoryData<string> Clips => new() { "vctk_f294", "vctk_f339", "vctk_m311", "vctk_m345" };

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [MemberData(nameof(Clips))]
    public async Task MatchesPythonAnalyzer(string clip)
    {
        var engine = new PraatAnalysisEngine(PraatLocator.Find());
        if (!engine.IsAvailable)
        {
            output.WriteLine("Praat not found — parity test skipped. " + engine.UnavailableReason);
            return;
        }

        var result = await engine.AnalyzeAsync(Fixture(clip + ".wav"));
        var expected = EuphoniaJson.ReadRecordings(File.ReadAllText(Fixture(clip + ".expected.json")))[0];
        var expectedDetail = EuphoniaJson.ReadDetail(File.ReadAllText(Fixture(clip + ".expected-detail.json")))!;
        var actual = result.Metrics;

        var rows = new List<(string Name, double? Expected, double? Actual, double Tolerance)>
        {
            ("duration_s", expected.DurationS, actual.DurationS, 0.001),
            ("pitch.mean_hz", expected.Pitch.MeanHz, actual.Pitch.MeanHz, Tol.Exact),
            ("pitch.median_hz", expected.Pitch.MedianHz, actual.Pitch.MedianHz, Tol.Exact),
            ("pitch.min_hz", expected.Pitch.MinHz, actual.Pitch.MinHz, Tol.Exact),
            ("pitch.max_hz", expected.Pitch.MaxHz, actual.Pitch.MaxHz, Tol.Exact),
            ("pitch.range_hz", expected.Pitch.RangeHz, actual.Pitch.RangeHz, Tol.Exact),
            ("pitch.sd_hz", expected.Pitch.SdHz, actual.Pitch.SdHz, Tol.Exact),
            ("formants.f1_hz", expected.Formants.F1Hz, actual.Formants.F1Hz, Tol.Formant),
            ("formants.f2_hz", expected.Formants.F2Hz, actual.Formants.F2Hz, Tol.Formant),
            ("formants.f3_hz", expected.Formants.F3Hz, actual.Formants.F3Hz, Tol.Formant),
            ("voice_quality.hnr_db", expected.VoiceQuality.HnrDb, actual.VoiceQuality.HnrDb, Tol.Exact),
            ("voice_quality.jitter_pct", expected.VoiceQuality.JitterPct, actual.VoiceQuality.JitterPct, Tol.Perturbation),
            ("voice_quality.shimmer_pct", expected.VoiceQuality.ShimmerPct, actual.VoiceQuality.ShimmerPct, Tol.Perturbation),
            ("intensity.mean_db", expected.Intensity.MeanDb, actual.Intensity.MeanDb, Tol.Exact),
            ("intensity.min_db", expected.Intensity.MinDb, actual.Intensity.MinDb, Tol.Exact),
            ("intensity.max_db", expected.Intensity.MaxDb, actual.Intensity.MaxDb, Tol.Exact),
            ("weight.h1a3c_db", expected.Weight?.H1a3cDb, actual.Weight?.H1a3cDb, Tol.Weight),
            ("weight.h1a3_db", expected.Weight?.H1a3Db, actual.Weight?.H1a3Db, Tol.Weight),
            ("weight.tilt_db_khz", expected.Weight?.TiltDbKhz, actual.Weight?.TiltDbKhz, Tol.Exact),
            ("register.in_register_pct", expected.Register?.InRegisterPct, actual.Register?.InRegisterPct, Tol.Exact),
            ("register.semitones_sd", expected.Register?.SemitonesSd, actual.Register?.SemitonesSd, Tol.Exact),
            ("register.in_register_semitones_sd", expected.Register?.InRegisterSemitonesSd, actual.Register?.InRegisterSemitonesSd, Tol.Exact),
            ("register.onset_sub_pct", expected.Register?.OnsetSubPct, actual.Register?.OnsetSubPct, Tol.Exact),
            ("register.mid_sub_pct", expected.Register?.MidSubPct, actual.Register?.MidSubPct, Tol.Exact),
            ("register.offset_sub_pct", expected.Register?.OffsetSubPct, actual.Register?.OffsetSubPct, Tol.Exact),
            ("register.phrases_landed_pct", expected.Register?.PhrasesLandedPct, actual.Register?.PhrasesLandedPct, Tol.Exact),
            ("register.n_phrases", expected.Register?.NPhrases, actual.Register?.NPhrases, Tol.Exact),
            ("detail.frames", expectedDetail.Frames.T.Count, result.Detail.Frames.T.Count, Tol.Exact),
            ("detail.phrases", expectedDetail.Phrases.Count, result.Detail.Phrases.Count, Tol.Exact),
        };

        var report = new StringBuilder();
        var failures = new List<string>();
        foreach (var (name, e, a, tol) in rows)
        {
            var allowed = tol < 0 ? -tol * Math.Abs(e ?? 0) : tol; // negative = relative
            var ok = e is null ? a is null : a is not null && Math.Abs(e.Value - a.Value) <= allowed + 1e-9;
            report.AppendLine(CultureInfo.InvariantCulture,
                $"{(ok ? "  " : "!!")} {name,-36} expected {Show(e),10}  actual {Show(a),10}  (±{(tol < 0 ? $"{-tol:P0}" : tol)})");
            if (!ok) failures.Add(name);
        }

        var voicedMismatch = CountVoicedMismatches(expectedDetail.Frames, result.Detail.Frames);
        report.AppendLine($"   contour frames with a different voiced/unvoiced call: {voicedMismatch}");
        output.WriteLine(report.ToString());

        Assert.True(failures.Count == 0, $"{clip}: out of tolerance: {string.Join(", ", failures)}\n{report}");

        // per-phrase breakdown (C#-port addition): one entry per phrase, and the
        // timestamped F2 / weight frames actually land inside phrases.
        var perPhrase = result.Detail.PhraseMetrics!;
        Assert.Equal(result.Detail.Phrases.Count, perPhrase.Count);
        Assert.Contains(perPhrase, p => p.F2Hz is not null);
        Assert.Contains(perPhrase, p => p.WeightDb is not null);
        Assert.All(perPhrase, p => Assert.NotNull(p.MeanHz));
    }

    private static int CountVoicedMismatches(Frames expected, Frames actual) =>
        expected.Hz.Zip(actual.Hz).Count(p => p.First.HasValue != p.Second.HasValue);

    private static string Show(double? v) => v?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null";

    /// <summary>
    /// Tolerances per metric family. "Exact" means equal after analyze.py's own
    /// 2-dp rounding (one last-digit wobble allowed). Negative values are
    /// relative (−0.05 = within 5% of the expected value).
    /// </summary>
    private static class Tol
    {
        public const double Exact = 0.011;
        public const double Formant = 0.011;
        public const double Weight = 0.011;

        // Jitter/shimmer come from To PointProcess (periodic, cc), whose pulse
        // picking differs slightly between Praat 6.1.38 (bundled in
        // parselmouth 0.4.7, which made the fixtures) and Praat 7.x (what we
        // ship). Measured gap on the fixtures: ≤ 4% relative — far inside the
        // 1-point-wide jitter zones. Everything else matches exactly.
        public const double Perturbation = -0.05;
    }
}
