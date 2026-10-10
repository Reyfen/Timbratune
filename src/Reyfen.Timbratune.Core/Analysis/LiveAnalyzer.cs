using Reyfen.Timbratune.Acoustics.Numerics;
using System.Collections.Concurrent;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Spectral;
using Reyfen.Timbratune.Acoustics.Streaming;
using Reyfen.Timbratune.Acoustics.Voice;
using Reyfen.Timbratune.Core.Diagnostics;
using Reyfen.Timbratune.Core.Models;

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

    /// <summary>
    /// The pitch frames and phrases so far (what the live graphs draw): the result's detail
    /// after a full update, or just this after a quick one (<see cref="Result"/> null then).
    /// </summary>
    public RecordingDetail? Contour { get; init; }
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
    /// <param name="full">
    /// False = a quick update for the live graphs: frames, series, pitch contour and phrases,
    /// without assembling and post-processing the whole take (statistics, formant and weight
    /// selection, trends) — that is only needed when the take's cards are refreshed.
    /// The last update (<paramref name="final"/>) is always full.
    /// </param>
    public LiveSnapshot Update(bool final = false, int? length = null, bool full = true)
    {
        lock (_updateGate)
        {
            var view = length is { } n ? _signal.View(n) : _signal.View();
            if (view.Duration < 0.1) return LiveSnapshot.Empty with { Elapsed = view.Duration, IsFinal = final };

            Parallel.Invoke(Parallelism.Options,
                () => { using (Timing.Measure("live.pitch")) _pitch.Update(view, final); },
                () => { using (Timing.Measure("live.hnr")) _harmonicity.Update(view, final); },
                () => { using (Timing.Measure("live.intensity")) _intensity.Update(view, final); },
                // Both ceilings share each block's segment and resampling.
                () => { using (Timing.Measure("live.formants")) LiveFormantTracker.UpdateAll([_formants5500, _formants5000], view, final); });
            if (_pitch.FrameCount == 0 || _intensity.FrameCount == 0)
                return LiveSnapshot.Empty with { Elapsed = view.Duration, IsFinal = final };

            Acoustics.Pitch.PitchContour pitch;
            using (Timing.Measure("live.pitchpath")) pitch = _pitch.Contour(view);
            var intensity = _intensity.Contour(view);
            Acoustics.Pitch.HarmonicityContour harmonicity;
            using (Timing.Measure("live.hnrpath")) harmonicity = _harmonicity.HarmonicityContour(view);
            var sound = view.AsSound();
            var lastFrame = pitch.FrameCount > 0 ? pitch.Grid.IndexToX(pitch.FrameCount - 1) : 0;
            using (Timing.Measure("live.pulses")) _pulses.Update(view, pitch, lastFrame - PulseStability, final);
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
            AnalysisResult? result = null;
            RecordingDetail contour;
            if (full || final)
            {
                RawAnalysis raw;
                using (Timing.Measure("live.assemble")) raw = RawAnalysisAssembler.Assemble(tracks);
                using (Timing.Measure("live.postprocess")) result = AnalysisPostProcessor.Process(raw, _registerFloorHz);
                contour = result.Detail;
            }
            else
            {
                using (Timing.Measure("live.contour"))
                    contour = AnalysisPostProcessor.AnalyzeRegister(RawAnalysisAssembler.Contour(pitch), tracks.Sounding, view.Duration,
                        _registerFloorHz).Detail;
            }
            LiveSeries series;
            using (Timing.Measure("live.series")) series = BuildSeries(tracks, harmonicity, WeightRow);
            return new LiveSnapshot(view.Duration, result, series, final) { Contour = contour };
        }
    }

    /// <summary>
    /// The series lists, two sets used in turn: each update refills one, so the previous
    /// snapshot's lists stay intact while the next is built, and no whole-take lists are
    /// allocated per update (they were a large share of the live analysis' garbage).
    /// </summary>
    private readonly FrameSeriesBuffers[] _series = [new(), new()];
    private int _seriesTurn;

    private LiveSeries BuildSeries(FrameTracks t, Acoustics.Pitch.HarmonicityContour harmonicity,
        Func<double, double, RawAnalysis.WeightRow?> weightRow)
    {
        var b = _series[_seriesTurn ^= 1];
        FrameSeriesBuilder.Build(t, harmonicity, weightRow, _pulses.StretchJitter, b);
        return new LiveSeries { Loudness = b.Loudness, Hnr = b.Hnr, F2 = b.F2, F3 = b.F3, Weight = b.Weight, Jitter = b.Jitter };
    }
}
