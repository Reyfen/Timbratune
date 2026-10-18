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

    /// <summary>How this finder scores a piece on a candidate (<see cref="CeilingScoring"/>).</summary>
    public CeilingScoring Scoring { get; }

    public CeilingPathFinder(CeilingScoring? scoring = null) => Scoring = scoring ?? CeilingScoring.Default;

    /// <summary>A piece a candidate can't score costs this much more than the worst candidate that could.</summary>
    public const double UnscoredPenalty = 0.5;

    /// <summary>
    /// Live: the path keeps ending on the previous update's last ceiling unless another end is cheaper by
    /// more than this (summed log W), so the newest moments don't flicker between near-equal ceilings.
    /// </summary>
    public const double EndMargin = 4;

    private readonly List<FramePiece> _pieces = [];
    private bool[] _voiced = [];
    private double[] _costs = [], _switch = [], _transitions = [];
    private int[] _back = [], _states = [];
    // Per candidate, by piece start frame: 1 + the end of the piece the cached scores belong to (0 = none), the log W,
    // and the log of the piece's instability against the neighbouring candidates (HIGH-PITCH-PLAN; NaN when unused).
    private int[][] _cachedTo = [];
    private double[][] _cachedCost = [];
    private double[][] _cachedStability = [];
    private double[][] _cachedMedianStability = [];
    // Per candidate, by piece start frame: the piece's level of F2–F4 (PieceLevels; Hz, NaN where the candidate lacks the formant), for the continuity term.
    private double[][] _cachedMedians = [];
    private const int MedianFormants = 3;

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
        var transitions = ReadOnlySpan<double>.Empty;
        if (Scoring.ContinuityWeight > 0)
        {
            Grow(ref _transitions, n * k * k);
            Continuity(analyses, k, _transitions.AsSpan(0, n * k * k));
            transitions = _transitions.AsSpan(0, n * k * k);
        }
        Decode(costs, k, _switch.AsSpan(0, n), transitions, _back.AsSpan(0, n * k), states, keepEnd);

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

    /// <summary>
    /// The score of every piece on every candidate into <see cref="_costs"/> (NaN where it can't be scored), from the
    /// cache where it can: <see cref="CeilingScoring.SmoothnessWeight"/> × log W, plus, when
    /// <see cref="CeilingScoring.StabilityWeight"/> is set, that weight × the log of the piece's instability.
    /// </summary>
    private void Score(IReadOnlyList<IFormantFrames> analyses)
    {
        var k = analyses.Count;
        if (_cachedTo.Length != k)
        {
            _cachedTo = new int[k][];
            _cachedCost = new double[k][];
            _cachedStability = new double[k][];
            _cachedMedianStability = new double[k][];
            _cachedMedians = new double[k][];
            for (var c = 0; c < k; c++) (_cachedTo[c], _cachedCost[c], _cachedStability[c], _cachedMedianStability[c], _cachedMedians[c]) = ([], [], [], [], []);
        }
        var missing = 0;
        for (var c = 0; c < k; c++)
        {
            Grow(ref _cachedTo[c], analyses[0].FrameCount, keep: true);
            Grow(ref _cachedCost[c], analyses[0].FrameCount, keep: true);
            Grow(ref _cachedStability[c], analyses[0].FrameCount, keep: true);
            Grow(ref _cachedMedianStability[c], analyses[0].FrameCount, keep: true);
            Grow(ref _cachedMedians[c], MedianFormants * analyses[0].FrameCount, keep: true);
            for (var i = 0; i < _pieces.Count; i++)
            {
                var piece = Clip(_pieces[i], analyses[c]);
                if (_cachedTo[c][piece.From] != piece.To + 1) missing++;
            }
        }
        // The first scoring of a whole take is worth spreading over the cores; the few new pieces of a live update aren't.
        if (missing > 64) Parallel.For(0, k, Parallelism.Options, c => ScoreCandidate(analyses, c));
        else for (var c = 0; c < k; c++) ScoreCandidate(analyses, c);
    }

    private void ScoreCandidate(IReadOnlyList<IFormantFrames> analyses, int c)
    {
        var k = analyses.Count;
        var frames = analyses[c];
        var cachedTo = _cachedTo[c];
        var cachedCost = _cachedCost[c];
        var cachedStability = _cachedStability[c];
        var cachedMedianStability = _cachedMedianStability[c];
        var cachedMedians = _cachedMedians[c];
        var stability = Scoring.StabilityWeight > 0;
        var medianStability = Scoring.MedianStabilityWeight > 0;
        for (var i = 0; i < _pieces.Count; i++)
        {
            var piece = Clip(_pieces[i], frames);
            if (cachedTo[piece.From] != piece.To + 1)
            {
                var w = piece.Length < FormantSmoothness.Terms + 3 ? double.NaN : FormantSmoothness.Criterion(frames, piece.From, piece.To, Scoring.BandwidthFloorHz);
                cachedCost[piece.From] = double.IsFinite(w) && w > 0 ? Math.Log(w) : double.NaN;
                cachedStability[piece.From] = stability ? Math.Log(Instability(analyses, c, piece, Scoring.Reference, Scoring.FirstFormant, Scoring.LastFormant, Scoring.Relative) + Scoring.StabilityFloor) : double.NaN;
                cachedMedianStability[piece.From] = medianStability
                    ? Math.Log(Instability(analyses, c, piece, StabilityReference.AllOthersMedian, Scoring.FirstFormant, Scoring.LastFormant, Scoring.Relative) + Scoring.StabilityFloor)
                    : double.NaN;
                if (Scoring.ContinuityWeight > 0) PieceLevels(frames, piece, cachedMedians.AsSpan(MedianFormants * piece.From, MedianFormants));
                // The stability compares with the other candidates: live, one of them can still lack the piece's last
                // frame, and a score from fewer frames must not be kept (the saved analysis would see them all).
                var complete = true;
                if (stability || medianStability)
                    foreach (var other in analyses)
                        complete &= other.FrameCount >= _pieces[i].To;
                cachedTo[piece.From] = complete ? piece.To + 1 : 0;
            }
            var cost = Scoring.SmoothnessWeight * cachedCost[piece.From];
            if (stability && double.IsFinite(cachedStability[piece.From])) cost += Scoring.StabilityWeight * cachedStability[piece.From];
            if (medianStability && double.IsFinite(cachedMedianStability[piece.From])) cost += Scoring.MedianStabilityWeight * cachedMedianStability[piece.From];
            _costs[i * k + c] = cost;
        }
    }

    /// <summary>
    /// How much candidate <paramref name="c"/>'s F2 and F3 differ from the nearer of its neighbouring candidates
    /// over <paramref name="piece"/> (Hz): per neighbour, the median over the piece's frames of the mean of
    /// |ΔF2| and |ΔF3|; the smaller of the two neighbours. A ceiling on the plateau where the formants no longer
    /// move with the ceiling scores low; one that squeezes an extra pole in (too low) or loses one (too high)
    /// differs from its neighbours by hundreds of Hz. Reads no pitch, so it can't couple pitch and size
    /// (HIGH-PITCH-PLAN). <paramref name="reference"/> says which other candidates count (<see cref="StabilityReference"/>).
    /// NaN when no other candidate has frames to compare.
    /// </summary>
    public static double Instability(IReadOnlyList<IFormantFrames> analyses, int c, FramePiece piece,
        StabilityReference reference = StabilityReference.NearerNeighbour, int firstFormant = 2, int lastFormant = 3, bool relative = false)
    {
        Span<double> differences = stackalloc double[Math.Max(1, piece.Length)];
        Span<double> perOther = stackalloc double[analyses.Count];
        var others = 0;
        var from = reference == StabilityReference.AllOthersMedian ? 0 : c - 1;
        var until = reference == StabilityReference.AllOthersMedian ? analyses.Count - 1 : c + 1;
        for (var n = from; n <= until; n++)
        {
            if (n == c || n < 0 || n >= analyses.Count) continue;
            var to = Math.Min(piece.To, Math.Min(analyses[c].FrameCount, analyses[n].FrameCount));
            var count = 0;
            for (var i = piece.From; i < to; i++)
            {
                var a = analyses[c].FrameAt(i);
                var b = analyses[n].FrameAt(i);
                if (a.Length < lastFormant || b.Length < lastFormant) continue;
                var sum = 0.0;
                for (var f = firstFormant - 1; f < lastFormant; f++)
                    sum += relative ? 1000 * Math.Abs(Math.Log(a[f].Frequency / b[f].Frequency)) : Math.Abs(a[f].Frequency - b[f].Frequency);
                if (!double.IsFinite(sum)) continue;
                differences[count++] = sum / (lastFormant - firstFormant + 1);
            }
            if (count == 0) continue;
            perOther[others++] = Median(differences[..count]);
        }
        if (others == 0) return double.NaN;
        var distances = perOther[..others];
        switch (reference)
        {
            case StabilityReference.NearerNeighbour:
                var best = distances[0];
                foreach (var d in distances) best = Math.Min(best, d);
                return best;
            case StabilityReference.BothNeighbours:
                var total = 0.0;
                foreach (var d in distances) total += d;
                return total / others;
            default:
                return Median(distances);
        }
    }

    /// <summary>
    /// The level of F2–F4 over the piece's frames that have them (NaN where fewer than half do): the mean of the middle
    /// half of the sorted values. Robust to a slipped frame like a median, but continuous in the frames, so a frame a
    /// few Hz off (a live block's last frames, HANDOFF §8w) moves it by a fraction of that and can't tip a path
    /// decision the way a median's step between two sorted values did (HANDOFF §8x: live = saved on 62 of 68 files
    /// with medians).
    /// </summary>
    private static void PieceLevels(IFormantFrames frames, FramePiece piece, Span<double> levels)
    {
        Span<double> values = stackalloc double[Math.Max(1, piece.Length)];
        for (var f = 0; f < MedianFormants; f++)
        {
            var count = 0;
            for (var i = piece.From; i < piece.To; i++)
            {
                var frame = frames.FrameAt(i);
                if (frame.Length > f + 1) values[count++] = frame[f + 1].Frequency;
            }
            if (count == 0 || count * 2 < piece.Length)
            {
                levels[f] = double.NaN;
                continue;
            }
            var sorted = values[..count];
            sorted.Sort();
            int from = count / 4, to = count - count / 4;
            var sum = 0.0;
            for (var i = from; i < to; i++) sum += sorted[i];
            levels[f] = sum / (to - from);
        }
    }

    /// <summary>
    /// The continuity cost of every transition into <paramref name="transitions"/> [piece × k × k + from × k + to], times
    /// <see cref="CeilingScoring.ContinuityWeight"/>, less <see cref="CeilingScoring.ContinuityDeadZone"/> (‰), never below 0:
    /// <list type="bullet">
    /// <item>Within a stretch (the pieces overlap by half): for a change of ceiling, how far the two ceilings' F2–F4
    /// sit apart on the frames the two pieces share, in ‰ of mean relative difference (the kernel of
    /// <see cref="Instability"/>, on the same moments, so a live block's frames a few Hz off can't move it); staying on
    /// a ceiling is exactly free. A change between two ceilings that agree (the plateau) costs a few ‰; one between
    /// the two readings of a voice like take 24's (HANDOFF §8x) costs 300–600 ‰: the jump the graphs would show.</item>
    /// <item>Across a pause (no shared frames; the next phrase starts on another vowel): how much further the
    /// <see cref="PieceLevels"/> of F3 and up jump from candidate <c>from</c> on the last piece before to candidate
    /// <c>to</c> on the first piece after than to the candidate that continues them best (the tract's cues carry the
    /// reading over; the best continuation is free).</item>
    /// </list>
    /// 0 where a candidate lacks every formant. The first piece's row is 0.
    /// </summary>
    private void Continuity(IReadOnlyList<IFormantFrames> analyses, int k, Span<double> transitions)
    {
        var n = _pieces.Count;
        transitions[..(k * k)].Clear();
        Span<double> jumps = stackalloc double[k];
        var first = Math.Max(0, Scoring.FirstFormant - 2);
        for (var i = 1; i < n; i++)
        {
            var row = transitions.Slice(i * k * k, k * k);
            if (_switch[i] != PauseSwitchCost)
            {
                if (!Scoring.ContinuityWithinStretches) continue;
                var shared = new FramePiece(_pieces[i].From, Math.Min(_pieces[i - 1].To, _pieces[i].To));
                var last = Math.Min(MedianFormants - 1, Scoring.ContinuityLastFormant - 2);
                for (var a = 0; a < k; a++)
                    for (var b = 0; b < k; b++)
                    {
                        if (a == b) continue;
                        var d = shared.Length > 0 ? Distance(analyses, a, b, shared, first + 2, last + 2) : double.NaN;
                        row[a * k + b] = double.IsFinite(d) ? Scoring.ContinuityWeight * Math.Max(0, d - Scoring.ContinuityDeadZone) : 0;
                    }
                continue;
            }
            int previous = _pieces[i - 1].From, current = _pieces[i].From;
            var pauseFirst = Math.Max(1, first);
            var pauseLast = Math.Min(MedianFormants - 1, Scoring.ContinuityPauseLastFormant - 2);
            for (var a = 0; a < k; a++)
            {
                var nearest = double.PositiveInfinity;
                for (var b = 0; b < k; b++)
                {
                    var sum = 0.0;
                    var count = 0;
                    for (var f = pauseFirst; f <= pauseLast; f++)
                    {
                        double fa = _cachedMedians[a][MedianFormants * previous + f], fb = _cachedMedians[b][MedianFormants * current + f];
                        if (!(fa > 0 && fb > 0)) continue;
                        sum += Math.Min(CeilingScoring.ContinuityCap, 1000 * Math.Abs(Math.Log(fa / fb)));
                        count++;
                    }
                    jumps[b] = count == 0 ? double.NaN : sum / count;
                    if (jumps[b] < nearest) nearest = jumps[b];
                }
                for (var b = 0; b < k; b++)
                    row[a * k + b] = double.IsFinite(jumps[b]) && double.IsFinite(nearest)
                        ? Scoring.ContinuityWeight * Math.Max(0, jumps[b] - nearest - Scoring.ContinuityDeadZone)
                        : 0;
            }
        }
    }

    /// <summary>
    /// How far candidates <paramref name="a"/> and <paramref name="b"/> sit apart over <paramref name="piece"/>: the
    /// median over its frames of the mean relative difference (‰, each formant capped at
    /// <see cref="CeilingScoring.ContinuityCap"/>) of formants <paramref name="firstFormant"/>–<paramref name="lastFormant"/>;
    /// NaN without a frame that has them on both.
    /// </summary>
    private static double Distance(IReadOnlyList<IFormantFrames> analyses, int a, int b, FramePiece piece, int firstFormant, int lastFormant)
    {
        Span<double> differences = stackalloc double[Math.Max(1, piece.Length)];
        var to = Math.Min(piece.To, Math.Min(analyses[a].FrameCount, analyses[b].FrameCount));
        var count = 0;
        for (var i = piece.From; i < to; i++)
        {
            var x = analyses[a].FrameAt(i);
            var y = analyses[b].FrameAt(i);
            if (x.Length < lastFormant || y.Length < lastFormant) continue;
            var sum = 0.0;
            for (var f = firstFormant - 1; f < lastFormant; f++)
                sum += Math.Min(CeilingScoring.ContinuityCap, 1000 * Math.Abs(Math.Log(x[f].Frequency / y[f].Frequency)));
            if (!double.IsFinite(sum)) continue;
            differences[count++] = sum / (lastFormant - firstFormant + 1);
        }
        return count == 0 ? double.NaN : Median(differences[..count]);
    }

    private static double Median(Span<double> values)
    {
        values.Sort();
        var count = values.Length;
        return count % 2 == 1 ? values[count / 2] : 0.5 * (values[count / 2 - 1] + values[count / 2]);
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
        int? keepEnd = null) =>
        Decode(costs, k, switchCosts, ReadOnlySpan<double>.Empty, back, states, keepEnd);

    /// <summary>
    /// The same with a cost per transition, <paramref name="transitions"/>[piece × k × k + from × k + to] (the continuity
    /// term; empty = none), added to the switch cost when the state changes and charged on its own when it stays.
    /// </summary>
    public static void Decode(ReadOnlySpan<double> costs, int k, ReadOnlySpan<double> switchCosts, ReadOnlySpan<double> transitions,
        Span<int> back, Span<int> states, int? keepEnd = null)
    {
        var n = states.Length;
        if (n == 0) return;
        Span<double> total = stackalloc double[k];
        Span<double> next = stackalloc double[k];
        costs[..k].CopyTo(total);
        for (var i = 1; i < n; i++)
        {
            if (transitions.IsEmpty)
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
            }
            else
            {
                var row = transitions.Slice(i * k * k, k * k);
                for (var c = 0; c < k; c++)
                {
                    var best = total[c] + row[c * k + c];
                    var from = c;
                    for (var a = 0; a < k; a++)
                    {
                        if (a == c) continue;
                        var via = total[a] + switchCosts[i] + row[a * k + c];
                        if (via < best) (best, from) = (via, a);
                    }
                    back[i * k + c] = from;
                    next[c] = best + costs[i * k + c];
                }
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
