namespace Reyfen.Timbratune.Acoustics.Intensity;

/// <summary>A stretch of the signal labelled as sounding or silent.</summary>
public readonly record struct Interval(double Start, double End, bool IsSounding)
{
    public double Duration => End - Start;
}

/// <summary>
/// Splits a sound into silent and sounding intervals from its intensity
/// contour, as described in the Praat manual (Intensity: To TextGrid
/// (silences)…): frames more than |threshold| dB below the maximum are
/// silent; then sounding intervals shorter than the minimum are absorbed
/// into the silence around them, and silences shorter than their minimum
/// into the speech around them.
/// </summary>
public static class SilenceDetector
{
    /// <param name="silenceThresholdDb">Relative to the maximum intensity, e.g. −25.</param>
    /// <param name="minimumSilentDuration">Shorter silences are merged away (s).</param>
    /// <param name="minimumSoundingDuration">Shorter sounding stretches are merged away (s).</param>
    public static IReadOnlyList<Interval> Detect(IntensityContour intensity, double silenceThresholdDb = -25,
        double minimumSilentDuration = 0.1, double minimumSoundingDuration = 0.05)
    {
        var grid = intensity.Grid;
        var whole = new List<Interval> { new(grid.XMin, grid.XMax, true) };
        if (minimumSilentDuration > grid.XMax - grid.XMin || grid.Count == 0) return whole;

        var threshold = intensity.Maximum() - Math.Abs(silenceThresholdDb);
        if (threshold < intensity.Minimum()) return whole;

        // Raw segmentation: a boundary at the centre of every frame where the state flips.
        var intervals = new List<Interval>();
        var start = grid.XMin;
        var silent = intensity.Db[0] < threshold;
        for (var i = 1; i < grid.Count; i++)
        {
            var nowSilent = intensity.Db[i] < threshold;
            if (nowSilent == silent) continue;
            var boundary = grid.IndexToX(i);
            intervals.Add(new Interval(start, boundary, !silent));
            start = boundary;
            silent = nowSilent;
        }
        intervals.Add(new Interval(start, grid.XMax, !silent));

        RemoveShort(intervals, sounding: true, minimumSoundingDuration);
        MergeNeighbours(intervals);
        RemoveShort(intervals, sounding: false, minimumSilentDuration);
        MergeNeighbours(intervals);
        return intervals;
    }

    /// <summary>
    /// Deletes too-short intervals of one kind; the time they covered goes to the
    /// preceding interval (or to the following one for the first interval).
    /// </summary>
    private static void RemoveShort(List<Interval> intervals, bool sounding, double minimumDuration)
    {
        var i = 0;
        while (i < intervals.Count)
        {
            var current = intervals[i];
            if (intervals.Count > 1 && current.IsSounding == sounding && current.Duration < minimumDuration)
            {
                if (i == 0) intervals[1] = intervals[1] with { Start = current.Start };
                else intervals[i - 1] = intervals[i - 1] with { End = current.End };
                intervals.RemoveAt(i);
                continue; // re-examine whatever now sits at index i
            }
            i++;
        }
    }

    private static void MergeNeighbours(List<Interval> intervals)
    {
        for (var i = intervals.Count - 1; i > 0; i--)
        {
            if (intervals[i].IsSounding != intervals[i - 1].IsSounding) continue;
            intervals[i - 1] = intervals[i - 1] with { End = intervals[i].End };
            intervals.RemoveAt(i);
        }
    }
}
