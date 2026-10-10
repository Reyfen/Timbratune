using System.Buffers;

namespace Reyfen.Timbratune.Acoustics.Pitch;

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
    /// <remarks>
    /// The scores live in flat arrays borrowed from a pool (frame f's candidates at
    /// offsets[f]…offsets[f+1]): the live analysis re-runs this over the whole take several
    /// times a second, and per-frame arrays made it one of the biggest sources of garbage.
    /// The arithmetic and tie-breaking are unchanged.
    /// </remarks>
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

        var offsets = ArrayPool<int>.Shared.Rent(n + 1);
        offsets[0] = 0;
        for (var f = 0; f < n; f++) offsets[f + 1] = offsets[f] + frames[f].Candidates.Count;
        var total = offsets[n];
        var local = ArrayPool<double>.Shared.Rent(total);
        var accumulated = ArrayPool<double>.Shared.Rent(total);
        try
        {
            // Local scores.
            for (var f = 0; f < n; f++)
            {
                var frame = frames[f];
                var intensity = frame.LocalPeak > globalPeak ? 1.0 : frame.LocalPeak / globalPeak;
                var unvoicedStrength = silenceThreshold <= 0 ? 0.0 : 2.0 - intensity / (silenceThreshold / (1.0 + voicingThreshold));
                unvoicedStrength = voicingThreshold + Math.Max(0.0, unvoicedStrength);
                var cands = frame.Candidates;
                var at = offsets[f];
                for (var c = 0; c < cands.Count; c++)
                {
                    var freq = cands[c].Frequency;
                    local[at + c] = PitchContour.IsVoicedFrequency(freq, ceiling)
                        ? cands[c].Strength - octaveCost * Math.Log2(ceiling / freq)
                        : unvoicedStrength;
                }
            }

            if (octaveJumpCost == 0 && voicedUnvoicedCost == 0)
                ChooseWithoutTransitionCosts(n, offsets, local, accumulated, chosen);
            else
                ChooseWithTransitionCosts(frames, offsets, local, accumulated, chosen, octaveJumpCost, voicedUnvoicedCost, ceiling);
            return chosen;
        }
        finally
        {
            ArrayPool<double>.Shared.Return(accumulated);
            ArrayPool<double>.Shared.Return(local);
            ArrayPool<int>.Shared.Return(offsets);
        }
    }

    private static void ChooseWithTransitionCosts(IReadOnlyList<PitchFrame> frames, int[] offsets, double[] local,
        double[] accumulated, int[] chosen, double octaveJumpCost, double voicedUnvoicedCost, double ceiling)
    {
        var n = frames.Count;
        var psi = ArrayPool<int>.Shared.Rent(offsets[n]);
        try
        {
            for (var c = 0; c < frames[0].Candidates.Count; c++)
            {
                accumulated[c] = local[c];
                psi[c] = 0;
            }

            // Forward pass: best predecessor for every candidate.
            for (var f = 1; f < n; f++)
            {
                var previous = frames[f - 1].Candidates;
                var current = frames[f].Candidates;
                int prevAt = offsets[f - 1], at = offsets[f];
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
                        var score = accumulated[prevAt + c1] - transition + local[at + c2];
                        if (score > best) // first of equal scores wins
                        {
                            best = score;
                            bestPlace = c1;
                        }
                    }
                    accumulated[at + c2] = best;
                    psi[at + c2] = bestPlace;
                }
            }

            // Best end point, then trace back.
            var last = offsets[n - 1];
            var place = 0;
            for (var c = 1; c < offsets[n] - last; c++)
                if (accumulated[last + c] > accumulated[last + place]) place = c;
            for (var f = n - 1; f >= 0; f--)
            {
                chosen[f] = place;
                place = psi[offsets[f] + place];
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(psi);
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
    private static void ChooseWithoutTransitionCosts(int n, int[] offsets, double[] local, double[] accumulated, int[] chosen)
    {
        for (var c = 0; c < offsets[1]; c++) accumulated[c] = local[c];
        for (var f = 1; f < n; f++)
        {
            var previousBest = double.NegativeInfinity;
            for (var c = offsets[f - 1]; c < offsets[f]; c++) previousBest = Math.Max(previousBest, accumulated[c]);
            for (var c = offsets[f]; c < offsets[f + 1]; c++) accumulated[c] = previousBest - 0.0 + local[c];
        }

        var last = offsets[n - 1];
        var place = 0;
        for (var c = 1; c < offsets[n] - last; c++)
            if (accumulated[last + c] > accumulated[last + place]) place = c;
        for (var f = n - 1; f >= 0; f--)
        {
            chosen[f] = place;
            if (f == 0) break;
            // First previous candidate whose sum with this candidate's local score is the maximum.
            var target = accumulated[offsets[f] + place];
            var localScore = local[offsets[f] + place];
            int prevAt = offsets[f - 1], prevCount = offsets[f] - prevAt;
            var predecessor = 0;
            for (var c1 = 0; c1 < prevCount; c1++)
            {
                if (accumulated[prevAt + c1] - 0.0 + localScore == target)
                {
                    predecessor = c1;
                    break;
                }
            }
            place = predecessor;
        }
    }
}
