namespace Reyfen.Timbratune.Acoustics.Numerics;

/// <summary>Small numeric helpers shared by the analyzers.</summary>
internal static class Stats
{
    /// <summary>Neumaier-compensated sum (Neumaier 1974) — nearly exact for long series.</summary>
    public static double Sum(ReadOnlySpan<double> values)
    {
        double sum = 0, compensation = 0;
        foreach (var v in values)
        {
            var t = sum + v;
            compensation += Math.Abs(sum) >= Math.Abs(v) ? sum - t + v : v - t + sum;
            sum = t;
        }
        return sum + compensation;
    }

    public static double Mean(ReadOnlySpan<double> values) => values.IsEmpty ? double.NaN : Sum(values) / values.Length;

    /// <summary>Rounds half up (⌊x + ½⌋), unlike <see cref="Math.Round(double)"/>.</summary>
    public static int RoundHalfUp(double x) => (int)Math.Floor(x + 0.5);

    /// <summary>
    /// Quantile with linear interpolation between order statistics, placing the
    /// k-th of n sorted values at probability (k − ½)/n.
    /// </summary>
    public static double Quantile(double[] sorted, double q)
    {
        var n = sorted.Length;
        if (n == 0) return double.NaN;
        if (n == 1) return sorted[0];
        var place = q * n + 0.5; // 1-based fractional rank
        var left = Math.Clamp((int)Math.Floor(place), 1, n - 1);
        return sorted[left - 1] + (place - left) * (sorted[left] - sorted[left - 1]);
    }
}

/// <summary>
/// Compensated running sum, for accumulations that can't be expressed as a span
/// (Neumaier 1974).
/// </summary>
internal struct CompensatedSum
{
    private double _sum;
    private double _compensation;

    public void Add(double v)
    {
        var t = _sum + v;
        _compensation += Math.Abs(_sum) >= Math.Abs(v) ? _sum - t + v : v - t + _sum;
        _sum = t;
    }

    public readonly double Value => _sum + _compensation;
}
