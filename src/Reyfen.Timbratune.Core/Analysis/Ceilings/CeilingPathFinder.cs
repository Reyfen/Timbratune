using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Acoustics.Pitch;

namespace Reyfen.Timbratune.Core.Analysis.Ceilings;

/// <summary>
/// The formant ceiling that follows the voice (RESEARCH-ALGORITHMS §13): the take's voiced pieces
/// (<see cref="FormantSmoothness.Pieces"/>, about 0.1 s every 0.05 s) are scored on every candidate
/// ceiling (log W, lower = smoother), then a Viterbi path (Viterbi 1967; Forney 1973) picks one
/// ceiling per piece, minimising the summed log W plus <see cref="SwitchCost"/> per change of ceiling,
/// or <see cref="PauseSwitchCost"/> across a pause of at least <see cref="PauseSeconds"/>. A change
/// therefore needs about a second of voice that fits the new ceiling clearly better (less after a
/// pause), and brief slips don't cause one. Changes fall halfway between the pieces either side.
/// <para>
/// One finder per recording: it caches each piece's score per candidate, which stays valid while the
/// candidates' frames are only ever appended (the live trackers), so the live analysis can find the
/// whole path again on every update for little more than the new pieces' scores. Not thread-safe.
/// </para>
/// </summary>
public sealed class CeilingPathFinder
{
    public const double SwitchCost = 12, PauseSwitchCost = 2, PauseSeconds = 0.25;

    /// <summary>A piece a candidate can't score costs this much more than the worst candidate that could.</summary>
    public const double UnscoredPenalty = 0.5;

    /// <summary>
    /// Live: the path keeps ending on the previous update's last ceiling unless another end is cheaper by
    /// more than this (summed log W), so the newest moments don't flicker between near-equal ceilings.
    /// </summary>
    public const double EndMargin = 4;

    private readonly List<FramePiece> _pieces = [];
    private bool[] _voiced = [];
    private double[] _costs = [], _switch = [];
    private int[] _back = [], _states = [];
    // Per candidate, by piece start frame: 1 + the end of the piece the cached log W belongs to (0 = none), and the log W.
    private int[][] _cachedTo = [];
    private double[][] _cachedCost = [];

    /// <summary>The voiced pieces the last <see cref="Find"/> scored.</summary>
    public int PieceCount => _pieces.Count;

    /// <summary>
    /// Fills <paramref name="path"/> with the path over <paramref name="analyses"/> (one per candidate, all on
    /// one frame grid; their counts may differ by a frame at the end). With no voiced piece, the path is
    /// <paramref name="fallback"/> throughout.
    /// </summary>
    /// <param name="pitch">Voicing: a formant frame is voiced when the pitch frame nearest to it is.</param>
    /// <param name="keepEnd">Live: the previous update's last ceiling (see <see cref="EndMargin"/>). Null for the
    /// final update and the saved analysis: the cheapest end.</param>
    public void Find(IReadOnlyList<IFormantFrames> analyses, PitchContour pitch, int fallback, CeilingPath path, int? keepEnd = null)
    {
        var k = analyses.Count;
        var reference = analyses[0];
        var frames = reference.FrameCount;
        Grow(ref _voiced, frames);
        for (var i = 0; i < frames; i++)
            _voiced[i] = pitch.FrameCount > 0 && pitch.IsVoiced(pitch.Grid.NearestIndex(reference.FrameTime(i)));
        _pieces.Clear();
        FormantSmoothness.Pieces(_voiced.AsSpan(0, frames), _pieces);
        var n = _pieces.Count;
        if (n == 0)
        {
            path.Reset(fallback);
            return;
        }

        Grow(ref _costs, n * k);
        Grow(ref _back, n * k);
        Grow(ref _states, n);
        Grow(ref _switch, n);
        Score(analyses);

        // Entries a candidate couldn't score: that piece's worst score plus a penalty, so a candidate
        // that loses a formant isn't favoured; a piece no candidate can score costs nothing.
        var costs = _costs.AsSpan(0, n * k);
        for (var i = 0; i < n; i++)
        {
            var row = costs.Slice(i * k, k);
            var worst = double.NegativeInfinity;
            foreach (var c in row)
                if (double.IsFinite(c)) worst = Math.Max(worst, c);
            for (var c = 0; c < k; c++)
                if (!double.IsFinite(row[c])) row[c] = double.IsFinite(worst) ? worst + UnscoredPenalty : 0;
        }
        _switch[0] = 0;
        for (var i = 1; i < n; i++)
        {
            var gap = reference.FrameTime(_pieces[i].From) - reference.FrameTime(_pieces[i - 1].To - 1);
            _switch[i] = gap >= PauseSeconds ? PauseSwitchCost : SwitchCost;
        }
        var states = _states.AsSpan(0, n);
        Decode(costs, k, _switch.AsSpan(0, n), _back.AsSpan(0, n * k), states, keepEnd);

        path.Reset(states[0]);
        Span<int> counts = stackalloc int[k];
        counts.Clear();
        for (var i = 0; i < n; i++)
        {
            counts[states[i]]++;
            if (i > 0 && states[i] != states[i - 1])
                path.Add(0.5 * (Middle(reference, _pieces[i - 1]) + Middle(reference, _pieces[i])), states[i]);
        }
        var dominant = 0;
        for (var c = 1; c < k; c++)
            if (counts[c] > counts[dominant]) dominant = c;
        path.SetDominant(dominant);
    }

    /// <summary>log W of every piece on every candidate into <see cref="_costs"/> (NaN where it can't be scored), from the cache where it can.</summary>
    private void Score(IReadOnlyList<IFormantFrames> analyses)
    {
        var k = analyses.Count;
        if (_cachedTo.Length != k)
        {
            _cachedTo = new int[k][];
            _cachedCost = new double[k][];
            for (var c = 0; c < k; c++) (_cachedTo[c], _cachedCost[c]) = ([], []);
        }
        var missing = 0;
        for (var c = 0; c < k; c++)
        {
            Grow(ref _cachedTo[c], analyses[0].FrameCount, keep: true);
            Grow(ref _cachedCost[c], analyses[0].FrameCount, keep: true);
            for (var i = 0; i < _pieces.Count; i++)
            {
                var piece = Clip(_pieces[i], analyses[c]);
                if (_cachedTo[c][piece.From] != piece.To + 1) missing++;
            }
        }
        // The first scoring of a whole take is worth spreading over the cores; the few new pieces of a live update aren't.
        if (missing > 64) Parallel.For(0, k, Parallelism.Options, c => ScoreCandidate(analyses[c], c, k));
        else for (var c = 0; c < k; c++) ScoreCandidate(analyses[c], c, k);
    }

    private void ScoreCandidate(IFormantFrames frames, int c, int k)
    {
        var cachedTo = _cachedTo[c];
        var cachedCost = _cachedCost[c];
        for (var i = 0; i < _pieces.Count; i++)
        {
            var piece = Clip(_pieces[i], frames);
            if (cachedTo[piece.From] != piece.To + 1)
            {
                var w = piece.Length < FormantSmoothness.Terms + 3 ? double.NaN : FormantSmoothness.Criterion(frames, piece.From, piece.To);
                cachedCost[piece.From] = double.IsFinite(w) && w > 0 ? Math.Log(w) : double.NaN;
                cachedTo[piece.From] = piece.To + 1;
            }
            _costs[i * k + c] = cachedCost[piece.From];
        }
    }

    /// <summary>The piece within this candidate's frames (it may have one frame fewer at the end).</summary>
    private static FramePiece Clip(FramePiece piece, IFormantFrames frames) =>
        piece with { To = Math.Max(piece.From, Math.Min(piece.To, frames.FrameCount)) };

    /// <summary>
    /// The Viterbi path over <paramref name="states"/>.Length pieces and <paramref name="k"/> states:
    /// <paramref name="costs"/>[piece × k + state]; staying costs nothing, changing between pieces i − 1 and i costs
    /// <paramref name="switchCosts"/>[i]. The cheapest path's state per piece goes into <paramref name="states"/>
    /// (<paramref name="back"/>: working space as long as <paramref name="costs"/>). <paramref name="keepEnd"/>:
    /// see <see cref="EndMargin"/>.
    /// </summary>
    public static void Decode(ReadOnlySpan<double> costs, int k, ReadOnlySpan<double> switchCosts, Span<int> back, Span<int> states,
        int? keepEnd = null)
    {
        var n = states.Length;
        if (n == 0) return;
        Span<double> total = stackalloc double[k];
        Span<double> next = stackalloc double[k];
        costs[..k].CopyTo(total);
        for (var i = 1; i < n; i++)
        {
            // Each state either stays, or comes from the cheapest state for the switch cost.
            var cheapest = ArgMin(total);
            var viaSwitch = total[cheapest] + switchCosts[i];
            for (var c = 0; c < k; c++)
            {
                var stay = total[c] <= viaSwitch;
                back[i * k + c] = stay ? c : cheapest;
                next[c] = (stay ? total[c] : viaSwitch) + costs[i * k + c];
            }
            next.CopyTo(total);
        }
        var state = ArgMin(total);
        if (keepEnd is { } kept && kept >= 0 && kept < k && total[kept] - total[state] <= EndMargin) state = kept;
        for (var i = n - 1; i >= 0; i--)
        {
            states[i] = state;
            if (i > 0) state = back[i * k + state];
        }
    }

    private static double Middle(IFormantFrames frames, FramePiece piece) =>
        0.5 * (frames.FrameTime(piece.From) + frames.FrameTime(piece.To - 1));

    private static int ArgMin(ReadOnlySpan<double> values)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
            if (values[i] < values[best]) best = i;
        return best;
    }

    private static void Grow<T>(ref T[] array, int length, bool keep = false)
    {
        if (array.Length >= length) return;
        var grown = new T[Math.Max(length, 2 * array.Length)];
        if (keep) array.CopyTo(grown, 0);
        array = grown;
    }
}
