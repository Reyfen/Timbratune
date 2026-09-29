namespace Euphonia.Acoustics.Pitch;

/// <summary>
/// Global path finder over the per-frame candidates (Boersma 1993, §5): a
/// Viterbi search that maximizes the summed candidate scores minus
/// transition costs for octave jumps and voiced/unvoiced switches.
/// </summary>
public static class PitchPath
{
    /// <summary>
    /// Index of the chosen candidate in every frame. Frames are not modified.
    /// Each frame's relative intensity is its local peak over <paramref name="globalPeak"/>.
    /// Costs are scaled to a 10 ms time step.
    /// </summary>
    public static int[] ChooseIndices(IReadOnlyList<PitchFrame> frames, double timeStep, double globalPeak,
        double silenceThreshold, double voicingThreshold, double octaveCost, double octaveJumpCost,
        double voicedUnvoicedCost, double ceiling)
    {
        var n = frames.Count;
        var chosen = new int[n];
        if (n == 0) return chosen;
        var stepCorrection = 0.01 / timeStep;
        octaveJumpCost *= stepCorrection;
        voicedUnvoicedCost *= stepCorrection;

        // Local scores.
        var delta = new double[n][];
        for (var f = 0; f < n; f++)
        {
            var frame = frames[f];
            var intensity = frame.LocalPeak > globalPeak ? 1.0 : frame.LocalPeak / globalPeak;
            var unvoicedStrength = silenceThreshold <= 0 ? 0.0 : 2.0 - intensity / (silenceThreshold / (1.0 + voicingThreshold));
            unvoicedStrength = voicingThreshold + Math.Max(0.0, unvoicedStrength);
            var cands = frame.Candidates;
            delta[f] = new double[cands.Count];
            for (var c = 0; c < cands.Count; c++)
            {
                var freq = cands[c].Frequency;
                delta[f][c] = PitchContour.IsVoicedFrequency(freq, ceiling)
                    ? cands[c].Strength - octaveCost * Math.Log2(ceiling / freq)
                    : unvoicedStrength;
            }
        }

        if (octaveJumpCost == 0 && voicedUnvoicedCost == 0)
            ChooseWithoutTransitionCosts(delta, chosen);
        else
            ChooseWithTransitionCosts(frames, delta, chosen, octaveJumpCost, voicedUnvoicedCost, ceiling);
        return chosen;
    }

    private static void ChooseWithTransitionCosts(IReadOnlyList<PitchFrame> frames, double[][] delta, int[] chosen,
        double octaveJumpCost, double voicedUnvoicedCost, double ceiling)
    {
        var n = frames.Count;
        var psi = new int[n][];
        psi[0] = new int[delta[0].Length];

        // Forward pass: best predecessor for every candidate.
        for (var f = 1; f < n; f++)
        {
            var previous = frames[f - 1].Candidates;
            var current = frames[f].Candidates;
            var bestScores = new double[current.Count];
            psi[f] = new int[current.Count];
            for (var c2 = 0; c2 < current.Count; c2++)
            {
                var f2 = current[c2].Frequency;
                var currentVoiced = PitchContour.IsVoicedFrequency(f2, ceiling);
                var best = double.NegativeInfinity;
                var bestPlace = 0;
                for (var c1 = 0; c1 < previous.Count; c1++)
                {
                    var f1 = previous[c1].Frequency;
                    var previousVoiced = PitchContour.IsVoicedFrequency(f1, ceiling);
                    var transition = currentVoiced && previousVoiced ? octaveJumpCost * Math.Abs(Math.Log2(f1 / f2))
                        : currentVoiced != previousVoiced ? voicedUnvoicedCost
                        : 0.0;
                    var score = delta[f - 1][c1] - transition + delta[f][c2];
                    if (score > best) // first of equal scores wins
                    {
                        best = score;
                        bestPlace = c1;
                    }
                }
                bestScores[c2] = best;
                psi[f][c2] = bestPlace;
            }
            delta[f] = bestScores;
        }

        // Best end point, then trace back.
        var place = 0;
        for (var c = 1; c < delta[n - 1].Length; c++)
            if (delta[n - 1][c] > delta[n - 1][place]) place = c;
        for (var f = n - 1; f >= 0; f--)
        {
            chosen[f] = place;
            place = psi[f][place];
        }
    }

    /// <summary>
    /// The same search when every transition costs 0 (e.g. harmonicity), in
    /// O(frames × candidates) instead of O(frames × candidates²). It reproduces
    /// the general search exactly, rounding included: with zero costs every
    /// candidate's accumulated score is (previous frame's best accumulated
    /// score) + (its own score), and the predecessor on the traced path is the
    /// first previous candidate reaching that same rounded sum.
    /// </summary>
    private static void ChooseWithoutTransitionCosts(double[][] local, int[] chosen)
    {
        var n = local.Length;
        var accumulated = new double[n][];
        accumulated[0] = local[0];
        for (var f = 1; f < n; f++)
        {
            var previousBest = double.NegativeInfinity;
            foreach (var v in accumulated[f - 1]) previousBest = Math.Max(previousBest, v);
            var row = new double[local[f].Length];
            for (var c = 0; c < row.Length; c++) row[c] = previousBest - 0.0 + local[f][c];
            accumulated[f] = row;
        }

        var place = 0;
        for (var c = 1; c < accumulated[n - 1].Length; c++)
            if (accumulated[n - 1][c] > accumulated[n - 1][place]) place = c;
        for (var f = n - 1; f >= 0; f--)
        {
            chosen[f] = place;
            if (f == 0) break;
            // First previous candidate whose sum with this candidate's local score is the maximum.
            var target = accumulated[f][place];
            var previous = accumulated[f - 1];
            var predecessor = 0;
            for (var c1 = 0; c1 < previous.Length; c1++)
            {
                if (previous[c1] - 0.0 + local[f][place] == target)
                {
                    predecessor = c1;
                    break;
                }
            }
            place = predecessor;
        }
    }
}
