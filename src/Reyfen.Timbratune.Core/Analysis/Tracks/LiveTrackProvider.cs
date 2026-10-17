using System.Collections.Concurrent;
using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Spectral;
using Reyfen.Timbratune.Acoustics.Streaming;
using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Reyfen.Timbratune.Core.Diagnostics;

namespace Reyfen.Timbratune.Core.Analysis.Tracks;

/// <summary>
/// The tracks while recording, from the live trackers (the same frame kernels as the saved
/// provider, so live ≈ saved by construction). Stage 1 runs under the live core limit
/// (<see cref="Parallelism.Options"/>); weight rows are memoised across updates; the voiced
/// frames and the gate are refilled lists. Not thread-safe: one update at a time.
/// </summary>
public sealed class LiveTrackProvider
{
    /// <summary>Voiced stretches must have ended this long ago before their pulses are measured.</summary>
    private const double PulseStability = 0.3;

    private readonly LivePitchTracker _pitch;
    private readonly LivePitchTracker _harmonicity;
    private readonly LiveIntensityTracker _intensity;
    private readonly LiveFormantTracker _formants5500;
    private readonly LiveFormantTracker _formants5000;
    private readonly LiveFormantTracker[] _formants;
    private readonly LivePulseTracker _pulses = new();
    private readonly ConcurrentDictionary<long, RawAnalysis.WeightRow?> _weightCache = new();
    private readonly DerivedTracks _derived = new();
    private readonly double _loudOutlierFraction;

    public LiveTrackProvider(double samplingFrequency, double formantBlockSeconds = LiveAnalyzer.DefaultFormantBlockSeconds,
        double loudOutlierFraction = RawAnalysisAssembler.LoudOutlierFraction)
    {
        _pitch = LivePitchTracker.Autocorrelation(samplingFrequency, 0, AcousticsAnalysisEngine.PitchFloor, AcousticsAnalysisEngine.PitchCeiling);
        _harmonicity = LivePitchTracker.Harmonicity(samplingFrequency, 0.01, AcousticsAnalysisEngine.PitchFloor, 0.1, 1.0);
        _intensity = new LiveIntensityTracker(samplingFrequency, AcousticsAnalysisEngine.PitchFloor);
        _formants5500 = new LiveFormantTracker(RawAnalysisAssembler.FormantCeiling, 5, 0.025, 50, formantBlockSeconds);
        _formants5000 = new LiveFormantTracker(5000, 5, 0.025, 50, formantBlockSeconds);
        _formants = [_formants5500, _formants5000];
        _loudOutlierFraction = loudOutlierFraction;
    }

    /// <summary>See <see cref="LivePitchTracker.AlignedLength"/>.</summary>
    public int AlignedLength(int available, double samplingFrequency) => _pitch.AlignedLength(available, samplingFrequency);

    /// <summary>
    /// Analyzes the frames that have become complete and returns the tracks so far; null when
    /// there is no pitch or intensity frame yet. <paramref name="final"/> completes the last
    /// frames the way the saved analysis does (and measures the LTAS, if asked for).
    /// </summary>
    public TrackView? Update(LiveSignalView view, TrackSet set, bool final)
    {
        set = set.WithDependencies();
        if (!final) set &= ~TrackSet.Ltas; // the spectral tilt needs the whole take's spectrum

        // Stage 1: the frame trackers. Live, all of them run on every update (their state must
        // keep up with the signal), whichever tracks are asked for.
        Parallel.Invoke(Parallelism.Options,
            () => { using (Timing.Measure("live.pitch")) _pitch.Update(view, final); },
            () => { using (Timing.Measure("live.hnr")) _harmonicity.Update(view, final); },
            () => { using (Timing.Measure("live.intensity")) _intensity.Update(view, final); },
            // Both ceilings share each block's segment and resampling.
            () => { using (Timing.Measure("live.formants")) LiveFormantTracker.UpdateAll(_formants, view, final); });
        if (_pitch.FrameCount == 0 || _intensity.FrameCount == 0) return null;

        PitchContour pitch;
        using (Timing.Measure("live.pitchpath")) pitch = _pitch.Contour(view);
        var intensity = _intensity.Contour(view);
        HarmonicityContour harmonicity;
        using (Timing.Measure("live.hnrpath")) harmonicity = _harmonicity.HarmonicityContour(view);
        var sound = view.AsSound();

        // Stage 2: pulses, then the voiced frames, sounding stretches and the gate.
        var lastFrame = pitch.FrameCount > 0 ? pitch.Grid.IndexToX(pitch.FrameCount - 1) : 0;
        using (Timing.Measure("live.pulses")) _pulses.Update(view, pitch, lastFrame - PulseStability, final);
        _derived.ComputeStage2(pitch, intensity, set, _loudOutlierFraction);

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

        var track5000 = _formants5000.Track();
        var tracks = new TrackView
        {
            Tracks = set,
            Duration = view.Duration,
            SamplingFrequency = view.Grid.Step > 0 ? 1.0 / view.Grid.Step : sound.SamplingFrequency,
            Sound = sound,
            Pitch = pitch,
            Intensity = intensity,
            Harmonicity = harmonicity,
            FormantMethod = FormantCeilingMethod.Legacy,
            FormantCeilings = FormantCeilings.LegacyCandidates,
            FormantCandidates = [track5500, track5000],
            Formants = track5500,
            Ltas = set.Has(TrackSet.Ltas) ? RawAnalysisAssembler.LtasPoints(Ltas.FromSound(sound, 100)) : [],
            Pulses = _pulses.Pulses,
            StretchJitter = _pulses.StretchJitter,
            WeightRow = WeightRow,
            Derived = _derived,
        };
        // Stage 3: the derived tracks (full updates).
        if ((set & (TrackSet.VowelFormantRows | TrackSet.WeightFrames)) != 0)
            using (Timing.Measure("live.derived")) _derived.ComputeStage3(tracks, WeightRow, set);
        return tracks;
    }
}
