using System.Diagnostics;
using System.Globalization;
using System.Text;
using Euphonia.Acoustics;
using Euphonia.Core.Analysis;
using Euphonia.Core.Models;
using Xunit.Abstractions;

namespace Euphonia.Core.Tests;

/// <summary>
/// Streams the fixture clips through <see cref="LiveAnalyzer"/> exactly as the app
/// does while recording (irregular audio chunks, an update every 100 ms, the take
/// trimmed to <see cref="LiveAnalyzer.AlignedLength"/> on Stop) and compares the
/// final live snapshot with the full analysis of the saved (trimmed) audio.
/// </summary>
public sealed class LiveAnalysisTests(ITestOutputHelper output)
{
    public static TheoryData<string> Clips => new() { "vctk_f294", "vctk_f339", "vctk_m311", "vctk_m345" };

    private static double[] Samples(string clip)
    {
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", clip + ".wav"));
        return WavDecoder.Decode(s).Channel(0).ToArray();
    }

    /// <summary>Records <paramref name="samples"/> live; returns the final snapshot and the saved length.</summary>
    private static (LiveSnapshot Final, int SavedLength, int Updates, TimeSpan Analysis) Record(double[] samples)
    {
        var live = new LiveAnalyzer(44100);
        var rng = new Random(11);
        var next = 4410;
        var updates = 0;
        var sw = new Stopwatch();
        for (var i = 0; i < samples.Length;)
        {
            var n = Math.Min(rng.Next(200, 1500), samples.Length - i);
            live.Append(samples.AsSpan(i, n));
            i += n;
            if (i < next) continue;
            sw.Start();
            live.Update();
            sw.Stop();
            updates++;
            next += 4410;
        }
        var saved = live.AlignedLength(live.SampleCount);
        sw.Start();
        var final = live.Update(final: true, length: saved);
        sw.Stop();
        return (final, saved, updates + 1, sw.Elapsed);
    }

    [Theory, MemberData(nameof(Clips))]
    public void LiveMatchesTheSavedAnalysis(string clip)
    {
        var samples = Samples(clip);
        var (snapshot, savedLength, updates, time) = Record(samples);
        Assert.InRange(samples.Length - savedLength, 0, 441); // at most 10 ms trimmed
        var saved = AnalysisPostProcessor.Process(AcousticsAnalysisEngine.Measure(new Sound(samples[..savedLength], 44100)));
        Assert.NotNull(snapshot.Result);
        var live = snapshot.Result!;
        var e = saved.Metrics;
        var a = live.Metrics;

        const double Exact = 0;
        var rows = new List<(string Name, double? Saved, double? Live, double Tolerance)>
        {
            // Pitch frames coincide → identical.
            ("pitch.mean_hz", e.Pitch.MeanHz, a.Pitch.MeanHz, Exact),
            ("pitch.median_hz", e.Pitch.MedianHz, a.Pitch.MedianHz, Exact),
            ("pitch.sd_hz", e.Pitch.SdHz, a.Pitch.SdHz, Exact),
            ("pitch.min_hz", e.Pitch.MinHz, a.Pitch.MinHz, Exact),
            ("pitch.max_hz", e.Pitch.MaxHz, a.Pitch.MaxHz, Exact),
            ("voice_quality.jitter_pct", e.VoiceQuality.JitterPct, a.VoiceQuality.JitterPct, Exact),
            ("voice_quality.shimmer_pct", e.VoiceQuality.ShimmerPct, a.VoiceQuality.ShimmerPct, Exact),
            ("register.in_register_pct", e.Register?.InRegisterPct, a.Register?.InRegisterPct, Exact),
            ("register.semitones_sd", e.Register?.SemitonesSd, a.Register?.SemitonesSd, Exact),
            ("register.in_register_semitones_sd", e.Register?.InRegisterSemitonesSd, a.Register?.InRegisterSemitonesSd, Exact),
            // Intensity and HNR frames can't also sit on the pitch-aligned grid (other window and
            // step), so they are up to half a step off. Measured worst cases on the fixtures:
            // loudness mean/max 0.03 dB, HNR 0.2 dB. The loudness *minimum* is the digital-silence
            // floor (≈ −335 dB) and meaningless, hence its loose bound.
            ("intensity.mean_db", e.Intensity.MeanDb, a.Intensity.MeanDb, 0.05),
            ("intensity.min_db", e.Intensity.MinDb, a.Intensity.MinDb, 10),
            ("intensity.max_db", e.Intensity.MaxDb, a.Intensity.MaxDb, 0.05),
            ("voice_quality.hnr_db", e.VoiceQuality.HnrDb, a.VoiceQuality.HnrDb, 0.25),
            ("register.onset_sub_pct", e.Register?.OnsetSubPct, a.Register?.OnsetSubPct, 1),
            ("register.mid_sub_pct", e.Register?.MidSubPct, a.Register?.MidSubPct, 1),
            ("register.offset_sub_pct", e.Register?.OffsetSubPct, a.Register?.OffsetSubPct, 1),
            ("register.phrases_landed_pct", e.Register?.PhrasesLandedPct, a.Register?.PhrasesLandedPct, Exact),
            ("register.n_phrases", e.Register?.NPhrases, a.Register?.NPhrases, Exact),
            // Formants come from stitched live segments rather than one pass over the file, and
            // are read between frames at the voiced-frame times. Measured worst case 11 Hz (F3,
            // 0.4%) and 0.32 dB weight — well inside the zones (F2's neutral band is 80 Hz wide).
            ("formants.f1_hz", e.Formants.F1Hz, a.Formants.F1Hz, 15),
            ("formants.f2_hz", e.Formants.F2Hz, a.Formants.F2Hz, 15),
            ("formants.f3_hz", e.Formants.F3Hz, a.Formants.F3Hz, 15),
            ("weight.h1a3c_db", e.Weight?.H1a3cDb, a.Weight?.H1a3cDb, 0.4),
            ("weight.tilt_db_khz", e.Weight?.TiltDbKhz, a.Weight?.TiltDbKhz, Exact),
        };

        var report = new StringBuilder();
        var failures = new List<string>();
        foreach (var (name, x, y, tol) in rows)
        {
            var ok = x is null ? y is null : y is not null && Math.Abs(x.Value - y.Value) <= tol + 1e-9;
            var delta = x is { } p && y is { } q ? Math.Abs(p - q) : (double?)null;
            report.AppendLine(CultureInfo.InvariantCulture,
                $"{(ok ? "  " : "!!")} {name,-36} saved {Show(x),10}  live {Show(y),10}  Δ {Show(delta),8}  {(tol == 0 ? "(exact)" : $"(±{tol:0.###})")}");
            if (!ok) failures.Add(name);
        }
        var contourIdentical = saved.Detail.Frames.T.SequenceEqual(live.Detail.Frames.T) && saved.Detail.Frames.Hz.SequenceEqual(live.Detail.Frames.Hz);
        report.AppendLine($"   contour ({live.Detail.Frames.T.Count} frames): {(contourIdentical ? "identical" : "DIFFERENT")}");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"   {samples.Length - savedLength} samples trimmed; {updates} updates, {time.TotalMilliseconds:0} ms of analysis for {savedLength / 44100.0:0.0} s of audio");
        output.WriteLine(report.ToString());

        Assert.True(contourIdentical, "contour differs");
        Assert.True(failures.Count == 0, $"{clip}: live differs from saved in {string.Join(", ", failures)}\n{report}");
    }

    [Fact]
    public void SnapshotsDuringRecordingGrowAndStayConsistent()
    {
        var samples = Samples("vctk_f294");
        var live = new LiveAnalyzer(44100);
        LiveSnapshot? previous = null;
        for (var i = 0; i < samples.Length; i += 4410)
        {
            live.Append(samples.AsSpan(i, Math.Min(4410, samples.Length - i)));
            var snapshot = live.Update();
            if (snapshot.Result is { } r)
            {
                Assert.True(r.Detail.Frames.T.Count >= (previous?.Result?.Detail.Frames.T.Count ?? 0), "contour must never shrink");
                Assert.All(r.Detail.Frames.T, t => Assert.True(t <= snapshot.Elapsed));
            }
            previous = snapshot;
        }
        Assert.NotNull(previous?.Result);
        Assert.True(previous!.Series.Loudness.Count > 0 && previous.Series.F2.Count > 0 && previous.Series.Weight.Count > 0);
    }

    [Fact]
    public void TruncatingTheSavedWavKeepsItValid()
    {
        var path = Path.Combine(Path.GetTempPath(), $"euphonia-trunc-{Guid.NewGuid():N}.wav");
        try
        {
            using (var w = new Audio.WavWriter(path, 44100))
                w.Write(Enumerable.Range(0, 10000).Select(i => (float)Math.Sin(i * 0.05) * 0.5f).ToArray());
            Audio.WavWriter.Truncate(path, 9559);
            using var s = File.OpenRead(path);
            var sound = WavDecoder.Decode(s);
            Assert.Equal(9559, sound.SampleCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Show(double? v) => v?.ToString("0.####", CultureInfo.InvariantCulture) ?? "null";
}
