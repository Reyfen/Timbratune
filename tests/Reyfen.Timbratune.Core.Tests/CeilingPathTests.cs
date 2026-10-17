using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Xunit.Abstractions;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>The formant ceiling that follows the voice (RESONANCE-PLAN F2): decoding and the path on synthetic voices.</summary>
public sealed class CeilingPathTests(ITestOutputHelper output)
{
    private static int[] Decode(double[,] costs, double[] switchCosts, int? keepEnd = null)
    {
        var (n, k) = (costs.GetLength(0), costs.GetLength(1));
        var flat = new double[n * k];
        for (var i = 0; i < n; i++)
            for (var c = 0; c < k; c++) flat[i * k + c] = costs[i, c];
        var states = new int[n];
        CeilingPathFinder.Decode(flat, k, switchCosts, new int[n * k], states, keepEnd);
        return states;
    }

    /// <summary>Two states; state 1 is better by <paramref name="advantage"/> per piece on pieces [from, to), much worse elsewhere.</summary>
    private static double[,] Costs(int n, int from, int to, double advantage)
    {
        var costs = new double[n, 2];
        for (var i = 0; i < n; i++)
        {
            costs[i, 0] = 1;
            costs[i, 1] = i >= from && i < to ? 1 - advantage : 4;
        }
        return costs;
    }

    private static double[] Switches(int n, int pauseBefore = -1) =>
        [.. Enumerable.Range(0, n).Select(i => i == pauseBefore ? CeilingPathFinder.PauseSwitchCost : CeilingPathFinder.SwitchCost)];

    [Fact]
    public void AShortBetterStretchDoesNotSwitchButALongOneDoes()
    {
        // 5 pieces better by 1 (gain 5 < 2 × 12 for switching there and back): no switch.
        Assert.All(Decode(Costs(30, 10, 15, 1), Switches(30)), s => Assert.Equal(0, s));
        // 30 pieces better by 1: worth the two switches.
        var states = Decode(Costs(60, 15, 45, 1), Switches(60));
        Assert.Equal(0, states[14]);
        Assert.All(states[15..45], s => Assert.Equal(1, s));
        Assert.Equal(0, states[45]);
    }

    [Fact]
    public void APauseMakesASwitchCheaper()
    {
        // From piece 10 on, state 1 is better by 1 for 8 pieces: 8 < 12, but > 2 after a pause.
        Assert.All(Decode(Costs(18, 10, 18, 1), Switches(18)), s => Assert.Equal(0, s));
        var states = Decode(Costs(18, 10, 18, 1), Switches(18, pauseBefore: 10));
        Assert.Equal(0, states[9]);
        Assert.Equal(1, states[10]);
    }

    [Fact]
    public void LiveTheEndKeepsItsCeilingWithinTheMargin()
    {
        // After a pause the last 4 pieces favour state 1 by 0.9 each: 3.6 − 2 for the switch is within the end margin of 4.
        var costs = Costs(20, 16, 20, 0.9);
        var switches = Switches(20, pauseBefore: 16);
        Assert.Equal(1, Decode(costs, switches)[^1]);             // final: the cheapest end
        Assert.Equal(0, Decode(costs, switches, keepEnd: 0)[^1]); // live: kept
        // 8 such pieces (7.2 − 2 > 4): beyond the margin, the end moves even live.
        Assert.Equal(1, Decode(Costs(20, 12, 20, 0.9), Switches(20, pauseBefore: 12), keepEnd: 0)[^1]);
    }

    // ------------------------------------------------------------------
    // Synthetic voices: a pulse train through five resonances of a uniform tube
    // ------------------------------------------------------------------

    /// <summary>
    /// A vowel-like sound from a tube of length <paramref name="lengthCm"/>: Fi = (2i − 1) c / 4L for F1–F7, with
    /// a slow glide of F1/F2 and vibrato. (Resonances above F5 matter: without them a too-high ceiling fits as well.)
    /// </summary>
    private static (Sound Sound, double DeltaF) Voice(double lengthCm, double f0, double seconds, int seed)
    {
        const double fs = 44100;
        var deltaF = 35000 / (2 * lengthCm);
        var n = (int)(seconds * fs);
        var x = new double[n];
        var rng = new Random(seed);
        var phase = 0.0;
        for (var i = 0; i < n; i++)
        {
            var t = i / fs;
            phase += (f0 * (1 + 0.02 * Math.Sin(2 * Math.PI * 5 * t))) / fs;
            if (phase >= 1)
            {
                phase -= 1;
                x[i] = 1 + 0.05 * rng.NextDouble();
            }
        }
        // Glottal roll-off (two one-pole low-passes), then the resonances; F1 and F2 glide a little.
        for (var pass = 0; pass < 2; pass++)
            for (var i = 1; i < n; i++) x[i] += 0.97 * x[i - 1];
        double[] bandwidths = [70, 90, 130, 180, 250, 300, 350];
        for (var f = 0; f < bandwidths.Length; f++)
        {
            var y = new double[n];
            for (var i = 0; i < n; i++)
            {
                var t = i / fs;
                var glide = f < 2 ? 1 + 0.08 * Math.Sin(2 * Math.PI * 0.7 * t + f) : 1;
                var frequency = (2 * f + 1) * deltaF / 2 * glide;
                var r = Math.Exp(-Math.PI * bandwidths[f] / fs);
                var c1 = 2 * r * Math.Cos(2 * Math.PI * frequency / fs);
                y[i] = x[i] + (i >= 1 ? c1 * y[i - 1] : 0) - (i >= 2 ? r * r * y[i - 2] : 0);
            }
            x = y;
        }
        var peak = x.Max(Math.Abs);
        for (var i = 0; i < n; i++) x[i] *= 0.5 / peak;
        return (new Sound(x, fs), deltaF);
    }

    private static (FormantContour[] Smoothed, PitchContour Pitch) Analyse(Sound sound)
    {
        var pitch = PitchAnalyzer.Autocorrelation(sound, 0, AcousticsAnalysisEngine.PitchFloor, AcousticsAnalysisEngine.PitchCeiling);
        var contours = FormantAnalyzer.BurgAll(sound, FormantCeilings.PathCandidates, 0, AcousticsAnalysisEngine.FormantCount,
            AcousticsAnalysisEngine.FormantWindow, 50, gridOrigin: 0);
        return ([.. contours.Select(c => FormantMedian.Smooth(c))], pitch);
    }

    private static double MedianF3(IFormantTrack track, PitchContour pitch, double from, double to)
    {
        var values = new List<double>();
        for (var i = 0; i < pitch.FrameCount; i++)
        {
            var t = pitch.Grid.IndexToX(i);
            if (t < from || t > to || !pitch.IsVoiced(i)) continue;
            var f3 = track.ValueAtTime(3, t);
            if (double.IsFinite(f3)) values.Add(f3);
        }
        values.Sort();
        return values[values.Count / 2];
    }

    [Theory]
    [InlineData(17.5, 110)] // a long tract: ΔF 1000 Hz, ceiling about 5000
    [InlineData(14.5, 220)] // a short one: ΔF 1207 Hz, ceiling about 6000
    public void ASyntheticVoiceGetsACeilingNearFiveTimesItsFormantSpacing(double lengthCm, double f0)
    {
        var (sound, deltaF) = Voice(lengthCm, f0, 2.0, 1);
        var (smoothed, pitch) = Analyse(sound);
        var path = new CeilingPath();
        new CeilingPathFinder().Find(smoothed, pitch, FormantCeilings.FallbackIndex(FormantCeilings.PathCandidates), path);
        var ceiling = FormantCeilings.PathCandidates[path.Dominant];
        var f3 = MedianF3(new SwitchedFormantTrack(smoothed, path), pitch, 0, 2);
        output.WriteLine($"L {lengthCm} cm, ΔF {deltaF:F0} Hz: ceiling {ceiling} Hz, {path.Switches} switches, F3 {f3:F0} Hz (true {2.5 * deltaF:F0})");
        Assert.InRange(ceiling, 5 * deltaF - 600, 5 * deltaF + 600);
        Assert.InRange(f3, 0.88 * 2.5 * deltaF, 1.12 * 2.5 * deltaF);
    }

    [Fact]
    public void TheCeilingFollowsAChangeOfVoice()
    {
        // A long tract for 2 s, a pause, then a short one for 2 s.
        var (a, deltaA) = Voice(17.5, 110, 2.0, 2);
        var (b, deltaB) = Voice(14.5, 220, 2.0, 3);
        var gap = new double[(int)(0.3 * 44100)];
        var joined = new Sound([.. a.Channel(0).ToArray(), .. gap, .. b.Channel(0).ToArray()], 44100);
        var (smoothed, pitch) = Analyse(joined);
        var path = new CeilingPath();
        new CeilingPathFinder().Find(smoothed, pitch, FormantCeilings.FallbackIndex(FormantCeilings.PathCandidates), path);
        var track = new SwitchedFormantTrack(smoothed, path);
        double first = FormantCeilings.PathCandidates[path.At(1.0)], second = FormantCeilings.PathCandidates[path.At(3.3)];
        output.WriteLine($"ceilings {first} → {second} Hz, {path.Switches} switches");
        Assert.True(second > first, $"{first} → {second}");
        Assert.InRange(MedianF3(track, pitch, 0.3, 1.9), 0.88 * 2.5 * deltaA, 1.12 * 2.5 * deltaA);
        Assert.InRange(MedianF3(track, pitch, 2.6, 4.2), 0.88 * 2.5 * deltaB, 1.12 * 2.5 * deltaB);
    }

    [Fact]
    public void NothingVoicedGivesTheFallbackAndCountsDifferingByAFrameAreClipped()
    {
        var silence = new Sound(new double[44100], 44100);
        var (smoothed, pitch) = Analyse(silence);
        var path = new CeilingPath(3);
        var finder = new CeilingPathFinder();
        finder.Find(smoothed, pitch, 2, path);
        Assert.Equal(0, finder.PieceCount);
        Assert.Equal(2, path.Last);
        Assert.Equal(0, path.Switches);

        // One candidate with a frame fewer at the end: no exception, the same path as without the frame.
        var (sound, _) = Voice(17.5, 110, 1.0, 4);
        var (full, p) = Analyse(sound);
        var shorter = full.Select(IFormantFrames (c) => c).ToArray();
        shorter[4] = new Truncated(full[4], full[4].FrameCount - 1);
        var expected = new CeilingPath();
        new CeilingPathFinder().Find(full, p, 2, expected);
        var actual = new CeilingPath();
        new CeilingPathFinder().Find(shorter, p, 2, actual);
        Assert.Equal(expected.Dominant, actual.Dominant);
    }

    [Fact]
    public void AFinderReusedOnGrowingFramesGivesTheSamePathAsAFreshOne()
    {
        var (a, _) = Voice(17.5, 110, 1.5, 5);
        var (b, _) = Voice(14.5, 220, 1.5, 6);
        var (smoothed, pitch) = Analyse(new Sound([.. a.Channel(0).ToArray(), .. b.Channel(0).ToArray()], 44100));
        var reused = new CeilingPathFinder();
        var path = new CeilingPath();
        // As live: the frames grow, and the finder keeps its piece scores between calls.
        for (var count = 100; count < smoothed[0].FrameCount; count += 37)
            reused.Find([.. smoothed.Select(IFormantFrames (c) => new Truncated(c, Math.Min(count, c.FrameCount)))], pitch, 2, path);
        reused.Find(smoothed, pitch, 2, path);
        var fresh = new CeilingPath();
        new CeilingPathFinder().Find(smoothed, pitch, 2, fresh);
        Assert.Equal(fresh.SegmentCount, path.SegmentCount);
        for (var i = 0; i < fresh.SegmentCount; i++)
        {
            Assert.Equal(fresh.SegmentStart(i), path.SegmentStart(i));
            Assert.Equal(fresh.SegmentCeiling(i), path.SegmentCeiling(i));
        }
    }

    private sealed class Truncated(IFormantFrames frames, int count) : IFormantFrames
    {
        public int FrameCount => count;
        public double FrameTime(int index) => frames.FrameTime(index);
        public ReadOnlySpan<FormantValue> FrameAt(int index) => frames.FrameAt(index);
    }
}
