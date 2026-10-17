using System.Collections.Concurrent;
using Reyfen.Timbratune.Acoustics.Formants;
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
/// <para>
/// The formant ceiling path is found again on every update over all the frames so far (the
/// piece scores are cached, so only new pieces cost anything), and the formants are read along
/// it: when more voice shows that a change of ceiling happened, the recent past is redrawn.
/// The newest end of the path keeps its ceiling unless another is clearly better
/// (<see cref="CeilingPathFinder.EndMargin"/>); the final update has no such margin, so it ends
/// on the saved analysis' path.
/// </para>
/// </summary>
public sealed class LiveTrackProvider
{
    /// <summary>Voiced stretches must have ended this long ago before their pulses are measured.</summary>
    private const double PulseStability = 0.3;

    private readonly LivePitchTracker _pitch;
    private readonly LivePitchTracker _harmonicity;
    private readonly LiveIntensityTracker _intensity;
    private readonly FormantCeilingMethod _formantMethod;
    private readonly IReadOnlyList<double> _ceilings;
    private readonly int _fallback;
    private readonly LiveFormantTracker[] _formants;
    private readonly LivePulseTracker _pulses = new();
    /// <summary>Per candidate ceiling: weight rows by frame time (a moment may change ceiling as the path is found again).</summary>
    private readonly ConcurrentDictionary<long, RawAnalysis.WeightRow?>[] _weightCache;
    private readonly DerivedTracks _derived = new();
    private readonly double _loudOutlierFraction;

    // The ceiling path, refilled by every update (Path only).
    private readonly CeilingPathFinder _finder = new();
    private readonly CeilingPath _path = new();
    private readonly IFormantFrames[] _frames;
    private readonly IFormantTrack?[] _smoothed;
    private readonly IFormantTrack[] _candidates;
    private readonly SwitchedFormantTrack _alongPath;
    private int? _pathEnd;

    public LiveTrackProvider(double samplingFrequency, double formantBlockSeconds = LiveAnalyzer.DefaultFormantBlockSeconds,
        double loudOutlierFraction = RawAnalysisAssembler.LoudOutlierFraction, FormantCeilingMethod formantMethod = FormantCeilingMethod.Path)
    {
        _pitch = LivePitchTracker.Autocorrelation(samplingFrequency, 0, AcousticsAnalysisEngine.PitchFloor, AcousticsAnalysisEngine.PitchCeiling);
        _harmonicity = LivePitchTracker.Harmonicity(samplingFrequency, 0.01, AcousticsAnalysisEngine.PitchFloor, 0.1, 1.0);
        _intensity = new LiveIntensityTracker(samplingFrequency, AcousticsAnalysisEngine.PitchFloor);
        _formantMethod = formantMethod;
        _ceilings = FormantCeilings.Candidates(formantMethod);
        _fallback = FormantCeilings.FallbackIndex(_ceilings);
        var origin = FormantCeilings.GridOrigin(formantMethod);
        _formants = new LiveFormantTracker[_ceilings.Count];
        _weightCache = new ConcurrentDictionary<long, RawAnalysis.WeightRow?>[_ceilings.Count];
        for (var i = 0; i < _formants.Length; i++)
        {
            _formants[i] = new LiveFormantTracker(_ceilings[i], AcousticsAnalysisEngine.FormantCount, AcousticsAnalysisEngine.FormantWindow, 50,
                formantBlockSeconds, gridOrigin: origin);
            _weightCache[i] = new ConcurrentDictionary<long, RawAnalysis.WeightRow?>();
        }
        _frames = new IFormantFrames[_ceilings.Count];
        _smoothed = new IFormantTrack?[_ceilings.Count];
        _candidates = new IFormantTrack[_ceilings.Count];
        _alongPath = new SwitchedFormantTrack(_smoothed, _path);
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
            // All ceilings share each block's segment and resampling.
            () => { using (Timing.Measure("live.formants")) LiveFormantTracker.UpdateAll(_formants, view, final); });
        if (_pitch.FrameCount == 0 || _intensity.FrameCount == 0) return null;

        PitchContour pitch;
        using (Timing.Measure("live.pitchpath")) pitch = _pitch.Contour(view);
        var intensity = _intensity.Contour(view);
        HarmonicityContour harmonicity;
        using (Timing.Measure("live.hnrpath")) harmonicity = _harmonicity.HarmonicityContour(view);
        var sound = view.AsSound();

        // Stage 2: pulses, then the voiced frames, sounding stretches and the gate; the ceiling path.
        var lastFrame = pitch.FrameCount > 0 ? pitch.Grid.IndexToX(pitch.FrameCount - 1) : 0;
        using (Timing.Measure("live.pulses")) _pulses.Update(view, pitch, lastFrame - PulseStability, final);
        _derived.ComputeStage2(pitch, intensity, set, _loudOutlierFraction);
        for (var i = 0; i < _formants.Length; i++) _candidates[i] = _formants[i].Track();
        IFormantTrack formants;
        CeilingPath? path = null;
        if (_formantMethod == FormantCeilingMethod.Path && set.Has(TrackSet.FormantPath))
        {
            using (Timing.Measure("live.formants.path")) formants = FindPath(pitch, final);
            path = _path;
        }
        else formants = _candidates[_fallback];

        var covered = _formants[0].CoveredUntil;
        RawAnalysis.WeightRow? WeightRow(double t, double f0)
        {
            // Each moment's row is measured on (and cached for) the ceiling that holds there now.
            var ceiling = path?.At(t) ?? _fallback;
            var cache = _weightCache[ceiling];
            var key = BitConverter.DoubleToInt64Bits(t);
            if (cache.TryGetValue(key, out var cached)) return cached;
            // Formants must already be known around t; otherwise try again on a later update.
            if (t + 0.5 * Math.Max(0.025, 3.0 / f0) + 0.01 > covered && !final) return null;
            var row = RawAnalysisAssembler.MeasureWeightRow(sound, formants, t, f0);
            cache[key] = row;
            return row;
        }

        var tracks = new TrackView
        {
            Tracks = set,
            Duration = view.Duration,
            SamplingFrequency = view.Grid.Step > 0 ? 1.0 / view.Grid.Step : sound.SamplingFrequency,
            Sound = sound,
            Pitch = pitch,
            Intensity = intensity,
            Harmonicity = harmonicity,
            FormantMethod = _formantMethod,
            FormantCeilings = _ceilings,
            FormantCandidates = _candidates,
            Formants = formants,
            CeilingPath = path,
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

    /// <summary>
    /// The path over all frames so far (scored as measured, as the saved analysis does), then the
    /// used candidates' median-smoothed tracks along it. Before the final update the path's end
    /// keeps the previous update's ceiling unless another is clearly better.
    /// </summary>
    private IFormantTrack FindPath(PitchContour pitch, bool final)
    {
        for (var i = 0; i < _formants.Length; i++) _frames[i] = _formants[i].Frames();
        _finder.Find(_frames, pitch, _fallback, _path, final ? null : _pathEnd);
        _pathEnd = _path.Last;
        for (var i = 0; i < _formants.Length; i++)
            _smoothed[i] = _path.Uses(i) ? _formants[i].SmoothedTrack() : null;
        return _alongPath;
    }
}
