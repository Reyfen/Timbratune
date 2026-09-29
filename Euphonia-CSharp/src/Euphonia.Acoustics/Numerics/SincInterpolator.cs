namespace Euphonia.Acoustics.Numerics;

/// <summary>
/// Band-limited interpolation of a sampled function with a windowed sinc
/// (Boersma 1993, §3: sin(x)/x interpolation with a raised-cosine window of
/// half-width depth + ½ samples). Depth 0 = nearest, 1 = linear, 2 = cubic.
/// </summary>
/// <remarks>
/// Positions use 1-based sample numbers (sample k sits at x = k) because that
/// is how the analyzers express fractional lags; outside [1, n] the edge value
/// is repeated. The kernel is not renormalized by the window sum.
/// </remarks>
public static class SincInterpolator
{
    public const int Nearest = 0;
    public const int Linear = 1;
    public const int Cubic = 2;

    /// <param name="y">Samples; y[0] is sample number 1.</param>
    /// <param name="x">Fractional 1-based sample number.</param>
    /// <param name="maxDepth">Number of samples used on each side (clipped at the edges).</param>
    public static double Interpolate(ReadOnlySpan<double> y, double x, int maxDepth)
    {
        var n = y.Length;
        if (n < 1) return double.NaN;
        if (x < 1) return y[0];
        if (x > n) return y[n - 1];
        var midLeft = (int)Math.Floor(x);
        var midRight = midLeft + 1;
        if (x == midLeft) return y[midLeft - 1];

        maxDepth = Math.Min(maxDepth, Math.Min(midRight - 1, n - midLeft));
        if (maxDepth <= Nearest) return y[(int)Math.Floor(x + 0.5) - 1];
        if (maxDepth == Linear) return y[midLeft - 1] + (x - midLeft) * (y[midRight - 1] - y[midLeft - 1]);
        if (maxDepth == Cubic)
        {
            // Hermite-style cubic through the two middle samples with central-difference slopes.
            double yl = y[midLeft - 1], yr = y[midRight - 1];
            var slopeLeft = 0.5 * (yr - y[midLeft - 2]);
            var slopeRight = 0.5 * (y[midRight] - yl);
            double u = x - midLeft, v = midRight - x;
            return yl * v + yr * u - u * v * (0.5 * (slopeRight - slopeLeft) + (u - 0.5) * (slopeLeft + slopeRight - 2 * (yr - yl)));
        }

        var halfWidth = maxDepth + 0.5;
        var left = midRight - maxDepth;
        var right = midLeft + maxDepth;
        var sum = 0.0;
        for (var k = left; k <= right; k++)
        {
            var d = Math.PI * (x - k); // never 0: x is not an integer
            var window = 0.5 + 0.5 * Math.Cos(d / halfWidth);
            sum += y[k - 1] * Math.Sin(d) / d * window;
        }
        return sum;
    }
}
