namespace Euphonia.Acoustics.Formants;

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
    public static double[] Coefficients(ReadOnlySpan<double> x, int order)
    {
        var n = x.Length;
        var a = new double[order + 1];
        if (n <= order) return a;
        var forward = x.ToArray();  // f_k[i]
        var backward = x.ToArray(); // b_k[i]
        var previous = new double[order + 1];

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
