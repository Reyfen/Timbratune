using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Voice;

namespace Reyfen.Timbratune.Acoustics.Streaming;

/// <summary>
/// Pitch (or harmonicity) frames computed as samples arrive, with the same frame
/// kernel as <see cref="PitchAnalyzer"/>. Frames sit on a fixed grid from the start
/// of the recording; the path through them is re-chosen on every
/// <see cref="Contour"/>, against the signal's peak so far.
/// </summary>
public sealed class LivePitchTracker
{
    private readonly PitchFrameAnalyzer _analyzer;
    private readonly List<PitchFrame> _frames = [];

    private LivePitchTracker(PitchFrameAnalyzer analyzer) => _analyzer = analyzer;

    /// <summary>Same settings as <see cref="PitchAnalyzer.Autocorrelation"/>.</summary>
    public static LivePitchTracker Autocorrelation(double samplingFrequency, double timeStep, double pitchFloor, double pitchCeiling,
        PitchSettings? settings = null) =>
        new(new PitchFrameAnalyzer(samplingFrequency, 1, PitchAnalyzer.Method.AutocorrelationHanning, 3.0, timeStep, pitchFloor,
            pitchCeiling, settings ?? new PitchSettings()));

    /// <summary>Same settings as <see cref="HarmonicityAnalyzer.CrossCorrelation"/>.</summary>
    public static LivePitchTracker Harmonicity(double samplingFrequency, double timeStep = 0.01, double minimumPitch = 75,
        double silenceThreshold = 0.1, double periodsPerWindow = 1.0) =>
        new(HarmonicityAnalyzer.CreateFrameAnalyzer(samplingFrequency, 1, timeStep, minimumPitch, silenceThreshold, periodsPerWindow));

    public int FrameCount => _frames.Count;

    /// <summary>
    /// Recording length (≤ <paramref name="available"/>, less than one frame step shorter) at
    /// which a whole-file analysis puts its frames exactly on this tracker's frames.
    /// </summary>
    public int AlignedLength(int available, double samplingFrequency) =>
        TimeGrid.AlignedLength(available, samplingFrequency, _analyzer.FrameWindowDuration, _analyzer.TimeStep);

    /// <summary>
    /// Analyzes every frame whose samples have all arrived. With <paramref name="final"/>
    /// (the recording has ended) it completes exactly the frames the full analysis would have.
    /// </summary>
    public void Update(LiveSignalView signal, bool final = false)
    {
        var first = signal.FirstFrame(_analyzer.FrameWindowDuration);
        var dt = _analyzer.TimeStep;
        var limit = final ? signal.FullFrameCount(_analyzer.FrameWindowDuration, dt) : int.MaxValue;
        if (final)
        {
            // Frames that read past the (possibly trimmed) end are redone, clipped like the full analysis clips them.
            var keep = Math.Min(_frames.Count, limit);
            while (keep > 0 && _analyzer.LastSampleNeeded(signal.Grid, first + (keep - 1) * dt) > signal.Count - 1) keep--;
            _frames.RemoveRange(keep, _frames.Count - keep);
        }

        var pending = new List<double>();
        for (var k = _frames.Count; k < limit; k++)
        {
            var t = first + k * dt;
            if (!final && _analyzer.LastSampleNeeded(signal.Grid, t) > signal.Count - 1) break;
            pending.Add(t);
        }
        if (pending.Count == 0) return;
        var results = new PitchFrame[pending.Count];
        var channels = signal.Channels;
        Parallel.For(0, pending.Count, Parallelism.Options, _analyzer.RentBuffers, (i, _, buffers) =>
        {
            results[i] = _analyzer.AnalyzeFrame(channels, signal.Grid, pending[i], buffers);
            return buffers;
        }, _analyzer.ReturnBuffers);
        _frames.AddRange(results);
    }

    /// <summary>The contour so far, with the path chosen against the signal's current peak.</summary>
    public PitchContour Contour(LiveSignalView signal)
    {
        var grid = new TimeGrid(0, signal.Duration, _frames.Count, _analyzer.TimeStep, signal.FirstFrame(_analyzer.FrameWindowDuration));
        return _analyzer.ChoosePath(grid, [.. _frames], signal.GlobalPeak);
    }

    /// <summary>For a harmonicity tracker: HNR per frame so far.</summary>
    public HarmonicityContour HarmonicityContour(LiveSignalView signal) => HarmonicityAnalyzer.FromPitch(Contour(signal));
}

/// <summary>Intensity frames computed as samples arrive (same kernel as <see cref="IntensityAnalyzer"/>).</summary>
public sealed class LiveIntensityTracker(double samplingFrequency, double minimumPitch, double timeStep = 0, bool subtractMean = true)
{
    private readonly IntensityFrameAnalyzer _analyzer = new(samplingFrequency, minimumPitch, timeStep, subtractMean);
    private readonly List<double> _db = [];

    public int FrameCount => _db.Count;

    public void Update(LiveSignalView signal, bool final = false)
    {
        var first = signal.FirstFrame(_analyzer.FrameWindowDuration);
        var dt = _analyzer.TimeStep;
        var limit = final ? signal.FullFrameCount(_analyzer.FrameWindowDuration, dt) : int.MaxValue;
        if (final)
        {
            var keep = Math.Min(_db.Count, limit);
            while (keep > 0 && _analyzer.LastSampleNeeded(signal.Grid, first + (keep - 1) * dt) > signal.Count - 1) keep--;
            _db.RemoveRange(keep, _db.Count - keep);
        }
        var buffer = _analyzer.CreateBuffer();
        var channels = signal.Channels;
        for (var k = _db.Count; k < limit; k++)
        {
            var t = first + k * dt;
            if (!final && _analyzer.LastSampleNeeded(signal.Grid, t) > signal.Count - 1) break;
            _db.Add(_analyzer.AnalyzeFrame(channels, signal.Grid, t, buffer));
        }
    }

    public IntensityContour Contour(LiveSignalView signal) =>
        new(new TimeGrid(0, signal.Duration, _db.Count, _analyzer.TimeStep, signal.FirstFrame(_analyzer.FrameWindowDuration)), [.. _db]);
}

/// <summary>
/// Formants while recording: every half second the recent stretch (plus a margin
/// on both sides) goes through <see cref="FormantAnalyzer.Burg"/>, and only the
/// frames clear of the margins are kept, so segment edges don't affect them.
/// </summary>
public sealed class LiveFormantTracker(double formantCeiling, double maxFormants = 5, double windowLength = 0.025,
    double preEmphasisFrom = 50, double blockSeconds = 0.5, double marginSeconds = 0.25)
{
    private readonly List<(double T, FormantValue[] Formants)> _frames = [];
    private readonly double _maxFormants = maxFormants, _windowLength = windowLength, _preEmphasisFrom = preEmphasisFrom;
    private readonly double _blockSeconds = blockSeconds, _marginSeconds = marginSeconds;

    /// <summary>Formants are known up to this time (s).</summary>
    public double CoveredUntil { get; private set; }

    public double FormantCeiling { get; } = formantCeiling;

    public void Update(LiveSignalView signal, bool final = false) => UpdateAll([this], signal, final);

    /// <summary>
    /// Updates several trackers of the same signal at once. Trackers with the same block settings
    /// that are at the same point share the segment and its resampling (<see cref="FormantAnalyzer.BurgAll"/>,
    /// identical to separate <see cref="FormantAnalyzer.Burg"/> calls); the others update on their own.
    /// </summary>
    public static void UpdateAll(IReadOnlyList<LiveFormantTracker> trackers, LiveSignalView signal, bool final = false)
    {
        foreach (var group in trackers.GroupBy(t => (t.CoveredUntil, t._maxFormants, t._windowLength, t._preEmphasisFrom, t._blockSeconds, t._marginSeconds)))
        {
            var members = group.ToList();
            var lead = members[0];
            var duration = signal.Duration;
            if (!final && duration - lead.CoveredUntil < lead._blockSeconds + lead._marginSeconds) continue;
            var keepUntil = final ? duration + 1 : duration - lead._marginSeconds;
            var segmentStart = Math.Max(0, lead.CoveredUntil - lead._marginSeconds);
            if (duration - segmentStart < 2 * lead._windowLength + 0.01) continue;

            var segment = signal.AsSound().ExtractPart(segmentStart, duration);
            var contours = FormantAnalyzer.BurgAll(segment, members.Select(m => m.FormantCeiling).ToList(), 0, lead._maxFormants,
                lead._windowLength, lead._preEmphasisFrom);
            for (var k = 0; k < members.Count; k++)
            {
                var tracker = members[k];
                var contour = contours[k];
                for (var i = 0; i < contour.Grid.Count; i++)
                {
                    var t = contour.Grid.IndexToX(i) + segmentStart;
                    if (t < tracker.CoveredUntil || t >= keepUntil) continue;
                    tracker._frames.Add((t, contour.Frames[i] as FormantValue[] ?? [.. contour.Frames[i]]));
                }
                tracker.CoveredUntil = final ? duration : keepUntil;
            }
        }
    }

    /// <summary>
    /// The frames so far, as a view (no copy): valid while no update runs, i.e. for use within
    /// the same live update (the frames are only ever appended).
    /// </summary>
    public IFormantTrack Track() => new StitchedFormantTrack(_frames, _frames.Count);
}

/// <summary>Formant frames at irregular times, interpolated like <see cref="FormantContour"/>.</summary>
internal sealed class StitchedFormantTrack(IReadOnlyList<(double T, FormantValue[] Formants)> frames, int count) : IFormantTrack
{
    public double ValueAtTime(int number, double t) => AtTime(number, t, f => f.Frequency);
    public double BandwidthAtTime(int number, double t) => AtTime(number, t, f => f.Bandwidth);

    private double AtTime(int number, double t, Func<FormantValue, double> select)
    {
        if (count == 0) return double.NaN;
        // Beyond half a frame step outside the known frames, nothing is known.
        var halfStep = count > 1 ? 0.5 * (frames[1].T - frames[0].T) : 0.003;
        if (t < frames[0].T - halfStep || t > frames[count - 1].T + halfStep) return double.NaN;

        // First frame at or after t (frames are in time order).
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (frames[mid].T < t) lo = mid + 1;
            else hi = mid;
        }
        if (lo < count && frames[lo].T == t) return Get(lo);
        var right = lo;
        var left = right - 1;
        if (left < 0) return Get(0);
        if (right >= count) return Get(count - 1);
        var phase = (t - frames[left].T) / (frames[right].T - frames[left].T);
        int near = left, far = right;
        if (phase >= 0.5)
        {
            (near, far) = (right, left);
            phase = 1 - phase;
        }
        var nearValue = Get(near);
        if (double.IsNaN(nearValue)) return double.NaN;
        var farValue = Get(far);
        return double.IsNaN(farValue) ? nearValue : nearValue + phase * (farValue - nearValue);

        double Get(int i)
        {
            var f = frames[i].Formants;
            return number >= 1 && number <= f.Length ? select(f[number - 1]) : double.NaN;
        }
    }
}

/// <summary>
/// Glottal pulses while recording: voiced stretches that have ended (and whose pitch
/// no longer changes) are analyzed once and cached; the stretch still being spoken is
/// left out until it ends.
/// </summary>
public sealed class LivePulseTracker
{
    private sealed record Stretch(double Left, double Right, double[] F0, double AddedRightBefore, double[] Pulses, double AddedRightAfter);

    private readonly List<Stretch> _done = [];
    private double _peakUsed = double.NaN;

    /// <summary>Pulses of all finished stretches, sorted.</summary>
    public double[] Pulses { get; private set; } = [];

    /// <summary>Per finished stretch: its end time and local jitter (fraction; NaN if too few pulses).</summary>
    public IReadOnlyList<(double End, double Jitter)> StretchJitter { get; private set; } = [];

    /// <param name="stableUntil">Stretches must end before this time to count as finished (ignored when final).</param>
    public void Update(LiveSignalView signal, PitchContour pitch, double stableUntil, bool final = false)
    {
        var peak = signal.AbsolutePeak;
        if (peak != _peakUsed)
        {
            _done.Clear(); // acceptance levels are relative to the peak: redo everything
            _peakUsed = peak;
        }
        var sound = signal.AsSound();
        var stretches = PulseDetector.VoicedStretches(pitch).Where(s => final || s.Right < stableUntil).ToList();

        var keep = 0;
        while (keep < _done.Count && keep < stretches.Count && Same(_done[keep], stretches[keep], pitch)) keep++;
        _done.RemoveRange(keep, _done.Count - keep);

        var addedRight = keep > 0 ? _done[keep - 1].AddedRightAfter : -1e308;
        for (var i = keep; i < stretches.Count; i++)
        {
            var (left, right) = stretches[i];
            var before = addedRight;
            var points = new SortedSet<double>();
            if (!PulseDetector.DetectStretch(sound, pitch, left, right, peak, points, ref addedRight)) break;
            _done.Add(new Stretch(left, right, F0In(pitch, left, right), before, [.. points], addedRight));
        }

        var all = new SortedSet<double>();
        foreach (var s in _done) all.UnionWith(s.Pulses);
        Pulses = [.. all];
        StretchJitter = _done.Select(s => (s.Right, VoiceReport.JitterLocal(s.Pulses))).ToList();
    }

    private static bool Same(Stretch cached, (double Left, double Right) current, PitchContour pitch) =>
        cached.Left == current.Left && cached.Right == current.Right && cached.F0.AsSpan().SequenceEqual(F0In(pitch, current.Left, current.Right));

    private static double[] F0In(PitchContour pitch, double left, double right)
    {
        var (from, to) = pitch.Grid.WindowIndices(left, right);
        var f0 = new double[Math.Max(0, to - from + 1)];
        for (var i = 0; i < f0.Length; i++) f0[i] = pitch.ValueInFrame(from + i);
        return f0;
    }
}
