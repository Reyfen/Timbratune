namespace Reyfen.Timbratune.Acoustics;

/// <summary>
/// A sampled sound: one or more channels of samples (nominally −1..1) at a
/// fixed sampling frequency. Sample k sits at time (k + ½)/fs, so the sound
/// spans [0, n/fs]. Immutable; analyzers read it, never modify it.
/// </summary>
public sealed class Sound
{
    private readonly double[][] _channels;

    public Sound(double[][] channels, double samplingFrequency, double xmin = 0)
    {
        if (channels.Length == 0) throw new ArgumentException("A sound needs at least one channel.");
        if (channels.Any(c => c.Length != channels[0].Length)) throw new ArgumentException("Channels differ in length.");
        if (!(samplingFrequency > 0)) throw new ArgumentOutOfRangeException(nameof(samplingFrequency));
        _channels = channels;
        var dx = 1.0 / samplingFrequency;
        var n = channels[0].Length;
        Grid = new TimeGrid(xmin, xmin + n * dx, n, dx, xmin + 0.5 * dx);
    }

    public Sound(double[] mono, double samplingFrequency) : this([mono], samplingFrequency) { }

    /// <summary>Wraps existing arrays without copying; they may be longer than <paramref name="grid"/>.Count.</summary>
    internal Sound(double[][] channels, TimeGrid grid)
    {
        _channels = channels;
        Grid = grid;
    }

    public TimeGrid Grid { get; }
    public int SampleCount => Grid.Count;
    public int ChannelCount => _channels.Length;
    public double SamplingFrequency => 1.0 / Grid.Step;
    public double Duration => Grid.XMax - Grid.XMin;

    public ReadOnlySpan<double> Channel(int channel) => _channels[channel].AsSpan(0, SampleCount);

    /// <summary>The backing array; it may be longer than <see cref="SampleCount"/> (live buffers grow in place).</summary>
    internal double[] ChannelArray(int channel) => _channels[channel];

    /// <summary>Average of the channels at sample <paramref name="i"/>.</summary>
    public double MonoSample(int i)
    {
        if (_channels.Length == 1) return _channels[0][i];
        var sum = 0.0;
        foreach (var c in _channels) sum += c[i];
        return sum / _channels.Length;
    }

    /// <summary>Channel average as one array (the channel itself for mono sounds).</summary>
    public double[] ToMono()
    {
        if (_channels.Length == 1)
            return _channels[0].Length == SampleCount ? _channels[0] : _channels[0].AsSpan(0, SampleCount).ToArray();
        var mono = new double[SampleCount];
        for (var i = 0; i < mono.Length; i++) mono[i] = MonoSample(i);
        return mono;
    }

    /// <summary>
    /// Copies the samples whose times fall in [<paramref name="tmin"/>, <paramref name="tmax"/>],
    /// multiplied by <paramref name="window"/>, into a new sound whose time axis starts at 0.
    /// </summary>
    public Sound ExtractPart(double tmin, double tmax, WindowShape window = WindowShape.Rectangular)
    {
        var from = (int)Math.Ceiling((tmin - Grid.First) / Grid.Step);
        var to = (int)Math.Floor((tmax - Grid.First) / Grid.Step);
        var n = to - from + 1;
        if (n < 1) throw new ArgumentException("The part contains no samples.");
        var channels = new double[_channels.Length][];
        for (var c = 0; c < channels.Length; c++)
        {
            var part = new double[n];
            for (var i = 0; i < n; i++)
            {
                var source = from + i;
                if (source >= 0 && source < SampleCount) part[i] = _channels[c][source] * Windows.Value(window, i, n);
            }
            channels[c] = part;
        }
        var first = Grid.First + from * Grid.Step - tmin;
        return new Sound(channels, new TimeGrid(0, tmax - tmin, n, Grid.Step, first));
    }
}

public enum WindowShape { Rectangular, Hamming, Hanning }

/// <summary>Window functions (Harris 1978), sampled at half-sample phase over n points.</summary>
public static class Windows
{
    public static double Value(WindowShape shape, int i, int n)
    {
        var phase = (i + 0.5) / n; // centre of sample i within the window
        return shape switch
        {
            WindowShape.Hamming => 0.54 - 0.46 * Math.Cos(2 * Math.PI * phase),
            WindowShape.Hanning => 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase),
            _ => 1.0,
        };
    }

    /// <summary>Modified Bessel function I₀ via the polynomial approximations of Abramowitz &amp; Stegun 9.8.1–9.8.2.</summary>
    public static double BesselI0(double x)
    {
        if (x < 0) x = -x;
        if (x < 3.75)
        {
            var t = x / 3.75;
            var t2 = t * t;
            return 1.0 + t2 * (3.5156229 + t2 * (3.0899424 + t2 * (1.2067492 + t2 * (0.2659732 + t2 * (0.0360768 + t2 * 0.0045813)))));
        }
        var u = 3.75 / x;
        return Math.Exp(x) / Math.Sqrt(x) * (0.39894228 + u * (0.01328592 + u * (0.00225319 + u * (-0.00157565 + u * (0.00916281
            + u * (-0.02057706 + u * (0.02635537 + u * (-0.01647633 + u * 0.00392377))))))));
    }
}
