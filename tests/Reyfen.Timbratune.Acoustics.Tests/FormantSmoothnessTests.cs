using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Streaming;
using Xunit.Abstractions;

namespace Reyfen.Timbratune.Acoustics.Tests;

/// <summary>Weenink's smoothness criterion, the running median and the fixed formant frame grid.</summary>
public sealed class FormantSmoothnessTests(ITestOutputHelper output)
{
    private sealed class Frames(double step, FormantValue[][] frames) : IFormantFrames
    {
        public int FrameCount => frames.Length;
        public double FrameTime(int index) => 0.1 + index * step;
        public ReadOnlySpan<FormantValue> FrameAt(int index) => frames[index];
        public FormantValue[][] All => frames;
    }

    /// <summary>16 frames of gliding F1–F4 (a diphthong-like movement), bandwidths <paramref name="bandwidthScale"/> × 60–150 Hz.</summary>
    private static Frames Glide(double bandwidthScale = 1, int count = 16)
    {
        var frames = new FormantValue[count][];
        var rng = new Random(5);
        for (var i = 0; i < count; i++)
        {
            var x = i / (double)(count - 1);
            frames[i] =
            [
                new(650 + 80 * x + rng.NextDouble() * 6, 60 * bandwidthScale),
                new(1200 + 500 * x + rng.NextDouble() * 10, 90 * bandwidthScale),
                new(2500 + 150 * x * x + rng.NextDouble() * 12, 120 * bandwidthScale),
                new(3500 + rng.NextDouble() * 20, 150 * bandwidthScale),
            ];
        }
        return new Frames(0.00625, frames);
    }

    [Fact]
    public void ALabelSlipMakesThePieceFarLessSmooth()
    {
        var smooth = Glide();
        var slipped = Glide();
        // A spurious pole between F2 and F3 for two frames: F3 takes its place, F4 takes F3's.
        foreach (var i in new[] { 7, 8 })
        {
            var f = slipped.All[i];
            slipped.All[i] = [f[0], f[1], new(0.5 * (f[1].Frequency + f[2].Frequency), 200), f[2], f[3]];
        }
        var w = FormantSmoothness.Criterion(smooth, 0, 16);
        var wSlipped = FormantSmoothness.Criterion(slipped, 0, 16);
        output.WriteLine($"W smooth {w:g4}, slipped {wSlipped:g4}");
        Assert.True(double.IsFinite(w) && w > 0);
        Assert.True(wSlipped > 100 * w, $"{wSlipped} vs {w}");
    }

    [Fact]
    public void DoubledBandwidthsScaleTheCriterionByAKnownPower()
    {
        // Weights 1/B²: the coefficient variances grow by 4, χ² shrinks by 4, so W grows by 4^t / 4.
        var w1 = FormantSmoothness.Criterion(Glide(1), 0, 16);
        var w2 = FormantSmoothness.Criterion(Glide(2), 0, 16);
        Assert.Equal(Math.Pow(2, 2 * FormantSmoothness.VariancePower - 2), w2 / w1, 9);
    }

    [Fact]
    public void TooFewFramesOrAMissingFormantCannotBeScored()
    {
        Assert.Equal(double.PositiveInfinity, FormantSmoothness.Criterion(Glide(), 0, 5));
        var gappy = Glide();
        for (var i = 0; i < 12; i++) gappy.All[i] = gappy.All[i][..2]; // F3 missing in most frames
        Assert.Equal(double.PositiveInfinity, FormantSmoothness.Criterion(gappy, 0, 16));
    }

    [Fact]
    public void PiecesCoverVoicedStretchesOfAtLeastEightFrames()
    {
        var voiced = new bool[60];
        for (var i = 2; i < 9; i++) voiced[i] = true;   // 7 frames: too short
        for (var i = 12; i < 40; i++) voiced[i] = true; // 28 frames
        for (var i = 50; i < 58; i++) voiced[i] = true; // exactly 8
        var pieces = new List<FramePiece>();
        FormantSmoothness.Pieces(voiced, pieces);
        Assert.Equal([new(12, 28), new(20, 36), new(28, 40), new(50, 58)], pieces);
    }

    [Fact]
    public void TheMedianRemovesAOneFrameSlipAndKeepsADrift()
    {
        var frames = Glide(count: 20);
        var original = frames.All.Select(f => f.ToArray()).ToArray();
        var f10 = frames.All[10];
        frames.All[10] = [f10[0], f10[1], new(1900, 300), f10[2], f10[3]];
        var smoothed = Enumerable.Range(0, 20).Select(i => FormantMedian.Frame(frames, i, 20)).ToArray();
        // The slipped frame gets back an F3 near its neighbours'.
        Assert.InRange(smoothed[10][2].Frequency, original[9][2].Frequency - 15, original[11][2].Frequency + 15);
        Assert.Equal(300, smoothed[10][2].Bandwidth); // a frame keeps its own bandwidth
        // A monotonic drift is unchanged where it is monotonic in its neighbours (F2 rises by ~26 Hz per frame).
        for (var i = 3; i < 8; i++) Assert.Equal(original[i][1].Frequency, smoothed[i][1].Frequency);
        // Smoothing clips at the end of what is available.
        Assert.Equal(FormantMedian.Frame(frames, 19, 20), FormantMedian.Frame(new Frames(0.00625, frames.All[..20]), 19, 20));
    }

    private static double[] Samples(string clip)
    {
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", clip + ".wav"));
        return WavDecoder.Decode(s).Channel(0).ToArray();
    }

    [Theory]
    [InlineData("vctk_f294", 4500)]
    [InlineData("vctk_m311", 6500)]
    public void OnAFixedGridLiveFramesFallOnTheSavedFrames(string clip, double ceiling)
    {
        var samples = Samples(clip);
        var saved = FormantAnalyzer.Burg(new Sound(samples, 44100), 0, 5, ceiling, 0.025, 50, gridOrigin: 0);
        Assert.Equal(0, Math.Round(saved.FrameTime(0) / saved.Grid.Step) * saved.Grid.Step - saved.FrameTime(0), 12);

        var tracker = new LiveFormantTracker(ceiling, gridOrigin: 0);
        var signal = new LiveSignal(44100);
        for (var i = 0; i < samples.Length; i += 4410)
        {
            signal.Append(samples.AsSpan(i, Math.Min(4410, samples.Length - i)));
            tracker.Update(signal.View());
        }
        tracker.Update(signal.View(), final: true);
        var live = tracker.Frames();

        // Every live frame is a saved frame, at its time (to rounding).
        var offset = (int)Math.Round((live.FrameTime(0) - saved.FrameTime(0)) / saved.Grid.Step);
        Assert.Equal(saved.FrameCount, live.FrameCount + offset);
        var differences = new List<double>();
        for (var i = 0; i < live.FrameCount; i++)
        {
            Assert.Equal(saved.FrameTime(i + offset), live.FrameTime(i), 1e-12);
            var a = live.FrameAt(i);
            var b = saved.FrameAt(i + offset);
            if (a.Length >= 3 && b.Length >= 3) differences.Add(Math.Abs(a[2].Frequency - b[2].Frequency));
        }
        differences.Sort();
        var median = differences[differences.Count / 2];
        output.WriteLine($"{clip} at {ceiling} Hz: {live.FrameCount} frames, median |ΔF3| {median:F3} Hz, p90 {differences[(int)(0.9 * differences.Count)]:F3} Hz");
        // Not identical: each live block is resampled on its own, and a resampled block's samples
        // can't fall on the whole take's (2 × ceiling is not a divisor of 44.1 kHz). Measured 2.4–3.2 Hz.
        Assert.True(median < 5, $"median |ΔF3| {median} Hz");
    }

    [Fact]
    public void TheLiveMedianEqualsTheMedianOfTheWholeTrack()
    {
        var samples = Samples("vctk_f339");
        var tracker = new LiveFormantTracker(5500, gridOrigin: 0);
        var signal = new LiveSignal(44100);
        IFormantTrack smoothed = null!;
        for (var i = 0; i < samples.Length; i += 2205)
        {
            signal.Append(samples.AsSpan(i, Math.Min(2205, samples.Length - i)));
            tracker.Update(signal.View());
            smoothed = tracker.SmoothedTrack();
        }
        tracker.Update(signal.View(), final: true);
        smoothed = tracker.SmoothedTrack();
        var frames = tracker.Frames();
        for (var i = 0; i < frames.FrameCount; i++)
        {
            var expected = FormantMedian.Frame(frames, i, frames.FrameCount);
            var t = frames.FrameTime(i);
            for (var n = 1; n <= 3 && n <= expected.Length; n++)
                Assert.Equal(expected[n - 1].Frequency, smoothed.ValueAtTime(n, t));
        }
    }
}
