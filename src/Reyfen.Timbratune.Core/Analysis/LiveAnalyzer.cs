using System.Collections.Concurrent;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Spectral;
using Reyfen.Timbratune.Acoustics.Streaming;
using Reyfen.Timbratune.Acoustics.Voice;

namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>A (time, value) point of a live timeline.</summary>
public readonly record struct TimedValue(double T, double Value);

/// <summary>Per-frame series for the live timelines (values as the cards show them).</summary>
public sealed record LiveSeries
{
    public static readonly LiveSeries Empty = new();

    /// <summary>Intensity per frame (dB).</summary>
    public IReadOnlyList<TimedValue> Loudness { get; init; } = [];
    /// <summary>HNR per voiced frame (dB).</summary>
    public IReadOnlyList<TimedValue> Hnr { get; init; } = [];
    /// <summary>F2 / F3 on loud voiced frames with a plausible F1 (Hz).</summary>
    public IReadOnlyList<TimedValue> F2 { get; init; } = [];
    public IReadOnlyList<TimedValue> F3 { get; init; } = [];
    /// <summary>Corrected H1*–A3* per measurable voiced frame (dB).</summary>
    public IReadOnlyList<TimedValue> Weight { get; init; } = [];
    /// <summary>Local jitter (%) per finished voiced stretch, at the stretch end.</summary>
    public IReadOnlyList<TimedValue> Jitter { get; init; } = [];
}

/// <summary>What the live analysis knows at one moment.</summary>
public sealed record LiveSnapshot(double Elapsed, AnalysisResult? Result, LiveSeries Series, bool IsFinal)
{
    public static readonly LiveSnapshot Empty = new(0, null, LiveSeries.Empty, false);
}

/// <summary>
/// Analysis while recording. Samples are appended as they are captured; each
/// <see cref="Update"/> analyzes the frames that have become complete (the same
/// frame kernels as the full analysis), then assembles and post-processes the
/// take so far exactly like <see cref="AcousticsAnalysisEngine"/> does for a
/// whole file. After the last <c>Update(final: true)</c> the result is the full
/// analysis of the recording, up to frame placement (see LiveAnalysisTests).
/// Single-channel only; <see cref="Update"/> must not run concurrently with itself.
/// </summary>
public sealed class LiveAnalyzer
{
    /// <summary>Voiced stretches must have ended this long ago before their pulses are measured.</summary>
    private const double PulseStability = 0.3;

    private readonly LiveSignal _signal;
    private readonly LivePitchTracker _pitch;
    private readonly LivePitchTracker _harmonicity;
    private readonly LiveIntensityTracker _intensity;
    private readonly LiveFormantTracker _formants5500;
    private readonly LiveFormantTracker _formants5000;
    private readonly LivePulseTracker _pulses = new();
    private readonly ConcurrentDictionary<long, RawAnalysis.WeightRow?> _weightCache = new();
    private readonly double _registerFloorHz;
    private readonly object _updateGate = new();

    /// <summary>
    /// How much new audio (s) the formant trackers wait for before analyzing the next block.
    /// Small blocks let resonance and weight arrive steadily and soon (latency ≈ block +
    /// the 0.25 s edge margin); each block re-analyzes its margins, so it costs a bit more.
    /// </summary>
    public const double DefaultFormantBlockSeconds = 0.5;

    public LiveAnalyzer(double samplingFrequency = 44100, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        double formantBlockSeconds = DefaultFormantBlockSeconds)
    {
        SamplingFrequency = samplingFrequency;
        _registerFloorHz = registerFloorHz;
        _signal = new LiveSignal(samplingFrequency);
        _pitch = LivePitchTracker.Autocorrelation(samplingFrequency, 0, AcousticsAnalysisEngine.PitchFloor, AcousticsAnalysisEngine.PitchCeiling);
        _harmonicity = LivePitchTracker.Harmonicity(samplingFrequency, 0.01, AcousticsAnalysisEngine.PitchFloor, 0.1, 1.0);
        _intensity = new LiveIntensityTracker(samplingFrequency, AcousticsAnalysisEngine.PitchFloor);
        _formants5500 = new LiveFormantTracker(RawAnalysisAssembler.FormantCeiling, 5, 0.025, 50, formantBlockSeconds);
        _formants5000 = new LiveFormantTracker(5000, 5, 0.025, 50, formantBlockSeconds);
    }

    public double SamplingFrequency { get; }

    /// <summary>Appends decoded samples (−1..1). Safe to call from the audio thread.</summary>
    public void Append(ReadOnlySpan<double> samples) => _signal.Append(samples);

    /// <summary>
    /// Appends microphone samples, quantized exactly as the recorder stores them in the
    /// WAV (PCM16), so live analysis sees the same numbers the saved take will.
    /// </summary>
    public void AppendCaptured(ReadOnlySpan<float> samples)
    {
        Span<double> converted = samples.Length <= 4096 ? stackalloc double[samples.Length] : new double[samples.Length];
        for (var i = 0; i < samples.Length; i++) converted[i] = Audio.WavWriter.ToPcm16(samples[i]) / 32768.0;
        _signal.Append(converted);
    }

    /// <summary>Samples received so far.</summary>
    public int SampleCount => _signal.Count;

    /// <summary>
    /// The length (≤ <paramref name="available"/>, at most ~10 ms shorter) to trim the saved
    /// recording to, so the full analysis of the file puts its pitch frames exactly on the
    /// live ones: then everything pitch-based (contour, statistics, register, jitter and
    /// shimmer) comes out identical live and saved.
    /// </summary>
    public int AlignedLength(int available) => _pitch.AlignedLength(available, SamplingFrequency);

    /// <summary>
    /// Analyzes everything received so far. <paramref name="final"/> = the recording has
    /// ended: completes the last frames the way the full analysis does. With
    /// <paramref name="length"/> the take is treated as ending after that many samples
    /// (see <see cref="AlignedLength"/>).
    /// </summary>
    public LiveSnapshot Update(bool final = false, int? length = null)
    {
        lock (_updateGate)
        {
            var view = length is { } n ? _signal.View(n) : _signal.View();
            if (view.Duration < 0.1) return LiveSnapshot.Empty with { Elapsed = view.Duration, IsFinal = final };

            Parallel.Invoke(
                () => _pitch.Update(view, final),
                () => _harmonicity.Update(view, final),
                () => _intensity.Update(view, final),
                () => _formants5500.Update(view, final),
                () => _formants5000.Update(view, final));
            if (_pitch.FrameCount == 0 || _intensity.FrameCount == 0)
                return LiveSnapshot.Empty with { Elapsed = view.Duration, IsFinal = final };

            var pitch = _pitch.Contour(view);
            var intensity = _intensity.Contour(view);
            var harmonicity = _harmonicity.HarmonicityContour(view);
            var sound = view.AsSound();
            var lastFrame = pitch.FrameCount > 0 ? pitch.Grid.IndexToX(pitch.FrameCount - 1) : 0;
            _pulses.Update(view, pitch, lastFrame - PulseStability, final);
            var pulses = _pulses.Pulses;

            var track5500 = _formants5500.Track();
            var covered5500 = _formants5500.CoveredUntil;
            RawAnalysis.WeightRow? WeightRow(double t, double f0)
            {
                var key = BitConverter.DoubleToInt64Bits(t);
                if (_weightCache.TryGetValue(key, out var cached)) return cached;
                // Formants must already be known around t; otherwise try again on a later update.
                if (t + 0.5 * Math.Max(0.025, 3.0 / f0) + 0.01 > covered5500 && !final) return null;
                var row = RawAnalysisAssembler.MeasureWeightRow(sound, track5500, t, f0);
                _weightCache[key] = row;
                return row;
            }

            var tracks = new FrameTracks
            {
                Duration = view.Duration,
                SamplingFrequency = view.Grid.Step > 0 ? 1.0 / view.Grid.Step : SamplingFrequency,
                Pitch = pitch,
                Intensity = intensity,
                HnrMean = harmonicity.Mean(),
                Jitter = VoiceReport.JitterLocal(pulses, 0.0001, 0.02, 1.3),
                Shimmer = VoiceReport.ShimmerLocal(pulses, sound, 0.0001, 0.02, 1.3, 1.6),
                Formants5500 = track5500,
                Formants5000 = _formants5000.Track(),
                WeightRow = WeightRow,
                // The spectral tilt needs the whole-take spectrum: only at the end.
                Ltas = final ? RawAnalysisAssembler.LtasPoints(Ltas.FromSound(sound, 100)) : [],
                Sounding = RawAnalysisAssembler.SoundingIntervals(intensity),
                Harmonicity = harmonicity,
                Pulses = pulses,
            };
            var result = AnalysisPostProcessor.Process(RawAnalysisAssembler.Assemble(tracks), _registerFloorHz);
            return new LiveSnapshot(view.Duration, result, BuildSeries(tracks, harmonicity, WeightRow), final);
        }
    }

    private LiveSeries BuildSeries(FrameTracks t, Acoustics.Pitch.HarmonicityContour harmonicity,
        Func<double, double, RawAnalysis.WeightRow?> weightRow)
    {
        var loudness = new List<TimedValue>(t.Intensity.Db.Count);
        for (var i = 0; i < t.Intensity.Db.Count; i++) loudness.Add(new(t.Intensity.Grid.IndexToX(i), t.Intensity.Db[i]));

        var hnr = new List<TimedValue>();
        for (var i = 0; i < harmonicity.Db.Count; i++)
            if (harmonicity.Db[i] != Acoustics.Pitch.HarmonicityContour.Unvoiced) hnr.Add(new(harmonicity.Grid.IndexToX(i), harmonicity.Db[i]));

        var voiced = RawAnalysisAssembler.VoicedFrames(t.Pitch);
        var f2 = new List<TimedValue>();
        var f3 = new List<TimedValue>();
        foreach (var time in RawAnalysisAssembler.LoudVoicedTimes(voiced, t.Intensity))
        {
            double v1 = t.Formants5500.ValueAtTime(1, time), v2 = t.Formants5500.ValueAtTime(2, time), v3 = t.Formants5500.ValueAtTime(3, time);
            if (!(v1 >= 250 && v1 <= 1000 && v2 > 0 && v3 > 0)) continue;
            f2.Add(new(time, v2));
            f3.Add(new(time, v3));
        }

        var rows = new RawAnalysis.WeightRow?[voiced.Count];
        Parallel.For(0, voiced.Count, k => rows[k] = weightRow(voiced[k].T, voiced[k].F0));
        var weight = new List<TimedValue>();
        foreach (var row in rows)
        {
            if (row is not { } w) continue;
            var value = AnalysisPostProcessor.CorrectedH1A3(w, t.SamplingFrequency);
            if (double.IsFinite(value)) weight.Add(new(w.T, value));
        }

        var jitter = _pulses.StretchJitter.Where(s => double.IsFinite(s.Jitter)).Select(s => new TimedValue(s.End, s.Jitter * 100)).ToList();
        return new LiveSeries { Loudness = loudness, Hnr = hnr, F2 = f2, F3 = f3, Weight = weight, Jitter = jitter };
    }
}
