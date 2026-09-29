namespace Euphonia.Acoustics.Numerics;

/// <summary>
/// Sub-sample location and height of a local extremum: parabolic
/// interpolation through three points, or maximization of the sinc
/// interpolant with Brent's method (Brent 1973, ch. 5).
/// </summary>
public static class PeakRefinement
{
    public const int SincDepth70 = 70;
    public const int SincDepth700 = 700;

    /// <summary>Vertex of the parabola through (−1, left), (0, mid), (1, right).</summary>
    /// <returns>Height of the vertex and its offset from the middle sample.</returns>
    public static (double Value, double Offset) Parabolic(double left, double mid, double right)
    {
        var slope = 0.5 * (right - left);
        var curvature = 2 * mid - left - right;
        return (mid + 0.5 * slope * slope / curvature, slope / curvature);
    }

    /// <summary>
    /// Refines the maximum near 1-based sample <paramref name="index"/> by
    /// maximizing the sinc interpolant (depth <paramref name="sincDepth"/>) over
    /// [index − 1, index + 1]. Edge samples are returned unrefined.
    /// </summary>
    public static (double Value, double Position) SincMaximum(double[] y, int index, int sincDepth)
    {
        if (index <= 1) return (y[0], 1);
        if (index >= y.Length) return (y[^1], y.Length);
        var (xBest, fBest) = Brent.Minimize(x => -SincInterpolator.Interpolate(y, x, sincDepth), index - 1, index + 1, 1e-10);
        return (-fBest, xBest);
    }
}

/// <summary>
/// Brent's derivative-free one-dimensional minimizer (golden-section search
/// with successive parabolic interpolation), after Brent (1973), ch. 5.
/// </summary>
public static class Brent
{
    private static readonly double SqrtEpsilon = Math.Sqrt(Math.Pow(2, -53));
    private const double GoldenRatioComplement = 0.3819660112501051; // (3 − √5) / 2

    /// <returns>The abscissa of the minimum and the function value there.</returns>
    public static (double X, double Fx) Minimize(Func<double, double> f, double a, double b, double tolerance, int maxIterations = 60)
    {
        var x = a + GoldenRatioComplement * (b - a);
        double w = x, v = x;
        var fx = f(x);
        double fw = fx, fv = fx;
        double d = 0, e = 0;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var middle = 0.5 * (a + b);
            var tol1 = SqrtEpsilon * Math.Abs(x) + tolerance / 3;
            var tol2 = 2 * tol1;
            if (Math.Abs(x - middle) <= tol2 - 0.5 * (b - a)) break;

            var useGolden = true;
            if (Math.Abs(e) > tol1)
            {
                // Try a parabola through x, w, v.
                var r = (x - w) * (fx - fv);
                var q = (x - v) * (fx - fw);
                var p = (x - v) * q - (x - w) * r;
                q = 2 * (q - r);
                if (q > 0) p = -p;
                else q = -q;
                var eOld = e;
                e = d;
                if (Math.Abs(p) < Math.Abs(0.5 * q * eOld) && p > q * (a - x) && p < q * (b - x))
                {
                    d = p / q;
                    var u0 = x + d;
                    if (u0 - a < tol2 || b - u0 < tol2) d = x < middle ? tol1 : -tol1;
                    useGolden = false;
                }
            }
            if (useGolden)
            {
                e = (x < middle ? b : a) - x;
                d = GoldenRatioComplement * e;
            }

            var u = Math.Abs(d) >= tol1 ? x + d : x + (d > 0 ? tol1 : -tol1);
            var fu = f(u);
            if (fu <= fx)
            {
                if (u < x) b = x;
                else a = x;
                (v, fv) = (w, fw);
                (w, fw) = (x, fx);
                (x, fx) = (u, fu);
            }
            else
            {
                if (u < x) a = u;
                else b = u;
                if (fu <= fw || w == x)
                {
                    (v, fv) = (w, fw);
                    (w, fw) = (u, fu);
                }
                else if (fu <= fv || v == x || v == w)
                {
                    (v, fv) = (u, fu);
                }
            }
        }
        return (x, fx);
    }
}
