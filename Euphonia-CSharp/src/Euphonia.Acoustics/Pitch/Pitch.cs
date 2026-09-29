using Euphonia.Acoustics.Numerics;

namespace Euphonia.Acoustics.Pitch;

/// <summary>A periodicity candidate: frequency (0 = unvoiced) and normalized correlation strength.</summary>
public readonly record struct PitchCandidate(double Frequency, double Strength);

/// <summary>
/// One analysis frame: its local peak amplitude (compared with the whole sound's
/// peak when the path is chosen) and its candidates; candidate 0 is always "unvoiced".
/// Frames are immutable, so the same frames can be re-scored as a recording grows.
/// </summary>
public sealed class PitchFrame
{
    internal static readonly PitchFrame Silent = new(0, [new PitchCandidate(0, 0)]);

    internal PitchFrame(double localPeak, List<PitchCandidate> candidates)
    {
        LocalPeak = localPeak;
        Candidates = candidates;
    }

    /// <summary>Largest |sample| near the frame centre, after removing the local mean.</summary>
    public double LocalPeak { get; }
    internal List<PitchCandidate> Candidates { get; }
    public IReadOnlyList<PitchCandidate> AllCandidates => Candidates;
}

/// <summary>
/// A pitch contour: per frame the candidate chosen by the path finder. A frame is
/// voiced when that frequency is above 0 and below <see cref="Ceiling"/>.
/// </summary>
public sealed class PitchContour
{
    private readonly int[] _chosen;

    internal PitchContour(TimeGrid grid, IReadOnlyList<PitchFrame> frames, int[] chosen, double ceiling)
    {
        Grid = grid;
        Frames = frames;
        _chosen = chosen;
        Ceiling = ceiling;
    }

    public TimeGrid Grid { get; }
    public IReadOnlyList<PitchFrame> Frames { get; }
    public double Ceiling { get; }
    public int FrameCount => Frames.Count;

    /// <summary>The chosen candidate of a frame.</summary>
    public PitchCandidate Best(int frame) => Frames[frame].Candidates[_chosen[frame]];

    public bool IsVoiced(int frame) => frame >= 0 && frame < Frames.Count && IsVoicedFrequency(Best(frame).Frequency, Ceiling);

    internal static bool IsVoicedFrequency(double f, double ceiling) => f > 0 && f < ceiling;

    /// <summary>F0 of a frame in Hz, or NaN when unvoiced / out of range.</summary>
    public double ValueInFrame(int frame) => IsVoiced(frame) ? Best(frame).Frequency : double.NaN;

    public IEnumerable<double> VoicedValues()
    {
        for (var i = 0; i < Frames.Count; i++)
            if (IsVoiced(i)) yield return Best(i).Frequency;
    }

    /// <summary>Mean F0 over voiced frames (Hz).</summary>
    public double Mean() => Stats.Mean(VoicedValues().ToArray());

    /// <summary>Sample standard deviation of F0 over voiced frames (n − 1 in the denominator).</summary>
    public double StandardDeviation()
    {
        var v = VoicedValues().ToArray();
        if (v.Length < 2) return double.NaN;
        var mean = Stats.Mean(v);
        var sum = new CompensatedSum();
        foreach (var x in v) sum.Add((x - mean) * (x - mean));
        return Math.Sqrt(sum.Value / (v.Length - 1));
    }

    /// <summary>Quantile of the voiced F0 values, e.g. 0.5 for the median.</summary>
    public double Quantile(double q)
    {
        var v = VoicedValues().ToArray();
        Array.Sort(v);
        return Stats.Quantile(v, q);
    }

    /// <summary>Lowest F0, with parabolic refinement where a frame is a local minimum between voiced neighbours.</summary>
    public double Minimum() => Extremum(maximum: false);

    /// <summary>Highest F0, with parabolic refinement where a frame is a local maximum between voiced neighbours.</summary>
    public double Maximum() => Extremum(maximum: true);

    private double Extremum(bool maximum)
    {
        var best = double.NaN;
        for (var i = 0; i < Frames.Count; i++)
        {
            var mid = ValueInFrame(i);
            if (double.IsNaN(mid)) continue;
            var left = ValueInFrame(i - 1);
            var right = ValueInFrame(i + 1);
            double candidate;
            if (double.IsNaN(left) || double.IsNaN(right))
                candidate = mid;
            else if (maximum ? mid > left && mid >= right : mid < left && mid <= right)
                candidate = PeakRefinement.Parabolic(left, mid, right).Value;
            else
                continue;
            if (double.IsNaN(best) || (maximum ? candidate > best : candidate < best)) best = candidate;
        }
        return best > 0 ? best : double.NaN;
    }

    /// <summary>
    /// F0 at time t, linearly interpolated between the two nearest frames.
    /// NaN if the nearer frame is unvoiced; the nearer value alone if only the farther one is.
    /// </summary>
    public double ValueAtTime(double t)
    {
        if (t < Grid.XMin || t > Grid.XMax) return double.NaN;
        var index = Grid.XToIndex(t) + 1; // 1-based fractional
        var leftIndex = (int)Math.Floor(index);
        var phase = index - leftIndex;
        int near, far;
        if (phase < 0.5)
        {
            near = leftIndex;
            far = leftIndex + 1;
        }
        else
        {
            far = leftIndex;
            near = leftIndex + 1;
            phase = 1 - phase;
        }
        var nearValue = ValueInFrame(near - 1);
        if (double.IsNaN(nearValue)) return double.NaN;
        var farValue = ValueInFrame(far - 1);
        return double.IsNaN(farValue) ? nearValue : nearValue + phase * (farValue - nearValue);
    }
}
