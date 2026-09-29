namespace Euphonia.Acoustics.Pitch;

/// <summary>
/// Global path finder over the per-frame candidates (Boersma 1993, §5): a
/// Viterbi search that maximizes the summed candidate scores minus
/// transition costs for octave jumps and voiced/unvoiced switches.
/// </summary>
public static class PitchPath
{
    /// <summary>
    /// Reorders every frame's candidates so that the chosen one comes first.
    /// Costs are scaled to a 10 ms time step.
    /// </summary>
    public static void Choose(PitchContour pitch, double silenceThreshold, double voicingThreshold, double octaveCost,
        double octaveJumpCost, double voicedUnvoicedCost, double ceiling)
    {
        var frames = pitch.Frames;
        var n = frames.Count;
        if (n == 0) return;
        var stepCorrection = 0.01 / pitch.Grid.Step;
        octaveJumpCost *= stepCorrection;
        voicedUnvoicedCost *= stepCorrection;

        // Local scores.
        var delta = new double[n][];
        var psi = new int[n][];
        for (var f = 0; f < n; f++)
        {
            var frame = frames[f];
            var unvoicedStrength = silenceThreshold <= 0 ? 0.0 : 2.0 - frame.Intensity / (silenceThreshold / (1.0 + voicingThreshold));
            unvoicedStrength = voicingThreshold + Math.Max(0.0, unvoicedStrength);
            var cands = frame.Candidates;
            delta[f] = new double[cands.Count];
            psi[f] = new int[cands.Count];
            for (var c = 0; c < cands.Count; c++)
            {
                var freq = cands[c].Frequency;
                delta[f][c] = PitchContour.IsVoicedFrequency(freq, ceiling)
                    ? cands[c].Strength - octaveCost * Math.Log2(ceiling / freq)
                    : unvoicedStrength;
            }
        }

        // Forward pass: best predecessor for every candidate.
        for (var f = 1; f < n; f++)
        {
            var previous = frames[f - 1].Candidates;
            var current = frames[f].Candidates;
            var bestScores = new double[current.Count];
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

        // Best end point, then trace back, moving each winner to slot 0.
        var place = 0;
        for (var c = 1; c < delta[n - 1].Length; c++)
            if (delta[n - 1][c] > delta[n - 1][place]) place = c;
        for (var f = n - 1; f >= 0; f--)
        {
            var cands = frames[f].Candidates;
            (cands[0], cands[place]) = (cands[place], cands[0]);
            place = psi[f][place];
        }
    }
}
