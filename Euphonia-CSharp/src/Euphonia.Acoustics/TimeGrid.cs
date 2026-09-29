namespace Euphonia.Acoustics;

/// <summary>
/// A regular sampling of the domain [<see cref="XMin"/>, <see cref="XMax"/>]:
/// <see cref="Count"/> points spaced <see cref="Step"/> apart, the first one at
/// <see cref="First"/>. Used for audio samples, analysis frames and spectral bins.
/// Indices are 0-based.
/// </summary>
public readonly record struct TimeGrid(double XMin, double XMax, int Count, double Step, double First)
{
    public double IndexToX(int index) => First + index * Step;

    /// <summary>Fractional 0-based index of <paramref name="x"/>.</summary>
    public double XToIndex(double x) => (x - First) / Step;

    // The "+ 1 … − 1" keeps these bit-identical to 1-based conventions near integers.
    /// <summary>Largest index whose point is at or before x.</summary>
    public int LowIndex(double x) => (int)Math.Floor((x - First) / Step + 1) - 1;
    /// <summary>Smallest index whose point is at or after x.</summary>
    public int HighIndex(double x) => (int)Math.Ceiling((x - First) / Step + 1) - 1;
    /// <summary>Index of the nearest point (ties go up).</summary>
    public int NearestIndex(double x) => (int)Math.Floor((x - First) / Step + 1 + 0.5) - 1;

    /// <summary>Indices of the points inside [xmin, xmax], clipped to the grid (may be empty: from &gt; to).</summary>
    public (int From, int To) WindowIndices(double xmin, double xmax) =>
        (Math.Max(0, (int)Math.Ceiling((xmin - First) / Step)), Math.Min(Count - 1, (int)Math.Floor((xmax - First) / Step)));

    /// <summary>
    /// Frames for short-term analysis: as many windows of <paramref name="windowDuration"/>
    /// as fit, <paramref name="timeStep"/> apart, centred as a group in this grid's span.
    /// </summary>
    /// <summary>
    /// The largest sample count ≤ <paramref name="available"/> (dropping less than one
    /// <paramref name="timeStep"/>) for which <see cref="ShortTermFrames"/> of the whole
    /// signal starts its frames where a stream analysis does, at
    /// windowDuration/2 from the start. With that length, whole-signal and streaming
    /// analyses use the same frame times.
    /// </summary>
    public static int AlignedLength(int available, double samplingFrequency, double windowDuration, double timeStep)
    {
        var dx = 1.0 / samplingFrequency;
        var maxDrop = (int)Math.Ceiling(timeStep * samplingFrequency) + 1;
        for (var n = available; n >= Math.Max(1, available - maxDrop); n--)
        {
            var samples = new TimeGrid(0, n * dx, n, dx, 0.5 * dx);
            if (n * dx < windowDuration) break;
            var frames = ShortTermFrames(samples, windowDuration, timeStep);
            var streamFirst = samples.First - 0.5 * dx + 0.5 * windowDuration;
            if (Math.Abs(frames.First - streamFirst) < 1e-12) return n;
        }
        return available;
    }

    public static TimeGrid ShortTermFrames(TimeGrid signal, double windowDuration, double timeStep)
    {
        var duration = signal.Step * signal.Count;
        if (windowDuration > duration)
            throw new ArgumentException("The signal is shorter than one analysis window.");
        var frames = (int)Math.Floor((duration - windowDuration) / timeStep) + 1;
        var middle = signal.First - 0.5 * signal.Step + 0.5 * duration;
        var first = middle - 0.5 * frames * timeStep + 0.5 * timeStep;
        return new TimeGrid(signal.XMin, signal.XMax, frames, timeStep, first);
    }
}
