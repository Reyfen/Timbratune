#if PROFILING
using System.Diagnostics;
using Reyfen.Timbratune.Acoustics.Numerics;

namespace Reyfen.Timbratune.Android;

/// <summary>
/// Profiling builds, flag "fftbench": times the FFT on the device (the current one against
/// the original stage-by-stage loop) and checks they agree bit for bit. Results go to Perf.
/// </summary>
internal static class FftBench
{
    public static void Run()
    {
        foreach (var p in new[] { 12, 16, 21 })
        {
            var n = 1 << p;
            var rng = new Random(p);
            var x = new double[n];
            for (var i = 0; i < n; i++) x[i] = rng.NextDouble() - 0.5;
            var cos = new double[n / 2];
            var sin = new double[n / 2];
            for (var k = 0; k < n / 2; k++)
            {
                var angle = 2 * Math.PI * k / n;
                cos[k] = Math.Cos(angle);
                sin[k] = Math.Sin(angle);
            }
            var reps = p == 21 ? 3 : 20;
            double[] r1 = [], r2 = [], i1 = [], i2 = [];
            double oldMs = double.MaxValue, newMs = double.MaxValue;
            for (var rep = 0; rep < reps; rep++)
            {
                r1 = (double[])x.Clone();
                i1 = new double[n];
                var sw = Stopwatch.StartNew();
                Original(r1, i1, cos, sin);
                oldMs = Math.Min(oldMs, sw.Elapsed.TotalMilliseconds);
                r2 = (double[])x.Clone();
                i2 = new double[n];
                sw.Restart();
                Fft.ForwardInPlace(r2, i2);
                newMs = Math.Min(newMs, sw.Elapsed.TotalMilliseconds);
            }
            var same = r1.AsSpan().SequenceEqual(r2) && i1.AsSpan().SequenceEqual(i2);
            Diagnostics.Perf.Note($"fft 2^{p}: original {oldMs:0.0} ms, current {newMs:0.0} ms, identical {same}");
        }
    }

    private static void Original(double[] re, double[] im, double[] cos, double[] sin)
    {
        var n = re.Length;
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
        for (var len = 2; len <= n; len <<= 1)
        {
            var half = len >> 1;
            var stride = n / len;
            for (var start = 0; start < n; start += len)
                for (var k = 0; k < half; k++)
                {
                    var wr = cos[k * stride];
                    var wi = -1.0 * sin[k * stride];
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
#endif
