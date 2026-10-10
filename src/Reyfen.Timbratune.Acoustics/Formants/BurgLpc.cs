namespace Reyfen.Timbratune.Acoustics.Formants;

/// <summary>
/// Linear-prediction coefficients by Burg's maximum-entropy method (Burg
/// 1975; formulation after Childers 1978 and Kay &amp; Marple 1981): each
/// reflection coefficient minimizes the sum of forward and backward
/// prediction-error powers, which guarantees a stable (minimum-phase) filter.
/// </summary>
public static class BurgLpc
{
    /// <returns>
    /// Prediction coefficients a[1..order] (index 0 unused) with
    /// x[n] ≈ Σ a[j]·x[n − j]. Stops early (remaining coefficients 0) if the
    /// error power vanishes.
    /// </returns>
    public static double[] Coefficients(ReadOnlySpan<double> x, int order) =>
        Coefficients(x, order, new Scratch(x.Length, order));

    /// <summary>Working arrays for <see cref="Coefficients(ReadOnlySpan{double}, int, Scratch)"/>, reusable across frames of up to <c>maxLength</c> samples.</summary>
    public sealed class Scratch(int maxLength, int order)
    {
        internal readonly double[] Forward = new double[maxLength];
        internal readonly double[] Backward = new double[maxLength];
        internal readonly double[] A = new double[order + 1];
        internal readonly double[] Previous = new double[order + 1];
    }

    /// <summary>
    /// The same, working in <paramref name="scratch"/> (the formant analysis runs thousands of
    /// frames; fresh arrays for each were most of the live analysis' garbage). The returned
    /// coefficients live in the scratch: valid until its next use.
    /// </summary>
    public static double[] Coefficients(ReadOnlySpan<double> x, int order, Scratch scratch)
    {
        var n = x.Length;
        var a = scratch.A;
        Array.Clear(a);
        if (n <= order) return a;
        var forward = scratch.Forward;   // f_k[i]
        var backward = scratch.Backward; // b_k[i]
        x.CopyTo(forward);
        x.CopyTo(backward);
        var previous = scratch.Previous;

        for (var k = 1; k <= order; k++)
        {
            // Compensated sums: near-silent frames make the reflection coefficient
            // ill-conditioned, and plain double accumulation visibly shifts their formants.
            var numerator = new Numerics.CompensatedSum();
            var denominator = new Numerics.CompensatedSum();
            for (var i = k; i < n; i++)
            {
                numerator.Add(forward[i] * backward[i - 1]);
                denominator.Add(forward[i] * forward[i]);
                denominator.Add(backward[i - 1] * backward[i - 1]);
            }
            if (denominator.Value <= 0) break;
            var reflection = 2 * numerator.Value / denominator.Value;

            Array.Copy(a, previous, k);
            a[k] = reflection;
            for (var j = 1; j < k; j++) a[j] = previous[j] - reflection * previous[k - j];

            // Update the errors from the end so b_{k−1}[i − 1] is still unmodified when used.
            for (var i = n - 1; i >= k; i--)
            {
                var f = forward[i];
                forward[i] = f - reflection * backward[i - 1];
                backward[i] = backward[i - 1] - reflection * f;
            }
        }
        return a;
    }
}
