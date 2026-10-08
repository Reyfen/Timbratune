using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Voice;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// Lists the per-frame series are written into. The live analysis keeps two sets and
/// refills them in turn (no whole-take lists allocated per update); the full analysis
/// uses one set once.
/// </summary>
public sealed class FrameSeriesBuffers
{
    public readonly List<TimedValue> Loudness = [], Hnr = [], F2 = [], F3 = [], Weight = [], Jitter = [];
    /// <summary>F1 at the same times as F2/F3; null = not collected (the live graphs don't show it).</summary>
    public List<TimedValue>? F1;
    internal readonly List<(double T, double F0)> Voiced = [];
    internal readonly List<double> LoudTimes = [];
    internal RawAnalysis.WeightRow?[] Rows = [];
}

/// <summary>
/// The per-frame series as the live graphs and the saved take's lists show them. Shared by
/// <see cref="LiveAnalyzer"/> and <see cref="AcousticsAnalysisEngine"/>, so both use the
/// same definitions:
/// loudness on every intensity frame, HNR on voiced frames, F1–F3 on loud voiced frames
/// with a plausible F1 (5500 Hz ceiling), weight (corrected H1*–A3*) on every measurable
/// voiced frame, and jitter per voiced stretch.
/// </summary>
public static class FrameSeriesBuilder
{
    /// <param name="stretchJitter">Per voiced stretch: its end time and local jitter (fraction; NaN = too few pulses).</param>
    public static void Build(FrameTracks t, HarmonicityContour harmonicity, Func<double, double, RawAnalysis.WeightRow?> weightRow,
        IReadOnlyList<(double End, double Jitter)> stretchJitter, FrameSeriesBuffers b)
    {
        var loudness = b.Loudness;
        loudness.Clear();
        for (var i = 0; i < t.Intensity.Db.Count; i++) loudness.Add(new(t.Intensity.Grid.IndexToX(i), t.Intensity.Db[i]));

        var hnr = b.Hnr;
        hnr.Clear();
        for (var i = 0; i < harmonicity.Db.Count; i++)
            if (harmonicity.Db[i] != HarmonicityContour.Unvoiced) hnr.Add(new(harmonicity.Grid.IndexToX(i), harmonicity.Db[i]));

        var voiced = RawAnalysisAssembler.VoicedFrames(t.Pitch, b.Voiced);
        var f1 = b.F1;
        var f2 = b.F2;
        var f3 = b.F3;
        f1?.Clear();
        f2.Clear();
        f3.Clear();
        foreach (var time in RawAnalysisAssembler.LoudVoicedTimes(voiced, t.Intensity, b.LoudTimes))
        {
            double v1 = t.Formants5500.ValueAtTime(1, time), v2 = t.Formants5500.ValueAtTime(2, time), v3 = t.Formants5500.ValueAtTime(3, time);
            if (!(v1 >= 250 && v1 <= 1000 && v2 > 0 && v3 > 0)) continue;
            f1?.Add(new(time, v1));
            f2.Add(new(time, v2));
            f3.Add(new(time, v3));
        }

        if (b.Rows.Length < voiced.Count) b.Rows = new RawAnalysis.WeightRow?[Math.Max(voiced.Count, 2 * b.Rows.Length)];
        var rows = b.Rows;
        Parallel.For(0, voiced.Count, Parallelism.Options, k => rows[k] = weightRow(voiced[k].T, voiced[k].F0));
        var weight = b.Weight;
        weight.Clear();
        for (var k = 0; k < voiced.Count; k++)
        {
            if (rows[k] is not { } w) continue;
            var value = AnalysisPostProcessor.CorrectedH1A3(w, t.SamplingFrequency);
            if (double.IsFinite(value)) weight.Add(new(w.T, value));
        }

        var jitter = b.Jitter;
        jitter.Clear();
        foreach (var s in stretchJitter)
            if (double.IsFinite(s.Jitter)) jitter.Add(new TimedValue(s.End, s.Jitter * 100));
    }

    /// <summary>
    /// Local jitter per voiced stretch of a whole take, from all its pulses: each stretch
    /// uses the pulses inside it (what the live analysis measures stretch by stretch).
    /// </summary>
    public static List<(double End, double Jitter)> StretchJitter(PitchContour pitch, IReadOnlyList<double> pulses)
    {
        var result = new List<(double, double)>();
        var inside = new List<double>();
        var p = 0;
        foreach (var (left, right) in PulseDetector.VoicedStretches(pitch))
        {
            while (p < pulses.Count && pulses[p] < left) p++;
            inside.Clear();
            for (var q = p; q < pulses.Count && pulses[q] <= right; q++) inside.Add(pulses[q]);
            result.Add((right, VoiceReport.JitterLocal(inside)));
        }
        return result;
    }
    /// <summary>
    /// The saved take's series from the full analysis' tracks (rounded as the saved contour
    /// is: times to 1 ms, Hz to 0.1, dB to 0.01, % to 0.001).
    /// </summary>
    public static TakeSeries ForTake(FrameTracks t)
    {
        var harmonicity = t.Harmonicity ?? throw new ArgumentException("The tracks have no harmonicity.", nameof(t));
        var b = new FrameSeriesBuffers { F1 = [] };
        Build(t, harmonicity, t.WeightRow, StretchJitter(t.Pitch, t.Pulses), b);

        var pitchStep = t.Pitch.Grid.Step;
        var pitch = new FrameSeries { Unit = "Hz", StepS = pitchStep, Description = "every pitch frame; null = unvoiced" };
        for (var i = 0; i < t.Pitch.FrameCount; i++)
        {
            var hz = t.Pitch.ValueInFrame(i);
            pitch.T.Add(Math.Round(t.Pitch.Grid.IndexToX(i), 3));
            pitch.Values.Add(t.Pitch.IsVoiced(i) && double.IsFinite(hz) ? Math.Round(hz, 1) : null);
        }

        return new TakeSeries
        {
            Series =
            {
                [TakeSeries.Pitch] = pitch,
                [TakeSeries.Loudness] = Series(b.Loudness, "dB", t.Intensity.Grid.Step, 2, "every intensity frame"),
                [TakeSeries.Hnr] = Series(b.Hnr, "dB", harmonicity.Grid.Step, 2, "voiced harmonicity frames"),
                [TakeSeries.F1] = Series(b.F1, "Hz", pitchStep, 1, FormantFrames),
                [TakeSeries.F2] = Series(b.F2, "Hz", pitchStep, 1, FormantFrames),
                [TakeSeries.F3] = Series(b.F3, "Hz", pitchStep, 1, FormantFrames),
                [TakeSeries.Weight] = Series(b.Weight, "dB", pitchStep, 2, "corrected H1*-A3* on measurable voiced pitch frames"),
                [TakeSeries.Jitter] = Series(b.Jitter, "%", null, 3, "local jitter per voiced stretch, at the stretch end"),
            },
        };
    }

    private const string FormantFrames = "voiced pitch frames within 10 dB of the loudest frame, with F1 in 250-1000 Hz (5500 Hz ceiling)";

    private static FrameSeries Series(List<TimedValue> points, string unit, double? step, int digits, string description)
    {
        var series = new FrameSeries { Unit = unit, StepS = step, Description = description };
        series.T.Capacity = series.Values.Capacity = points.Count;
        foreach (var p in points)
        {
            series.T.Add(Math.Round(p.T, 3));
            series.Values.Add(double.IsFinite(p.Value) ? Math.Round(p.Value, digits) : null);
        }
        return series;
    }
}
