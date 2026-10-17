using Reyfen.Timbratune.Acoustics.Streaming;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Analysis.Tracks;
using Reyfen.Timbratune.Core.Diagnostics;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>A (time, value) point of a live timeline.</summary>
public readonly record struct TimedValue(double T, double Value);

/// <summary>
/// Per-frame series for the live timelines (values as the cards show them), one per metric of the
/// registry that measured them (empty for metrics without a live series). The lists are the live
/// analysis' pooled buffers: read them on the analysis thread, before the next update.
/// </summary>
public sealed class LiveSeries(MetricRegistry registry, IReadOnlyList<TimedValue>[] lines)
{
    public static readonly LiveSeries Empty = new(MetricRegistry.Default, []);

    public MetricRegistry Registry { get; } = registry;

    /// <summary>The series of metric <paramref name="id"/> of <see cref="Registry"/>.</summary>
    public IReadOnlyList<TimedValue> this[MetricId id] => id.Index < lines.Length ? lines[id.Index] : [];

    /// <summary>The series of a registered metric (empty when it isn't registered).</summary>
    public IReadOnlyList<TimedValue> this[MetricInfo metric] => Registry.TryGetId(metric, out var id) ? this[id] : [];
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
    private readonly LiveSignal _signal;
    private readonly LiveTrackProvider _tracks;
    private readonly double _registerFloorHz;
    private readonly MetricRegistry _registry;
    private readonly object _updateGate = new();

    /// <summary>
    /// How much new audio (s) the formant trackers wait for before analyzing the next block.
    /// Small blocks let resonance and weight arrive steadily and soon (latency ≈ block +
    /// the 0.25 s edge margin); each block re-analyzes its margins, so it costs a bit more.
    /// </summary>
    public const double DefaultFormantBlockSeconds = 0.5;

    public LiveAnalyzer(double samplingFrequency = 44100, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        double formantBlockSeconds = DefaultFormantBlockSeconds, MetricRegistry? registry = null)
    {
        SamplingFrequency = samplingFrequency;
        _registerFloorHz = registerFloorHz;
        _registry = registry ?? MetricRegistry.Default;
        _series = [new(_registry.Count), new(_registry.Count)];
        _liveSeries = [new(_registry, _series[0].Lines), new(_registry, _series[1].Lines)];
        _signal = new LiveSignal(samplingFrequency);
        _tracks = new LiveTrackProvider(samplingFrequency, formantBlockSeconds);
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
    public int AlignedLength(int available) => _tracks.AlignedLength(available, SamplingFrequency);

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

            // A quick update needs the tracks of the graphs; a full one also the derived tracks, the last one the LTAS.
            if (_tracks.Update(view, full || final ? TrackSet.All : TrackSet.Live, final) is not { } tracks)
                return LiveSnapshot.Empty with { Elapsed = view.Duration, IsFinal = final };
            AnalysisResult? result = null;
            RecordingDetail contour;
            if (full || final)
            {
                RawAnalysis raw;
                using (Timing.Measure("live.assemble")) raw = RawAnalysisAssembler.Assemble(tracks, _registry);
                using (Timing.Measure("live.postprocess")) result = AnalysisPostProcessor.Process(raw, _registerFloorHz, _registry);
                contour = result.Detail;
            }
            else
            {
                using (Timing.Measure("live.contour"))
                    contour = Metrics.Phrasing.PhrasingModule.AnalyzeRegister(RawAnalysisAssembler.Contour(tracks.Pitch), tracks.Derived.Sounding, view.Duration,
                        _registerFloorHz).Detail;
            }
            LiveSeries series;
            using (Timing.Measure("live.series")) series = BuildSeries(tracks);
            return new LiveSnapshot(view.Duration, result, series, final) { Contour = contour };
        }
    }

    /// <summary>
    /// The series lists, two sets used in turn: each update refills one, so the previous
    /// snapshot's lists stay intact while the next is built, and no whole-take lists are
    /// allocated per update (they were a large share of the live analysis' garbage).
    /// </summary>
    private readonly FrameSeriesBuffers[] _series;
    private readonly LiveSeries[] _liveSeries;
    private int _seriesTurn;

    private LiveSeries BuildSeries(in TrackView t)
    {
        var turn = _seriesTurn ^= 1;
        FrameSeriesBuilder.Build(t, _series[turn], _registry);
        return _liveSeries[turn];
    }
}
