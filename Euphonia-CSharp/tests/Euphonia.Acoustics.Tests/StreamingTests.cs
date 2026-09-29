using Euphonia.Acoustics.Intensity;
using Euphonia.Acoustics.Pitch;
using Euphonia.Acoustics.Streaming;
using Xunit.Abstractions;

namespace Euphonia.Acoustics.Tests;

/// <summary>
/// The live trackers use the same frame kernels as the full analyzers. When a
/// recording's length fits the frame grid exactly, the full analysis places its
/// frames exactly where the live one does, so the results must be bit-identical.
/// </summary>
public sealed class StreamingTests(ITestOutputHelper output)
{
    public static TheoryData<string> Clips => new() { "vctk_f294", "vctk_f339", "vctk_m311", "vctk_m345" };

    private static double[] Samples(string clip)
    {
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", clip + ".wav"));
        return WavDecoder.Decode(s).Channel(0).ToArray();
    }

    /// <summary>Feeds samples in irregular chunks, updating every ~100 ms, then finishes.</summary>
    private static LiveSignalView Stream(double[] samples, Action<LiveSignalView, bool> update)
    {
        var signal = new LiveSignal(44100);
        var rng = new Random(3);
        var nextUpdate = 4410;
        for (var i = 0; i < samples.Length;)
        {
            var n = Math.Min(rng.Next(1, 3000), samples.Length - i);
            signal.Append(samples.AsSpan(i, n));
            i += n;
            if (i >= nextUpdate)
            {
                update(signal.View(), false);
                nextUpdate += 4410;
            }
        }
        var view = signal.View();
        update(view, true);
        return view;
    }

    /// <summary>
    /// Largest length ≤ the clip for which the full analysis, computing its frame grid in
    /// floating point, puts the first frame exactly where the live grid has it (so every
    /// frame time coincides). Exact real-number fits are not enough: floor((d − w)/Δt)
    /// can land one below k and shift the whole grid by half a step.
    /// </summary>
    internal static int FittingLength(int available, double windowSeconds, double stepSeconds)
    {
        var dx = 1.0 / 44100;
        for (var n = available; n > available / 2; n--)
        {
            var samples = new TimeGrid(0, n * dx, n, dx, 0.5 * dx);
            var full = TimeGrid.ShortTermFrames(samples, windowSeconds, stepSeconds);
            var liveFirst = samples.First - 0.5 * dx + 0.5 * windowSeconds;
            // The two formulas may differ in the last bits; that doesn't move any frame to another sample.
            if (Math.Abs(full.First - liveFirst) < 1e-12) return n;
        }
        throw new InvalidOperationException("no fitting length");
    }

    [Theory, MemberData(nameof(Clips))]
    public void LivePitchIsIdenticalWhenTheGridFits(string clip)
    {
        var all = Samples(clip);
        var n = FittingLength(all.Length, 0.04, 0.01);
        var samples = all[..n];
        var tracker = LivePitchTracker.Autocorrelation(44100, 0, 75, 500);
        var view = Stream(samples, (v, final) => tracker.Update(v, final));
        var live = tracker.Contour(view);
        var full = PitchAnalyzer.Autocorrelation(new Sound(samples, 44100), 0, 75, 500);
        AssertSameContour(full, live, $"{clip} pitch (n = {n})");
    }

    [Theory, MemberData(nameof(Clips))]
    public void LiveHarmonicityIsIdenticalWhenTheGridFits(string clip)
    {
        var all = Samples(clip);
        var n = FittingLength(all.Length, 2.0 / 75, 0.01);
        var samples = all[..n];
        var tracker = LivePitchTracker.Harmonicity(44100);
        var view = Stream(samples, (v, final) => tracker.Update(v, final));
        var live = tracker.HarmonicityContour(view);
        var full = HarmonicityAnalyzer.CrossCorrelation(new Sound(samples, 44100));
        Assert.Equal(full.Grid.Count, live.Grid.Count);
        Assert.Equal(full.Grid.First, live.Grid.First, 12);
        Assert.Equal(full.Db, live.Db);
        Assert.Equal(full.Mean(), live.Mean());
        output.WriteLine($"{clip} harmonicity: {live.Grid.Count} frames identical, mean {live.Mean():F6} dB");
    }

    [Theory, MemberData(nameof(Clips))]
    public void LiveIntensityIsIdenticalWhenTheGridFits(string clip)
    {
        var all = Samples(clip);
        var n = FittingLength(all.Length, 6.4 / 75, 0.8 / 75);
        var samples = all[..n];
        var tracker = new LiveIntensityTracker(44100, 75);
        var view = Stream(samples, (v, final) => tracker.Update(v, final));
        var live = tracker.Contour(view);
        var full = IntensityAnalyzer.Analyze(new Sound(samples, 44100), 75);
        Assert.Equal(full.Grid.Count, live.Grid.Count);
        Assert.Equal(full.Grid.First, live.Grid.First, 12);

        // The intensity step (0.8/75 s = 470.4 samples at 44.1 kHz) puts every other frame
        // centre exactly halfway between two samples. Which one it rounds to then depends on
        // the last bit of the frame time, which the two grid formulas compute differently —
        // a tie, not a difference in the analysis. All other frames must be bit-identical.
        int identical = 0, ties = 0;
        for (var i = 0; i < full.Db.Count; i++)
        {
            var position = full.Grid.IndexToX(i) * 44100 - 0.5;
            var isTie = Math.Abs(position - Math.Floor(position) - 0.5) < 1e-6;
            if (full.Db[i] == live.Db[i]) identical++;
            else
            {
                Assert.True(isTie, $"frame {i} differs but is not a half-sample tie");
                Assert.True(Math.Abs(full.Db[i] - live.Db[i]) < 0.1, $"tie frame {i} differs by {Math.Abs(full.Db[i] - live.Db[i])} dB");
                ties++;
            }
        }
        output.WriteLine($"{clip} intensity: {identical} frames identical, {ties} half-sample ties within 0.1 dB");
    }

    [Theory, MemberData(nameof(Clips))]
    public void ChunkingDoesNotMatter(string clip)
    {
        // Same update times, different chunk sizes → identical frames.
        var samples = Samples(clip);
        PitchContour Run(int chunk)
        {
            var signal = new LiveSignal(44100);
            var tracker = LivePitchTracker.Autocorrelation(44100, 0, 75, 500);
            for (var i = 0; i < samples.Length; i += 4410)
            {
                var block = samples.AsSpan(i, Math.Min(4410, samples.Length - i));
                for (var j = 0; j < block.Length; j += chunk) signal.Append(block.Slice(j, Math.Min(chunk, block.Length - j)));
                tracker.Update(signal.View());
            }
            var view = signal.View();
            tracker.Update(view, final: true);
            return tracker.Contour(view);
        }
        var reference = Run(4410);
        foreach (var chunk in new[] { 64, 441, 1000 }) AssertSameContour(reference, Run(chunk), $"{clip} chunk {chunk}");
    }

    private void AssertSameContour(PitchContour expected, PitchContour actual, string what)
    {
        Assert.Equal(expected.FrameCount, actual.FrameCount);
        Assert.Equal(expected.Grid.IndexToX(0), actual.Grid.IndexToX(0), 12);
        for (var i = 0; i < expected.FrameCount; i++)
        {
            Assert.Equal(expected.Frames[i].LocalPeak, actual.Frames[i].LocalPeak);
            Assert.Equal(expected.Frames[i].AllCandidates, actual.Frames[i].AllCandidates);
            Assert.Equal(expected.Best(i), actual.Best(i));
        }
        output.WriteLine($"{what}: {actual.FrameCount} frames identical (candidates and chosen path)");
    }
}
