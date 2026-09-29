using Euphonia.Acoustics.Numerics;

namespace Euphonia.Acoustics.Streaming;

/// <summary>
/// A mono signal that grows while it is being recorded. <see cref="Append"/> may
/// be called from the audio thread; <see cref="View()"/> returns a consistent,
/// immutable picture of everything received so far for the analysis thread.
/// </summary>
public sealed class LiveSignal(double samplingFrequency)
{
    private readonly object _gate = new();
    private double[] _buffer = new double[(int)samplingFrequency * 10];
    private int _count;
    private CompensatedSum _sum;
    private double _absolutePeak;

    public double SamplingFrequency { get; } = samplingFrequency;

    public void Append(ReadOnlySpan<double> samples)
    {
        lock (_gate)
        {
            if (_count + samples.Length > _buffer.Length)
            {
                // Grow into a new array: views keep reading the old one, which is never written again.
                var bigger = new double[Math.Max(_buffer.Length * 2, _count + samples.Length)];
                Array.Copy(_buffer, bigger, _count);
                _buffer = bigger;
            }
            samples.CopyTo(_buffer.AsSpan(_count));
            foreach (var s in samples)
            {
                _sum.Add(s); // same order as a one-shot compensated mean, so the result is identical
                _absolutePeak = Math.Max(_absolutePeak, Math.Abs(s));
            }
            _count += samples.Length;
        }
    }

    public int Count
    {
        get { lock (_gate) return _count; }
    }

    public LiveSignalView View()
    {
        lock (_gate)
            return new LiveSignalView(_buffer, _count, SamplingFrequency, _count == 0 ? double.NaN : _sum.Value / _count, _absolutePeak);
    }

    /// <summary>A view of only the first <paramref name="count"/> samples (e.g. the recording trimmed to its final length).</summary>
    public LiveSignalView View(int count)
    {
        double[] buffer;
        lock (_gate)
        {
            if (count >= _count) return View();
            buffer = _buffer;
        }
        var sum = new CompensatedSum();
        var peak = 0.0;
        foreach (var s in buffer.AsSpan(0, count))
        {
            sum.Add(s);
            peak = Math.Max(peak, Math.Abs(s));
        }
        return new LiveSignalView(buffer, count, SamplingFrequency, count == 0 ? double.NaN : sum.Value / count, peak);
    }
}

/// <summary>Everything a <see cref="LiveSignal"/> had received at one moment.</summary>
public sealed class LiveSignalView
{
    private double? _globalPeak;

    internal LiveSignalView(double[] samples, int count, double samplingFrequency, double mean, double absolutePeak)
    {
        Samples = samples;
        Count = count;
        Mean = mean;
        AbsolutePeak = absolutePeak;
        var dx = 1.0 / samplingFrequency;
        Grid = new TimeGrid(0, count * dx, count, dx, 0.5 * dx);
    }

    /// <summary>Backing array; only the first <see cref="Count"/> values belong to the view.</summary>
    internal double[] Samples { get; }
    public int Count { get; }
    public double Mean { get; }
    /// <summary>Largest |sample| so far.</summary>
    public double AbsolutePeak { get; }
    public TimeGrid Grid { get; }
    public double Duration => Grid.XMax;

    /// <summary>Largest |sample − mean| so far: the pitch analyses' silence reference.</summary>
    public double GlobalPeak => _globalPeak ??= ComputeGlobalPeak();

    /// <summary>The view as a <see cref="Sound"/> (no copy).</summary>
    public Sound AsSound() => new([Samples], Grid);

    internal double[][] Channels => [Samples];

    private double ComputeGlobalPeak()
    {
        var peak = 0.0;
        foreach (var s in Samples.AsSpan(0, Count)) peak = Math.Max(peak, Math.Abs(s - Mean));
        return peak;
    }

    /// <summary>
    /// First frame time of a short-term analysis with this window: where the full
    /// analysis puts it when the recording length fits the frame grid exactly.
    /// </summary>
    internal double FirstFrame(double windowDuration) => Grid.First - 0.5 * Grid.Step + 0.5 * windowDuration;

    /// <summary>Number of frames the full analysis of this whole signal would have.</summary>
    internal int FullFrameCount(double windowDuration, double timeStep)
    {
        var duration = Grid.Step * Count;
        return windowDuration > duration ? 0 : (int)Math.Floor((duration - windowDuration) / timeStep) + 1;
    }
}
