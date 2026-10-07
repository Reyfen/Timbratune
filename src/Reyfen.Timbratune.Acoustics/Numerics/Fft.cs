namespace Reyfen.Timbratune.Acoustics.Numerics;

/// <summary>
/// Iterative radix-2 Cooley–Tukey FFT (Cooley &amp; Tukey 1965) for power-of-two
/// lengths, plus the real-signal conveniences the analyzers need. Forward
/// transforms use the e^(−iωt) sign and are unnormalized; <see cref="InverseInPlace"/>
/// divides by N so forward→inverse is the identity.
/// </summary>
public static class Fft
{
    /// <summary>Smallest power of two ≥ <paramref name="n"/> (and ≥ 1).</summary>
    public static int NextPowerOfTwo(int n)
    {
        var p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>In-place complex forward DFT: X[k] = Σ x[j]·e^(−2πijk/N).</summary>
    /// <param name="passDone">Called after each of the log2(N) butterfly passes (all equally costly).</param>
    public static void ForwardInPlace(double[] re, double[] im, Action? passDone = null) => Transform(re, im, false, passDone);

    /// <summary>In-place complex inverse DFT including the 1/N factor.</summary>
    /// <param name="passDone">Called after each of the log2(N) butterfly passes (all equally costly).</param>
    public static void InverseInPlace(double[] re, double[] im, Action? passDone = null)
    {
        Transform(re, im, true, passDone);
        var scale = 1.0 / re.Length;
        for (var i = 0; i < re.Length; i++)
        {
            re[i] *= scale;
            im[i] *= scale;
        }
    }

    /// <summary>
    /// Spectrum of a real signal zero-padded to <paramref name="n"/> (a power of two):
    /// returns bins 0..n/2 (n/2+1 values each; imaginary parts of DC and Nyquist are 0).
    /// </summary>
    public static (double[] Re, double[] Im) RealForward(ReadOnlySpan<double> signal, int n)
    {
        var bins = n / 2 + 1;
        var outRe = new double[bins];
        var outIm = new double[bins];
        if (n == 1)
        {
            outRe[0] = signal.Length > 0 ? signal[0] : 0;
            return (outRe, outIm);
        }
        RealForwardHalf(signal[..Math.Min(signal.Length, n)], n, outRe, outIm);
        outIm[0] = 0;
        outIm[bins - 1] = 0;
        return (outRe, outIm);
    }

    /// <summary>
    /// Bins 0..n/2 of the spectrum of a real signal (zero-padded or cut to <paramref name="n"/>,
    /// a power of two ≥ 2), from one complex transform of half the length: the even samples
    /// become the real parts and the odd ones the imaginary parts, and the two half-length
    /// spectra are separated afterwards (the standard real-input FFT, e.g. Press et al.,
    /// Numerical Recipes, §12.3). About half the work of a full complex transform.
    /// </summary>
    /// <param name="re">Receives Re X[k], k = 0..n/2 (length ≥ n/2 + 1).</param>
    /// <param name="im">Receives Im X[k], k = 0..n/2 (length ≥ n/2 + 1).</param>
    /// <param name="passDone">Called after each of the log2(n/2) butterfly passes.</param>
    public static void RealForwardHalf(ReadOnlySpan<double> signal, int n, double[] re, double[] im, Action? passDone = null)
    {
        var m = n / 2;
        var zr = RentExact(m);
        var zi = RentExact(m);
        try
        {
            for (var j = 0; j < m; j++)
            {
                zr[j] = 2 * j < signal.Length ? signal[2 * j] : 0;
                zi[j] = 2 * j + 1 < signal.Length ? signal[2 * j + 1] : 0;
            }
            Transform(zr, zi, false, passDone);
            var twiddles = Twiddles.For(n);
            for (var k = 0; k <= m; k++)
            {
                int a = k % m, b = (m - k) % m;
                // E = (Z[k] + conj Z[m−k]) / 2 (even samples), O = (Z[k] − conj Z[m−k]) / 2i (odd samples).
                double er = 0.5 * (zr[a] + zr[b]), ei = 0.5 * (zi[a] - zi[b]);
                double odr = 0.5 * (zi[a] + zi[b]), odi = -0.5 * (zr[a] - zr[b]);
                // X[k] = E + e^(−2πik/n)·O
                double wr = k < m ? twiddles.Cos[k] : -1, wi = k < m ? -twiddles.Sin[k] : 0;
                re[k] = er + (wr * odr - wi * odi);
                im[k] = ei + (wr * odi + wi * odr);
            }
        }
        finally
        {
            ReturnExact(zr);
            ReturnExact(zi);
        }
    }

    /// <summary>
    /// The real signal (length <paramref name="n"/>) whose spectrum bins 0..n/2 are given
    /// (the rest follow by conjugate symmetry), with the 1/n factor: the inverse of
    /// <see cref="RealForwardHalf"/>, again through one half-length complex transform.
    /// </summary>
    /// <param name="signal">Receives the n samples (length ≥ n).</param>
    public static void RealInverseHalf(double[] re, double[] im, int n, double[] signal, Action? passDone = null)
    {
        var m = n / 2;
        var zr = RentExact(m);
        var zi = RentExact(m);
        try
        {
            var twiddles = Twiddles.For(n);
            for (var k = 0; k < m; k++)
            {
                var b = m - k;
                // E = (X[k] + conj X[m−k]) / 2, O = (X[k] − conj X[m−k]) / 2 · e^(+2πik/n); Z[k] = E + i·O.
                double er = 0.5 * (re[k] + re[b]), ei = 0.5 * (im[k] - im[b]);
                double dr = 0.5 * (re[k] - re[b]), di = 0.5 * (im[k] + im[b]);
                double wr = twiddles.Cos[k], wi = twiddles.Sin[k];
                double odr = dr * wr - di * wi, odi = dr * wi + di * wr;
                zr[k] = er - odi;
                zi[k] = ei + odr;
            }
            InverseInPlace(zr, zi, passDone);
            for (var j = 0; j < m; j++)
            {
                signal[2 * j] = zr[j];
                signal[2 * j + 1] = zi[j];
            }
        }
        finally
        {
            ReturnExact(zr);
            ReturnExact(zi);
        }
    }

    /// <summary>A pooled array of exactly <paramref name="length"/> (the transforms take their size from the array).</summary>
    private static double[] RentExact(int length)
    {
        var array = System.Buffers.ArrayPool<double>.Shared.Rent(length);
        if (array.Length == length) return array;
        System.Buffers.ArrayPool<double>.Shared.Return(array);
        return new double[length];
    }

    /// <summary>Back to the pool — only sizes it holds (powers of two from 16): smaller ones were allocated, not rented.</summary>
    private static void ReturnExact(double[] array)
    {
        if (array.Length >= 16) System.Buffers.ArrayPool<double>.Shared.Return(array);
    }

    /// <summary>Number of butterfly passes of a length-<paramref name="n"/> transform (log2 n).</summary>
    public static int PassCount(int n) => System.Numerics.BitOperations.Log2((uint)n);

    private static void Transform(double[] re, double[] im, bool inverse, Action? passDone)
    {
        var n = re.Length;
        if (n != im.Length) throw new ArgumentException("Real and imaginary parts differ in length.");
        if (!IsPowerOfTwo(n)) throw new ArgumentException($"FFT length {n} is not a power of two.");
        if (n == 1) return;

        // bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        var twiddles = Twiddles.For(n);
        var sign = inverse ? 1.0 : -1.0;
        for (var len = 2; len <= n; len <<= 1)
        {
            var half = len >> 1;
            var stride = n / len;
            for (var start = 0; start < n; start += len)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = twiddles.Cos[k * stride];
                    var wi = sign * twiddles.Sin[k * stride];
                    var a = start + k;
                    var b = a + half;
                    var tr = re[b] * wr - im[b] * wi;
                    var ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
            }
            passDone?.Invoke();
        }
    }

    /// <summary>cos/sin(2πk/N) for k &lt; N/2, computed directly (no recurrences) and cached per N.</summary>
    private sealed class Twiddles
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, Twiddles> Cache = new();
        public double[] Cos { get; }
        public double[] Sin { get; }

        private Twiddles(int n)
        {
            Cos = new double[n / 2];
            Sin = new double[n / 2];
            for (var k = 0; k < n / 2; k++)
            {
                var angle = 2 * Math.PI * k / n;
                Cos[k] = Math.Cos(angle);
                Sin[k] = Math.Sin(angle);
            }
        }

        public static Twiddles For(int n) => Cache.GetOrAdd(n, static len => new Twiddles(len));
    }
}
