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
    public static void ForwardInPlace(double[] re, double[] im) => Transform(re, im, inverse: false);

    /// <summary>In-place complex inverse DFT including the 1/N factor.</summary>
    public static void InverseInPlace(double[] re, double[] im)
    {
        Transform(re, im, inverse: true);
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
        var re = new double[n];
        var im = new double[n];
        signal[..Math.Min(signal.Length, n)].CopyTo(re);
        ForwardInPlace(re, im);
        var bins = n / 2 + 1;
        var outRe = new double[bins];
        var outIm = new double[bins];
        Array.Copy(re, outRe, bins);
        Array.Copy(im, outIm, bins);
        outIm[0] = 0;
        if (n > 1) outIm[bins - 1] = 0;
        return (outRe, outIm);
    }

    private static void Transform(double[] re, double[] im, bool inverse)
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
