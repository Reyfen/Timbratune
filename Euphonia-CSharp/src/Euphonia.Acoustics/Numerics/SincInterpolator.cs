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

        // Σ y[k]·sin(d)/d·½(1 + cos(d / halfWidth)) with d = π(x − k), walking outwards from
        // the middle on each side. Only four trig calls per interpolation: stepping k by one
        // flips the sign of sin(d), and the window cosine advances by a fixed angle, so it is
        // updated with the angle-addition formulas instead of being recomputed per term.
        var halfWidth = maxDepth + 0.5;
        var windowStep = Math.PI / halfWidth;
        var cosStep = Math.Cos(windowStep);
        var sinStep = Math.Sin(windowStep);
        var sum = 0.0;

        // Left half: k = midLeft, midLeft − 1, …, midLeft − maxDepth + 1; d = π(x − k) > 0 grows by π.
        {
            var d = Math.PI * (x - midLeft);
            var sinD = Math.Sin(d);
            var cosW = Math.Cos(d / halfWidth);
            var sinW = Math.Sin(d / halfWidth);
            for (var k = midLeft; k >= midRight - maxDepth; k--)
            {
                sum += y[k - 1] * (0.5 * sinD / d * (1.0 + cosW));
                d += Math.PI;
                sinD = -sinD;
                (cosW, sinW) = (cosW * cosStep - sinW * sinStep, sinW * cosStep + cosW * sinStep);
            }
        }
        // Right half: k = midRight, …, midLeft + maxDepth; d = π(k − x) > 0 grows by π.
        {
            var d = Math.PI * (midRight - x);
            var sinD = Math.Sin(d);
            var cosW = Math.Cos(d / halfWidth);
            var sinW = Math.Sin(d / halfWidth);
            for (var k = midRight; k <= midLeft + maxDepth; k++)
            {
                sum += y[k - 1] * (0.5 * sinD / d * (1.0 + cosW));
                d += Math.PI;
                sinD = -sinD;
                (cosW, sinW) = (cosW * cosStep - sinW * sinStep, sinW * cosStep + cosW * sinStep);
            }
        }
        return sum;
    }
}
