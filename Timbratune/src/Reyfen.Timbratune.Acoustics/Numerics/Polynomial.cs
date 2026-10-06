using System.Numerics;

namespace Reyfen.Timbratune.Acoustics.Numerics;

/// <summary>
/// Roots of real polynomials by the Aberth–Ehrlich simultaneous iteration
/// (Aberth 1973; Bini 1996), followed by Newton polishing of each root
/// against the original polynomial.
/// </summary>
public static class Polynomial
{
    /// <param name="coefficients">c[0] + c[1]·z + … + c[n]·zⁿ, with c[n] ≠ 0.</param>
    public static Complex[] Roots(ReadOnlySpan<double> coefficients, int maxIterations = 500)
    {
        var degree = coefficients.Length - 1;
        while (degree > 0 && coefficients[degree] == 0) degree--;
        if (degree < 1) return [];

        var c = new double[degree + 1];
        for (var i = 0; i <= degree; i++) c[i] = coefficients[i] / coefficients[degree]; // monic

        // Initial guesses: spread on a circle whose radius bounds the roots (Cauchy-type bound),
        // at angles offset from the real axis so conjugate pairs are not started on top of each other.
        var radius = 0.0;
        for (var i = 0; i < degree; i++) radius = Math.Max(radius, Math.Pow(Math.Abs(c[i]), 1.0 / (degree - i)));
        radius = Math.Max(radius, 1e-3);
        var z = new Complex[degree];
        for (var k = 0; k < degree; k++)
            z[k] = Complex.FromPolarCoordinates(radius, 2 * Math.PI * k / degree + 0.4);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var maxStep = 0.0;
            for (var k = 0; k < degree; k++)
            {
                var (p, dp) = EvaluateWithDerivative(c, z[k]);
                if (p == Complex.Zero) continue;
                var ratio = p / dp;
                var repulsion = Complex.Zero;
                for (var j = 0; j < degree; j++)
                    if (j != k) repulsion += 1.0 / (z[k] - z[j]);
                var step = ratio / (1.0 - ratio * repulsion);
                z[k] -= step;
                maxStep = Math.Max(maxStep, step.Magnitude / Math.Max(1.0, z[k].Magnitude));
            }
            if (maxStep < 1e-15) break;
        }

        for (var k = 0; k < degree; k++) z[k] = Polish(c, z[k]);
        return z;
    }

    /// <summary>Newton iterations that stop as soon as |p(z)| no longer decreases; returns the best iterate.</summary>
    public static Complex Polish(ReadOnlySpan<double> coefficients, Complex z, int maxIterations = 80)
    {
        var (p, dp) = EvaluateWithDerivative(coefficients, z);
        var best = z;
        var bestMagnitude = p.Magnitude;
        for (var i = 0; i < maxIterations && bestMagnitude > 0 && dp != Complex.Zero; i++)
        {
            var next = best - p / dp;
            (p, dp) = EvaluateWithDerivative(coefficients, next);
            if (!(p.Magnitude < bestMagnitude)) break;
            best = next;
            bestMagnitude = p.Magnitude;
        }
        return best;
    }

    /// <summary>Horner evaluation of p(z) and p′(z) for p = Σ c[i]·zⁱ.</summary>
    public static (Complex Value, Complex Derivative) EvaluateWithDerivative(ReadOnlySpan<double> c, Complex z)
    {
        var value = new Complex(c[^1], 0);
        var derivative = Complex.Zero;
        for (var i = c.Length - 2; i >= 0; i--)
        {
            derivative = derivative * z + value;
            value = value * z + c[i];
        }
        return (value, derivative);
    }
}
