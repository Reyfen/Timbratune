using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Reyfen.Timbratune.Core.Models;
using Xunit.Abstractions;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>
/// HIGH-PITCH-PLAN.md: pitch, perceived size and weight are separate measurements, and each must read right on
/// its own. These tests pin that for the formant ceiling chooser, which v0.4.0's smoothness scoring broke on a
/// high-pitched, soft voice (HANDOFF §8v): the same vocal tract must get the same ceiling and the same size whether
/// it speaks low or high, breathy or clear, light or heavy. Synthetic voices with explicit resonances (the owner's
/// shape: a low F1 near the pitch) and the owner's own takes (<c>Fixtures/OWNER-TAKES.md</c>).
/// <para>
/// <see cref="Candidate"/> is the scoring under evaluation, the app's own (<see cref="CeilingScoring.Steady"/>) unless a
/// change is being measured. The last test pins that the app's default is the scoring these tests passed with.
/// </para>
/// </summary>
public sealed class HighPitchTests(ITestOutputHelper output)
{
    /// <summary>
    /// The scoring under test (HIGH-PITCH-PLAN §4.1): the app's, smoothness plus the cross-ceiling stability of F2–F4
    /// against the nearer neighbouring ceiling, plus the continuity of the chosen reading between pieces (HANDOFF §8x).
    /// Point it at a new scoring to measure a change.
    /// </summary>
    public static readonly CeilingScoring Candidate = CeilingScoring.Steady;

    private const double Fs = 44100;

    // ------------------------------------------------------------------
    // Synthetic voices: a pulse train, optional aspiration noise, a glottal tilt, explicit resonances
    // ------------------------------------------------------------------

    /// <summary>The owner's tract shape: a low F1, F2–F3 of a short tract, upper resonances about 1150 Hz apart.</summary>
    public static readonly double[] OwnerLikeFormants = [320, 1700, 2800, 4100, 5250, 6400, 7550];

    /// <summary>A long tract (a typical man): ΔF about 1000 Hz.</summary>
    public static readonly double[] LongTractFormants = [500, 1500, 2500, 3500, 4500, 5500, 6500];

    /// <summary>
    /// A vowel-like sound: pulses at <paramref name="f0"/> (with vibrato), <paramref name="tilt"/> one-pole
    /// low-passes on the pulses (2 = the usual glottal roll-off; 1 = a heavier, brighter voice), white noise
    /// at <paramref name="noiseDb"/> below the pulses (−∞ = none; about −10 dB gives an HNR near the owner's
    /// take 23), through the resonances <paramref name="formants"/> with a slow glide on F1 and F2.
    /// </summary>
    public static Sound Voice(double[] formants, double f0, double seconds, int seed, double noiseDb = double.NegativeInfinity, int tilt = 2)
    {
        var n = (int)(seconds * Fs);
        var x = new double[n];
        var rng = new Random(seed);
        var phase = 0.0;
        for (var i = 0; i < n; i++)
        {
            var t = i / Fs;
            phase += (f0 * (1 + 0.02 * Math.Sin(2 * Math.PI * 5 * t))) / Fs;
            if (phase >= 1)
            {
                phase -= 1;
                x[i] = 1 + 0.05 * rng.NextDouble();
            }
        }
        for (var pass = 0; pass < tilt; pass++)
            for (var i = 1; i < n; i++) x[i] += 0.97 * x[i - 1];
        if (double.IsFinite(noiseDb))
        {
            var pulseRms = Math.Sqrt(x.Sum(v => v * v) / n);
            var amplitude = pulseRms * Math.Pow(10, noiseDb / 20) * Math.Sqrt(3);
            for (var i = 0; i < n; i++) x[i] += amplitude * (2 * rng.NextDouble() - 1);
        }
        double[] bandwidths = [70, 90, 130, 180, 250, 300, 350];
        for (var f = 0; f < formants.Length; f++)
        {
            var y = new double[n];
            for (var i = 0; i < n; i++)
            {
                var t = i / Fs;
                var glide = f < 2 ? 1 + 0.08 * Math.Sin(2 * Math.PI * 0.7 * t + f) : 1;
                var frequency = formants[f] * glide;
                var r = Math.Exp(-Math.PI * bandwidths[Math.Min(f, bandwidths.Length - 1)] / Fs);
                var c1 = 2 * r * Math.Cos(2 * Math.PI * frequency / Fs);
                y[i] = x[i] + (i >= 1 ? c1 * y[i - 1] : 0) - (i >= 2 ? r * r * y[i - 2] : 0);
            }
            x = y;
        }
        var peak = x.Max(Math.Abs);
        for (var i = 0; i < n; i++) x[i] *= 0.5 / peak;
        return new Sound(x, Fs);
    }

    /// <summary>The take's cards through the whole saved analysis with the given ceiling scoring.</summary>
    private static (double F1, double F2, double F3, double Size, double Ceiling, double Weight, double Hnr, double Pitch) Cards(Sound sound, CeilingScoring scoring)
    {
        var tracks = AcousticsAnalysisEngine.MeasureTracks(sound, RawAnalysisAssembler.LoudOutlierFraction, null, FormantCeilingMethod.Path, scoring);
        var metrics = AnalysisPostProcessor.Process(RawAnalysisAssembler.Assemble(tracks)).Metrics;
        var f = metrics.Formants;
        return (f.F1Hz ?? double.NaN, f.F2Hz ?? double.NaN, f.F3Hz ?? double.NaN, f.VtlCm ?? double.NaN, f.CeilingHz ?? double.NaN,
            metrics.Weight?.H1a3cDb ?? double.NaN, metrics.VoiceQuality.HnrDb ?? double.NaN, metrics.Pitch.MedianHz ?? double.NaN);
    }

    private static double TrueSize(double[] formants)
    {
        // The same estimate the app uses: ΔF from F3 and F4 (Reby & McComb 2003), VTL = 35000 / 2ΔF.
        var deltaF = (2.5 * formants[2] + 3.5 * formants[3]) / (2.5 * 2.5 + 3.5 * 3.5);
        return 35000 / (2 * deltaF);
    }

    private void Log(string label, (double F1, double F2, double F3, double Size, double Ceiling, double Weight, double Hnr, double Pitch) c) =>
        output.WriteLine($"{label}: pitch {c.Pitch:F0} Hz, HNR {c.Hnr:F1} dB, weight {c.Weight:F1} dB | ceiling {c.Ceiling:F0} Hz, F1 {c.F1:F0} F2 {c.F2:F0} F3 {c.F3:F0} Hz, size {c.Size:F2} cm");

    /// <summary>
    /// Pitch and size are separate: the owner's tract shape at a low and at a high pitch (120 and 280 Hz, where
    /// the first harmonic sits on F1) must get a ceiling in the plateau and the same size within 0.5 cm.
    /// </summary>
    [Theory]
    [InlineData(120)]
    [InlineData(280)]
    [InlineData(320)]
    public void TheSameTractReadsTheSameSizeAtAnyPitch(double f0)
    {
        var c = Cards(Voice(OwnerLikeFormants, f0, 3.0, 11), Candidate);
        Log($"F0 {f0}", c);
        var expected = TrueSize(OwnerLikeFormants);
        Assert.InRange(c.Ceiling, 5500, 6500);
        Assert.InRange(c.F3, 0.92 * OwnerLikeFormants[2], 1.08 * OwnerLikeFormants[2]);
        Assert.InRange(c.Size, expected - 0.5, expected + 0.5);
    }

    /// <summary>Breathiness (noise, a lower HNR) is not size: the same tract, clear and breathy, within 0.5 cm.</summary>
    [Fact]
    public void ABreathyVoiceReadsTheSameSizeAsAClearOne()
    {
        var clear = Cards(Voice(OwnerLikeFormants, 280, 3.0, 12), Candidate);
        var breathy = Cards(Voice(OwnerLikeFormants, 280, 3.0, 12, noiseDb: -10), Candidate);
        Log("clear", clear);
        Log("breathy", breathy);
        Assert.True(breathy.Hnr < clear.Hnr - 3, $"the breathy voice should have a clearly lower HNR: {breathy.Hnr:F1} vs {clear.Hnr:F1} dB");
        Assert.InRange(breathy.Ceiling, 5500, 6500);
        Assert.InRange(breathy.Size, clear.Size - 0.5, clear.Size + 0.5);
    }

    /// <summary>Weight (spectral tilt) is not size: the same tract and pitch, a lighter and a heavier source, within 0.5 cm.</summary>
    [Fact]
    public void AHeavierVoiceReadsTheSameSizeAsALighterOne()
    {
        var light = Cards(Voice(OwnerLikeFormants, 280, 3.0, 13, tilt: 2), Candidate);
        var heavy = Cards(Voice(OwnerLikeFormants, 280, 3.0, 13, tilt: 1), Candidate);
        Log("light", light);
        Log("heavy", heavy);
        Assert.True(heavy.Weight < light.Weight - 2, $"the heavier source should read heavier: {heavy.Weight:F1} vs {light.Weight:F1} dB");
        Assert.InRange(heavy.Ceiling, 5500, 6500);
        Assert.InRange(heavy.Size, light.Size - 0.5, light.Size + 0.5);
    }

    /// <summary>A long tract keeps its low ceiling and its size at a low and at a high pitch (a man practising a higher voice).</summary>
    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    public void ALongTractReadsTheSameSizeAtAnyPitch(double f0)
    {
        var c = Cards(Voice(LongTractFormants, f0, 3.0, 14), Candidate);
        Log($"long tract F0 {f0}", c);
        var expected = TrueSize(LongTractFormants);
        Assert.InRange(c.Ceiling, 4500, 5500);
        Assert.InRange(c.Size, expected - 0.5, expected + 0.5);
    }

    // ------------------------------------------------------------------
    // The owner's takes (Fixtures/OWNER-TAKES.md): expected ranges from Praat at 5500–6500 Hz (F3: that band less 3 %;
    // F2: from 5000 Hz up, it depends on the text; size: Praat at 5500 Hz ± 0.5 cm), never from the app. A 4500 Hz slip
    // reads F2 1180–1230, F3 1980–2100 and 21 cm: far outside.
    // ------------------------------------------------------------------

    private static Sound Fixture(string name)
    {
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".wav"));
        return WavDecoder.Decode(s);
    }

    [Theory]
    [InlineData("owner_take22", 265, 1660, 1800, 2800, 2950, 13.0, 14.2)]
    [InlineData("owner_take23", 276, 1500, 1800, 2680, 2950, 13.6, 15.0)]
    public void TheOwnersTakesReadLikePraatAtTheRightCeiling(string name, double pitch, double f2Lo, double f2Hi, double f3Lo, double f3Hi, double sizeLo, double sizeHi)
    {
        var c = Cards(Fixture(name), Candidate);
        Log(name, c);
        Assert.InRange(c.Pitch, pitch - 5, pitch + 5);
        Assert.InRange(c.Ceiling, 5500, 6500);
        Assert.InRange(c.F2, f2Lo, f2Hi);
        Assert.InRange(c.F3, f3Lo, f3Hi);
        Assert.InRange(c.Size, sizeLo, sizeHi);
    }

    /// <summary>
    /// One voice a minute apart: the two takes' sizes within 1.2 cm of each other (Praat's own readings of the two takes at
    /// 5500 and 6000 Hz differ by up to 1.1 cm: 13.6 / 13.3 and 14.4 / 13.6 cm; v0.4.0 read them 4.5 cm apart).
    /// </summary>
    [Fact]
    public void TheOwnersTwoTakesAgreeOnSize()
    {
        var a = Cards(Fixture("owner_take22"), Candidate);
        var b = Cards(Fixture("owner_take23"), Candidate);
        Log("take 22", a);
        Log("take 23", b);
        Assert.InRange(Math.Abs(a.Size - b.Size), 0, 1.2);
    }

    // ------------------------------------------------------------------
    // The owner's graph lines: the saved F2 and F3 series (what the take's graphs draw), not just the cards. A median
    // card can hide a take whose line drops by hundreds of Hz for a few seconds at a time (v0.4.0 on take 23).
    // The references are Praat's, on the same moments, at fixed ceilings of 5500 and 6000 Hz (the right bracket for this
    // voice, OWNER-TAKES.md): Fixtures/owner_take*.praat.tsv, made by tools/ceiling-bench/praat-tracks.praat.
    // ------------------------------------------------------------------

    /// <summary>
    /// Praat's medians of one 3 s piece of a take (from <c>PieceSeconds × Piece</c>), over the moments the app's F3 line
    /// has a point there, at the two ceilings of the band the piece is checked against: 5500 and 6000 Hz (the right
    /// bracket for this voice, OWNER-TAKES.md), or 4500 and 5000 Hz for the low voice of take 24 (below). Measured
    /// 2026-10-18 with Praat 7.0.02 (<c>tools/praat/Praat.exe</c>): <c>To Formant (burg)</c>, 5 formants, 25 ms window,
    /// 50 Hz pre-emphasis, read at each graph point's time (the 10 ms rows of <c>owner_take*.praat.tsv</c>,
    /// <c>tools/ceiling-bench/praat-tracks.praat</c>), at the graph points of the plateau scoring (takes 22 and 23) or of
    /// <see cref="CeilingScoring.Steady"/> (take 24); the points' moments depend on the ceiling only through the F1 gate
    /// (Smoothness drops 3 of 629 on take 22).
    /// </summary>
    /// <param name="Checked">False: listed for the record, not asserted (an ambiguous start, or a stretch the owner hasn't ruled on).</param>
    public sealed record PieceReference(int Piece, double F2Lower, double F3Lower, double F2Upper, double F3Upper, bool Checked = true);

    internal const double PieceSeconds = 3;

    /// <summary>Pieces with fewer graph points are skipped (the take's last, partial piece).</summary>
    internal const int MinPiecePoints = 50;

    private static readonly Dictionary<string, PieceReference[]> PraatPieces = new()
    {
        ["owner_take22"] =
        [
            new(0, 1701, 2779, 1753, 2821), // 0-3 s, 62 points
            new(1, 1858, 2978, 1852, 2989), // 3-6 s, 82 points
            new(2, 1994, 2910, 1994, 2918), // 6-9 s, 101 points
            new(3, 1652, 2880, 1719, 2878), // 9-12 s, 160 points
            new(4, 1691, 2893, 1908, 2912), // 12-15 s, 115 points
            new(5, 1573, 2881, 1567, 2898), // 15-18 s, 109 points
        ],
        ["owner_take23"] =
        [
            new(0, 1616, 2707, 1652, 2755), // 0-3 s, 70 points
            new(1, 1835, 2807, 1820, 2835), // 3-6 s, 118 points
            new(2, 1597, 2784, 1663, 2897), // 6-9 s, 124 points
            new(3, 1837, 2729, 2031, 2976), // 9-12 s, 103 points
            new(4, 1582, 2774, 1655, 2863), // 12-15 s, 136 points
            new(5, 1784, 2842, 1838, 2905), // 15-18 s, 108 points
            new(6, 1982, 2854, 2015, 2958), // 18-21 s, 94 points
            new(7, 1209, 2670, 1393, 2776), // 21-24 s, 131 points
            new(8, 1566, 2796, 1589, 2842), // 24-27 s, 89 points
            new(9, 1581, 2849, 1566, 2911), // 27-30 s, 63 points
            new(10, 1674, 2667, 1751, 2746), // 30-33 s, 136 points
        ],
        // Take 24: a high voice for 12 s, then a low one whose spectrum has six resonances below 6 kHz (about 550, 1000, 2250,
        // 2750, 4400 and 5800 Hz). 4500–5500 Hz read the first five (F3 2250, F4 2750: a 20 cm tract by the formula); 6000 Hz
        // drops the 1000 Hz one and reads F3 2800, F4 4300: the same tract as the high voice (F3–F5 agree). The owner's rule
        // (HIGH-PITCH-PLAN §1): size must not depend on pitch, the low voice is the same instrument, so it is pinned to Praat's
        // 6000 Hz reading (OWNER-TAKES.md). An acceptance test: red until the analysis meets it.
        ["owner_take24"] =
        [
            new(0, 1973, 2762, 2158, 2858, Checked: false), // 0-3 s, 102 points: no plateau in Praat's sweep either
            new(1, 1691, 2829, 1723, 2825), // 3-6 s, 115 points
            new(2, 1776, 2812, 1962, 2899), // 6-9 s, 137 points
            new(3, 1686, 2737, 1722, 2810), // 9-12 s, 151 points
            new(4, 2025, 2839, 2025, 2839), // 12-15 s, 173 points: the low voice, at Praat's 6000 Hz reading from here on
            new(5, 2258, 2704, 2258, 2704), // 15-18 s, 102 points
            new(6, 1917, 2899, 1917, 2899), // 18-21 s, 80 points
            new(7, 2374, 2854, 2374, 2854), // 21-24 s, 172 points
            new(9, 2165, 2958, 2165, 2958), // 27-30 s, 68 points (24-27 s is a pause)
            new(10, 1942, 2759, 1942, 2759), // 30-33 s, 73 points
        ],
        // Take 25: the owner feminine (0-15 and 24-33 s, 214-271 Hz) and masculine (15-24 s, 82-133 Hz) through a PC microphone.
        // Praat's sweep has no plateau on it (F3 at 5000 / 5500 / 6000 Hz differ by 250-300 Hz in every feminine piece). The
        // feminine pieces are one voice and must read alike; the fixed 5500 Hz does (F3 2950-3100 Hz, 14.6-15.1 cm) while the
        // chooser dropped to 5000 Hz at 3-9 s (F3 2617-2703, 16-17 cm): pinned to Praat's 5500-6000 Hz band (OWNER-TAKES.md), an
        // acceptance test. The masculine pieces are listed, not checked: the owner hasn't ruled on them.
        ["owner_take25"] =
        [
            new(0, 1549, 2816, 1836, 3246), // 0-3 s
            new(1, 1687, 2973, 2295, 3264), // 3-6 s
            new(2, 1616, 2951, 1994, 3166), // 6-9 s
            new(3, 1795, 2988, 2126, 3180), // 9-12 s
            new(4, 1738, 3121, 1732, 3226), // 12-15 s
            new(5, 1464, 2600, 1569, 2821, Checked: false), // 15-18 s, masculine
            new(7, 1444, 2809, 1781, 3043, Checked: false), // 21-24 s, masculine
            new(8, 1280, 2974, 1277, 3170), // 24-27 s
            new(9, 1376, 3099, 1474, 3296), // 27-30 s
        ],
    };

    /// <summary>
    /// How far a piece's median may sit outside Praat's band. Measured 2026-10-18 (outside the band, worst piece of
    /// takes 22 and 23): the plateau scoring F2 −72 Hz, F3 −140 Hz; Smoothness (v0.4.0's choice) F2 −524 Hz, F3 −938 Hz,
    /// with 2 of 6 pieces of take 22 and 8 of 11 of take 23 beyond these bounds. Take 24 with the plateau scoring: 4 of
    /// the 8 checked pieces beyond (F3 up to +372 Hz, the other reading); <see cref="CeilingScoring.Steady"/> within
    /// F2 −75 Hz, F3 −116 Hz.
    /// </summary>
    private const double PieceF2ToleranceHz = 150, PieceF3ToleranceHz = 200;

    /// <summary>
    /// The SD of a take's F3 piece medians (population SD over its full pieces). Measured 2026-10-18 on take 22 / 23:
    /// the plateau scoring 81 / 80 Hz; Smoothness 315 / 324 Hz (v0.4.0 with its centred resampling: 392 Hz on take 23);
    /// v0.3.0 69 Hz.
    /// </summary>
    private const double MaxPieceF3SdHz = 150;

    /// <summary>
    /// The share of graph points whose F3 sits more than <see cref="StrayF3Hz"/> from Praat at the nearer of 5500 and
    /// 6000 Hz on the same moment (the nearer, because the right ceiling moves between the two within a take and
    /// Praat's own two readings differ by more than that on 17 / 21 % of the points). Measured 2026-10-18 on take 22 / 23:
    /// the plateau scoring 5.7 / 10.3 %; Smoothness 18.5 / 43.2 %.
    /// </summary>
    private const double MaxStrayF3Share = 0.15, StrayF3Hz = 300;

    /// <summary>One point of the F2 and F3 lines: its 10 ms row (time × 100) and values.</summary>
    internal readonly record struct GraphPoint(int Row, double F2, double F3, double Size = double.NaN);

    /// <summary>
    /// The take's saved F2 and F3 series with the given scoring: what <c>new AcousticsAnalysisEngine().AnalyzeAsync</c>
    /// stores for the graphs (<c>FrameSeriesBuilder.ForTake</c> on the measured tracks), which uses the app's default scoring; <see cref="TheAppsDefaultScoringMeetsTheHighPitchBar"/>
    /// checks the two agree.
    /// </summary>
    private static List<GraphPoint> GraphLines(Sound sound, CeilingScoring scoring) =>
        GraphLines(FrameSeriesBuilder.ForTake(AcousticsAnalysisEngine.MeasureTracks(sound, RawAnalysisAssembler.LoudOutlierFraction, null, FormantCeilingMethod.Path, scoring)));

    internal static List<GraphPoint> GraphLines(TakeSeries series)
    {
        var f2 = series.Series[TakeSeries.F2];
        var f3 = series.Series[TakeSeries.F3];
        Assert.Equal(f2.T, f3.T);
        Assert.Equal(0.01, f3.StepS);
        var sizes = new Dictionary<int, double>();
        if (series.Series.TryGetValue(TakeSeries.Size, out var size))
            for (var i = 0; i < size.T.Count; i++)
                if (size.Values[i] is { } v) sizes[(int)Math.Round(size.T[i] * 100)] = v;
        var points = new List<GraphPoint>(f3.T.Count);
        for (var i = 0; i < f3.T.Count; i++)
            if (f2.Values[i] is { } v2 && f3.Values[i] is { } v3)
            {
                var row = (int)Math.Round(f3.T[i] * 100);
                points.Add(new GraphPoint(row, v2, v3, sizes.TryGetValue(row, out var cm) ? cm : double.NaN));
            }
        return points;
    }

    /// <summary>The take's full pieces: piece number and its points.</summary>
    internal static IEnumerable<(int Piece, List<GraphPoint> Points)> Pieces(List<GraphPoint> points) =>
        points.GroupBy(p => (int)(p.Row / (PieceSeconds * 100)))
            .Where(g => g.Count() >= MinPiecePoints)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.ToList()));

    internal static double Median(IEnumerable<double> values)
    {
        var v = values.Order().ToArray();
        return v.Length % 2 == 1 ? v[v.Length / 2] : (v[v.Length / 2 - 1] + v[v.Length / 2]) / 2;
    }

    /// <summary>Praat's F3 at 5500 and 6000 Hz on every 10 ms row of the take (<c>Fixtures/&lt;take&gt;.praat.tsv</c>).</summary>
    private static Dictionary<int, (double F3At5500, double F3At6000)> PraatF3Rows(string name)
    {
        var rows = new Dictionary<int, (double, double)>();
        string[]? header = null;
        foreach (var line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".praat.tsv")))
        {
            var c = line.Split('\t');
            if (header is null)
            {
                header = c;
                continue;
            }
            double Parse(string column) => double.Parse(c[Array.IndexOf(header, column)], System.Globalization.CultureInfo.InvariantCulture);
            rows[(int)Math.Round(Parse("t") * 100)] = (Parse("f3_5500"), Parse("f3_6000"));
        }
        return rows;
    }

    /// <summary>The takes of one voice at the 5500–6000 Hz ceilings (the per-point and whole-take tests).</summary>
    public static TheoryData<string> OwnerTakes => ["owner_take22", "owner_take23"];

    /// <summary>The takes with a per-piece reference table (<see cref="PraatPieces"/>).</summary>
    public static TheoryData<string> OwnerTakesWithPieces => ["owner_take22", "owner_take23", "owner_take24", "owner_take25"];

    /// <summary>
    /// Piece by piece, the F2 and F3 lines stay with Praat at the right ceiling: every 3 s piece's median F2 and F3
    /// within <see cref="PieceF2ToleranceHz"/> / <see cref="PieceF3ToleranceHz"/> of Praat's band there (5500–6000 Hz,
    /// or 4500–5000 Hz for take 24's low voice).
    /// </summary>
    [Theory]
    [MemberData(nameof(OwnerTakesWithPieces))]
    public void TheOwnersGraphLinesFollowPraatPieceByPiece(string name)
    {
        var pieces = Pieces(GraphLines(Fixture(name), Candidate)).ToList();
        var references = PraatPieces[name];
        Assert.Equal(references.Select(r => r.Piece), pieces.Select(p => p.Piece));
        var failures = new List<string>();
        foreach (var (piece, points) in pieces)
        {
            var r = references.Single(x => x.Piece == piece);
            if (!r.Checked) continue;
            var f2 = Median(points.Select(p => p.F2));
            var f3 = Median(points.Select(p => p.F3));
            var line = $"{name} {piece * PieceSeconds:F0}-{(piece + 1) * PieceSeconds:F0} s ({points.Count} points): F2 {f2:F0} (Praat {r.F2Lower:F0}/{r.F2Upper:F0}), F3 {f3:F0} (Praat {r.F3Lower:F0}/{r.F3Upper:F0})";
            output.WriteLine(line);
            if (f2 < Math.Min(r.F2Lower, r.F2Upper) - PieceF2ToleranceHz || f2 > Math.Max(r.F2Lower, r.F2Upper) + PieceF2ToleranceHz
                || f3 < Math.Min(r.F3Lower, r.F3Upper) - PieceF3ToleranceHz || f3 > Math.Max(r.F3Lower, r.F3Upper) + PieceF3ToleranceHz)
                failures.Add(line);
        }
        Assert.True(failures.Count == 0, "pieces off Praat's band:\n" + string.Join("\n", failures));
    }

    /// <summary>The F3 line holds its level across the take: the SD of its piece medians under <see cref="MaxPieceF3SdHz"/>.</summary>
    [Theory]
    [MemberData(nameof(OwnerTakes))]
    public void TheOwnersF3LineHoldsItsLevelAcrossTheTake(string name)
    {
        var medians = Pieces(GraphLines(Fixture(name), Candidate)).Select(p => Median(p.Points.Select(x => x.F3))).ToArray();
        var mean = medians.Average();
        var sd = Math.Sqrt(medians.Sum(m => (m - mean) * (m - mean)) / medians.Length);
        output.WriteLine($"{name}: F3 piece medians {string.Join(" ", medians.Select(m => m.ToString("F0")))}; SD {sd:F0} Hz");
        Assert.InRange(sd, 0, MaxPieceF3SdHz);
    }

    /// <summary>
    /// Take 24 (OWNER-TAKES.md): the owner's high voice for 12 s, then their low voice (about 80 Hz) to 54 s. The low
    /// voice has two self-consistent readings (F3 near 2250 Hz at 4500–5500 Hz, near 2800 Hz at 6000–6500 Hz), and the
    /// plateau scoring hopped between them every second or so, so the F3 line and the size jumped by 500 Hz and 6 cm
    /// (HANDOFF §8x). Each voice must hold one reading: the SD of the F3 line's 3 s piece medians within each voice
    /// under <see cref="MaxPieceF3SdWithinAVoiceHz"/>. The first 3 s of the take are left out (Praat has no plateau
    /// there either: F3 2268 / 2466 / 2766 / 2851 Hz at 4500–6000 Hz), and so are the low voice's first 3 s, which
    /// continue the high voice's reading across the pause until the low voice's own evidence turns (12–15 s, checked
    /// against Praat at 5500–6000 Hz in <see cref="PraatPieces"/>). Measured 2026-10-18: the plateau scoring 48 / 165 Hz
    /// (high / low voice); <see cref="CeilingScoring.Steady"/> 47 / 55 Hz.
    /// <para>
    /// And the owner's rule (HIGH-PITCH-PLAN §1): size must not depend on pitch. The low voice is the same instrument as
    /// the high one, so the median of its size points must lie within <see cref="MaxSizeDifferenceBetweenVoicesCm"/> of the
    /// high voice's (Praat at 6000 Hz: 13.3–14.9 against 13.2–13.8 cm). Measured 2026-10-18: <see cref="CeilingScoring.Steady"/>
    /// reads the low voice 20.6 cm against 15.0 (the 4500–5500 Hz reading): an acceptance test, red until met.
    /// </para>
    /// </summary>
    [Fact]
    public void TheOwnersTake24HoldsOneReadingPerVoice()
    {
        var pieces = Pieces(GraphLines(Fixture("owner_take24"), Candidate)).ToList();
        var sizes = new Dictionary<string, double>();
        foreach (var (label, from, to) in new[] { ("high voice", 3, 12), ("low voice", 15, 33) })
        {
            var inVoice = pieces.Where(p => p.Piece * PieceSeconds >= from && (p.Piece + 1) * PieceSeconds <= to).ToList();
            var medians = inVoice.Select(p => Median(p.Points.Select(x => x.F3))).ToArray();
            var mean = medians.Average();
            var sd = Math.Sqrt(medians.Sum(m => (m - mean) * (m - mean)) / medians.Length);
            sizes[label] = Median(inVoice.SelectMany(p => p.Points).Select(x => x.Size).Where(double.IsFinite));
            output.WriteLine($"take 24 {label} ({from}-{to} s): F3 piece medians {string.Join(" ", medians.Select(m => m.ToString("F0")))}; SD {sd:F0} Hz; size {sizes[label]:F1} cm");
            Assert.True(medians.Length >= 3, $"{label}: only {medians.Length} full pieces");
            Assert.InRange(sd, 0, MaxPieceF3SdWithinAVoiceHz);
        }
        Assert.InRange(Math.Abs(sizes["low voice"] - sizes["high voice"]), 0, MaxSizeDifferenceBetweenVoicesCm);
    }

    private const double MaxPieceF3SdWithinAVoiceHz = 100, MaxSizeDifferenceBetweenVoicesCm = 1.5;

    /// <summary>Few graph points stray from Praat: under <see cref="MaxStrayF3Share"/> of the F3 points beyond <see cref="StrayF3Hz"/>.</summary>
    [Theory]
    [MemberData(nameof(OwnerTakes))]
    public void FewOfTheOwnersF3PointsStrayFromPraat(string name)
    {
        var points = GraphLines(Fixture(name), Candidate);
        var praat = PraatF3Rows(name);
        var stray = 0;
        foreach (var p in points)
        {
            var (at5500, at6000) = praat[p.Row];
            var nearer = Math.Min(double.IsNaN(at5500) ? double.PositiveInfinity : Math.Abs(p.F3 - at5500),
                double.IsNaN(at6000) ? double.PositiveInfinity : Math.Abs(p.F3 - at6000));
            if (nearer > StrayF3Hz) stray++;
        }
        var share = (double)stray / points.Count;
        output.WriteLine($"{name}: {stray} of {points.Count} F3 points ({share:P1}) more than {StrayF3Hz} Hz from Praat at the nearer of 5500/6000 Hz");
        Assert.InRange(share, 0, MaxStrayF3Share);
    }

    /// <summary>The bar for the app itself: the scoring the providers use by default is the one the tests above passed with.</summary>
    [Fact]
    public async Task TheAppsDefaultScoringMeetsTheHighPitchBar()
    {
        Assert.Equal(Candidate, CeilingScoring.Default);
        Assert.Equal(CeilingScoring.Default, new CeilingPathFinder().Scoring);
        var sound = Fixture("owner_take23");
        var c = Cards(sound, CeilingScoring.Default);
        Assert.InRange(c.Ceiling, 5500, 6500);
        Assert.InRange(c.Size, 13.6, 15.0);
        // The graph-line tests read the series the way the saved analysis builds them.
        var saved = await new AcousticsAnalysisEngine().AnalyzeAsync(sound);
        Assert.Equal(GraphLines(sound, CeilingScoring.Default), GraphLines(saved.Series!));
    }
}
