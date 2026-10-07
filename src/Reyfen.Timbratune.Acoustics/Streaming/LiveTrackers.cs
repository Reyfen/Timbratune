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
        Parallel.For(0, pending.Count, _analyzer.RentBuffers, (i, _, buffers) =>
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

    /// <summary>Formants are known up to this time (s).</summary>
    public double CoveredUntil { get; private set; }

    public double FormantCeiling { get; } = formantCeiling;

    public void Update(LiveSignalView signal, bool final = false)
    {
        var duration = signal.Duration;
        if (!final && duration - CoveredUntil < blockSeconds + marginSeconds) return;
        var keepUntil = final ? duration + 1 : duration - marginSeconds;
        var segmentStart = Math.Max(0, CoveredUntil - marginSeconds);
        if (duration - segmentStart < 2 * windowLength + 0.01) return;

        var segment = signal.AsSound().ExtractPart(segmentStart, duration);
        var contour = FormantAnalyzer.Burg(segment, 0, maxFormants, FormantCeiling, windowLength, preEmphasisFrom);
        for (var i = 0; i < contour.Grid.Count; i++)
        {
            var t = contour.Grid.IndexToX(i) + segmentStart;
            if (t < CoveredUntil || t >= keepUntil) continue;
            _frames.Add((t, [.. contour.Frames[i]]));
        }
        CoveredUntil = final ? duration : keepUntil;
    }

    public IFormantTrack Track() => new StitchedFormantTrack([.. _frames]);
}

/// <summary>Formant frames at irregular times, interpolated like <see cref="FormantContour"/>.</summary>
internal sealed class StitchedFormantTrack((double T, FormantValue[] Formants)[] frames) : IFormantTrack
{
    public double ValueAtTime(int number, double t) => AtTime(number, t, f => f.Frequency);
    public double BandwidthAtTime(int number, double t) => AtTime(number, t, f => f.Bandwidth);

    private double AtTime(int number, double t, Func<FormantValue, double> select)
    {
        if (frames.Length == 0) return double.NaN;
        // Beyond half a frame step outside the known frames, nothing is known.
        var halfStep = frames.Length > 1 ? 0.5 * (frames[1].T - frames[0].T) : 0.003;
        if (t < frames[0].T - halfStep || t > frames[^1].T + halfStep) return double.NaN;

        var right = Array.BinarySearch(frames, (t, Array.Empty<FormantValue>()), Comparer<(double T, FormantValue[] F)>.Create((a, b) => a.T.CompareTo(b.T)));
        if (right >= 0) return Get(right);
        right = ~right; // first frame after t
        var left = right - 1;
        if (left < 0) return Get(0);
        if (right >= frames.Length) return Get(frames.Length - 1);
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
