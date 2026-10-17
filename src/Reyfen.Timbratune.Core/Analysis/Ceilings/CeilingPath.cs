using Reyfen.Timbratune.Acoustics.Formants;

namespace Reyfen.Timbratune.Core.Analysis.Ceilings;

/// <summary>
/// Which formant ceiling (an index into the candidates) holds when: segments in time order, the
/// first from the start, each until the next one's start. Made by <see cref="CeilingPathFinder"/>;
/// live, one instance is refilled by every update (valid for the update that filled it).
/// </summary>
public sealed class CeilingPath
{
    private readonly List<double> _starts = [];
    private readonly List<int> _ceilings = [];

    /// <summary>One ceiling throughout.</summary>
    public CeilingPath(int ceiling = 0) => Reset(ceiling);

    /// <summary>Number of segments (at least 1).</summary>
    public int SegmentCount => _ceilings.Count;

    /// <summary>When segment <paramref name="index"/> starts (−∞ for the first).</summary>
    public double SegmentStart(int index) => _starts[index];

    /// <summary>The ceiling of segment <paramref name="index"/>.</summary>
    public int SegmentCeiling(int index) => _ceilings[index];

    /// <summary>The ceiling of the most voiced pieces (the take's main ceiling).</summary>
    public int Dominant { get; private set; }

    /// <summary>Changes of ceiling along the path.</summary>
    public int Switches => _ceilings.Count - 1;

    /// <summary>The ceiling at the end of the path.</summary>
    public int Last => _ceilings[^1];

    public int Lowest
    {
        get
        {
            var lowest = _ceilings[0];
            foreach (var c in _ceilings) lowest = Math.Min(lowest, c);
            return lowest;
        }
    }

    public int Highest
    {
        get
        {
            var highest = _ceilings[0];
            foreach (var c in _ceilings) highest = Math.Max(highest, c);
            return highest;
        }
    }

    /// <summary>Whether ceiling <paramref name="ceiling"/> holds anywhere on the path.</summary>
    public bool Uses(int ceiling) => _ceilings.Contains(ceiling);

    /// <summary>The ceiling at time <paramref name="t"/>.</summary>
    public int At(double t)
    {
        // Few segments: a linear scan from the end is as fast as a binary search and never allocates.
        for (var i = _starts.Count - 1; i > 0; i--)
            if (t >= _starts[i]) return _ceilings[i];
        return _ceilings[0];
    }

    internal void Reset(int ceiling)
    {
        _starts.Clear();
        _ceilings.Clear();
        _starts.Add(double.NegativeInfinity);
        _ceilings.Add(ceiling);
        Dominant = ceiling;
    }

    /// <summary>From <paramref name="start"/> on, <paramref name="ceiling"/> holds (starts must increase).</summary>
    internal void Add(double start, int ceiling)
    {
        _starts.Add(start);
        _ceilings.Add(ceiling);
    }

    internal void SetDominant(int ceiling) => Dominant = ceiling;
}

/// <summary>
/// Formants read, at each moment, from the track of the ceiling the <see cref="CeilingPath"/> names
/// there (one track per candidate; null for candidates the path doesn't use).
/// </summary>
public sealed class SwitchedFormantTrack(IReadOnlyList<IFormantTrack?> tracks, CeilingPath path) : IFormantTrack
{
    public CeilingPath Path => path;

    /// <summary>The candidates' tracks, by index.</summary>
    public IReadOnlyList<IFormantTrack?> Tracks => tracks;

    /// <summary>The track that holds at time <paramref name="t"/>.</summary>
    public IFormantTrack TrackAt(double t) => tracks[path.At(t)]!;

    public double ValueAtTime(int number, double t) => TrackAt(t).ValueAtTime(number, t);
    public double BandwidthAtTime(int number, double t) => TrackAt(t).BandwidthAtTime(number, t);
}
