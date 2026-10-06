using System.Globalization;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Spectral;
using Reyfen.Timbratune.Acoustics.Tests.Oracle;
using Reyfen.Timbratune.Acoustics.Voice;
using Xunit.Abstractions;

namespace Reyfen.Timbratune.Acoustics.Tests;

/// <summary>
/// Compares every component with real Praat on the VCTK fixtures. Skipped
/// (passes with a note) when tools/praat/Praat.exe isn't present.
/// </summary>
public sealed class OracleTests(ITestOutputHelper output)
{
    public static TheoryData<string> Clips => new() { "vctk_f294", "vctk_f339", "vctk_m311", "vctk_m345" };

    private static string Wav(string clip) => Path.Combine(AppContext.BaseDirectory, "Fixtures", clip + ".wav");

    private static Sound Load(string clip)
    {
        using var s = File.OpenRead(Wav(clip));
        return WavDecoder.Decode(s);
    }

    private PraatDump? Oracle(string clip)
    {
        var dump = PraatDump.For(Wav(clip));
        if (dump is null) output.WriteLine("Praat not found (tools/praat) — oracle comparison skipped.");
        return dump;
    }

    [Theory, MemberData(nameof(Clips))]
    public void Pitch(string clip)
    {
        if (Oracle(clip) is not { } o) return;
        var pitch = PitchAnalyzer.Autocorrelation(Load(clip), 0, 75, 500);
        var grid = o["pitchgrid"][0];
        Assert.Equal((int)grid[0], pitch.FrameCount);
        Assert.Equal(grid[1], pitch.Grid.IndexToX(0), 12);

        int voicingMismatch = 0, compared = 0;
        var maxDiff = 0.0;
        foreach (var row in o["pitch"])
        {
            var i = (int)row[0] - 1;
            var expected = row[1];
            var actual = pitch.ValueInFrame(i);
            if (double.IsNaN(expected) != double.IsNaN(actual)) { voicingMismatch++; continue; }
            if (double.IsNaN(expected)) continue;
            compared++;
            maxDiff = Math.Max(maxDiff, Math.Abs(expected - actual));
        }
        var stats = o["pitchstats"][0];
        output.WriteLine($"{clip}: frames {pitch.FrameCount}, voicing mismatches {voicingMismatch}, max |ΔF0| {maxDiff:g3} Hz over {compared}");
        output.WriteLine($"  mean {stats[0]:F4} vs {pitch.Mean():F4}; sd {stats[1]:F4} vs {pitch.StandardDeviation():F4}; " +
                         $"median {stats[2]:F4} vs {pitch.Quantile(0.5):F4}; min {stats[3]:F4} vs {pitch.Minimum():F4}; max {stats[4]:F4} vs {pitch.Maximum():F4}");

        Assert.True(voicingMismatch <= pitch.FrameCount * 0.005, $"{voicingMismatch} voicing decisions differ");
        Assert.True(maxDiff < 0.01, $"max F0 difference {maxDiff} Hz");
        Assert.Equal(stats[0], pitch.Mean(), 2);
        Assert.Equal(stats[1], pitch.StandardDeviation(), 2);
        Assert.Equal(stats[2], pitch.Quantile(0.5), 2);
        Assert.Equal(stats[3], pitch.Minimum(), 2);
        Assert.Equal(stats[4], pitch.Maximum(), 2);
    }

    [Theory, MemberData(nameof(Clips))]
    public void IntensityAndSilences(string clip)
    {
        if (Oracle(clip) is not { } o) return;
        var intensity = IntensityAnalyzer.Analyze(Load(clip), 75);
        Assert.Equal((int)o["intgrid"][0][0], intensity.Grid.Count);
        var maxDiff = o["int"].Max(r => Math.Abs(r[1] - intensity.Db[(int)r[0] - 1]));
        var stats = o["intstats"][0];
        output.WriteLine($"{clip}: max |ΔdB| {maxDiff:g3}; mean {stats[0]:F6} vs {intensity.MeanEnergyDb():F6}; " +
                         $"min {stats[1]:F4} vs {intensity.Minimum():F4}; max {stats[2]:F4} vs {intensity.Maximum():F4}");
        Assert.True(maxDiff < 1e-6, $"intensity differs by {maxDiff} dB");
        Assert.Equal(stats[0], intensity.MeanEnergyDb(), 6);
        Assert.Equal(stats[1], intensity.Minimum(), 6);
        Assert.Equal(stats[2], intensity.Maximum(), 6);

        var intervals = SilenceDetector.Detect(intensity);
        output.WriteLine("  praat:  " + string.Join(" ", o.Intervals.Select(i => $"[{i.Start:F3}-{i.End:F3} {i.Label}]")));
        output.WriteLine("  csharp: " + string.Join(" ", intervals.Select(i => $"[{i.Start:F3}-{i.End:F3} {(i.IsSounding ? "sounding" : "silent")}]")));
        Assert.Equal(o.Intervals.Count, intervals.Count);
        for (var i = 0; i < intervals.Count; i++)
        {
            Assert.Equal(o.Intervals[i].Start, intervals[i].Start, 9);
            Assert.Equal(o.Intervals[i].End, intervals[i].End, 9);
            Assert.Equal(o.Intervals[i].Label == "sounding", intervals[i].IsSounding);
        }
    }

    [Theory, MemberData(nameof(Clips))]
    public void Harmonicity(string clip)
    {
        if (Oracle(clip) is not { } o) return;
        var hnr = HarmonicityAnalyzer.CrossCorrelation(Load(clip));
        var rows = o["hnr"];
        Assert.Equal(rows.Count, hnr.Db.Count);
        var maxDiff = rows.Max(r => Math.Abs(r[1] - hnr.Db[(int)r[0] - 1]));
        var expectedMean = o["hnrmean"][0][0];
        output.WriteLine($"{clip}: max |ΔHNR| {maxDiff:g3} dB; mean {expectedMean:F6} vs {hnr.Mean():F6}");
        Assert.Equal(expectedMean, hnr.Mean(), 2);
    }

    [Theory, MemberData(nameof(Clips))]
    public void PulsesJitterShimmer(string clip)
    {
        if (Oracle(clip) is not { } o) return;
        var sound = Load(clip);
        var pitch = PitchAnalyzer.Autocorrelation(sound, 0, 75, 500);
        var pulses = PulseDetector.PeriodicCrossCorrelation(sound, pitch);
        var expected = o["pulse"].Select(r => r[0]).ToArray();
        var matched = expected.Count(e => pulses.Any(p => Math.Abs(p - e) < 1e-5));
        var voice = o["voice"][0];
        var jitter = VoiceReport.JitterLocal(pulses);
        var shimmer = VoiceReport.ShimmerLocal(pulses, sound);
        output.WriteLine($"{clip}: pulses {expected.Length} vs {pulses.Length}, matched {matched}; " +
                         $"jitter {voice[0]:F6} vs {jitter:F6}; shimmer {voice[1]:F6} vs {shimmer:F6}");
        Assert.True(matched >= expected.Length * 0.99, $"only {matched}/{expected.Length} pulses match");
        Assert.Equal(voice[0] * 100, jitter * 100, 2);
        Assert.Equal(voice[1] * 100, shimmer * 100, 2);
    }

    [Theory, MemberData(nameof(Clips))]
    public void Formants(string clip)
    {
        if (Oracle(clip) is not { } o) return;
        var sound = Load(clip);
        foreach (var ceiling in new[] { 5500.0, 5000.0 })
        {
            var formants = FormantAnalyzer.Burg(sound, 0, 5, ceiling, 0.025, 50);
            var grid = o["fmtgrid"].First(g => g[0] == ceiling);
            Assert.Equal((int)grid[1], formants.Grid.Count);
            Assert.Equal(grid[2], formants.Grid.IndexToX(0), 9);

            int countMismatch = 0, compared = 0, over001 = 0, over1 = 0, skipped = 0;
            var maxDiff = 0.0;
            var worst = "";
            foreach (var row in o["fmt"].Where(r => r[0] == ceiling))
            {
                // Digital silence (exact zeros in the fixtures) leaves only resampling round-off
                // noise, whose "formants" are arbitrary in any implementation — compare real signal only.
                if (WindowRms(sound, formants.Grid.IndexToX((int)row[1] - 1), 0.025) < 1e-4) { skipped++; continue; }
                var frame = formants.Frames[(int)row[1] - 1];
                var n = (int)row[2];
                if (frame.Count != n) { countMismatch++; continue; }
                for (var k = 0; k < n; k++)
                {
                    compared++;
                    var d = Math.Abs(row[3 + 2 * k] - frame[k].Frequency);
                    if (d > 0.01) over001++;
                    if (d > 1) over1++;
                    if (d > maxDiff)
                    {
                        maxDiff = d;
                        worst = $"frame {row[1]} F{k + 1}: praat {row[3 + 2 * k]:F2} (bw {row[4 + 2 * k]:F1}) vs {frame[k].Frequency:F2} (bw {frame[k].Bandwidth:F1})";
                    }
                }
            }
            output.WriteLine($"{clip} @ {ceiling}: frames {formants.Grid.Count} ({skipped} silent skipped), count mismatches {countMismatch}, " +
                             $"max |ΔF| {maxDiff:g3} Hz over {compared}; >0.01 Hz: {over001}, >1 Hz: {over1}; worst {worst}");
            Assert.True(countMismatch <= formants.Grid.Count * 0.005, $"{countMismatch} frames differ in formant count");
            Assert.True(maxDiff < 1.0, $"formant frequency differs by {maxDiff} Hz");
        }
    }

    private static double WindowRms(Sound sound, double t, double halfWidth)
    {
        var (from, to) = sound.Grid.WindowIndices(t - halfWidth, t + halfWidth);
        if (to < from) return 0;
        var sum = 0.0;
        for (var i = from; i <= to; i++) sum += sound.MonoSample(i) * sound.MonoSample(i);
        return Math.Sqrt(sum / (to - from + 1));
    }

    [Theory, MemberData(nameof(Clips))]
    public void SpectrumAndLtas(string clip)
    {
        if (Oracle(clip) is not { } o) return;
        var sound = Load(clip);
        var part = sound.ExtractPart(sound.Duration * 0.4, sound.Duration * 0.4 + 0.03, WindowShape.Hamming);
        var spectrum = Spectrum.FromSound(part);
        Assert.Equal((int)o["specgrid"][0][0], spectrum.BinCount);
        Assert.Equal(o["specgrid"][0][1], spectrum.BinWidth, 9);
        var scale = o["spec"].Max(r => Math.Abs(r[1]) + Math.Abs(r[2]));
        var maxDiff = o["spec"].Max(r => Math.Abs(r[1] - spectrum.Re[(int)r[0] - 1]) + Math.Abs(r[2] - spectrum.Im[(int)r[0] - 1]));

        var ltas = Ltas.FromSound(sound, 100);
        var ltasDiff = o["ltas"].Max(r => Math.Abs(r[1] - ltas.ValueAtFrequency(r[0])));
        output.WriteLine($"{clip}: spectrum max rel. diff {maxDiff / scale:g3}; LTAS max |ΔdB| {ltasDiff:g3}");
        Assert.True(maxDiff / scale < 1e-9, "spectrum differs");
        Assert.True(ltasDiff < 1e-6, $"LTAS differs by {ltasDiff} dB");
    }
}
