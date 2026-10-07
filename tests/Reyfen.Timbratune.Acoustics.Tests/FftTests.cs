using Reyfen.Timbratune.Acoustics.Numerics;

namespace Reyfen.Timbratune.Acoustics.Tests;

/// <summary>The half-length real transforms against the full complex FFT.</summary>
public sealed class FftTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(1024)]
    [InlineData(65536)]
    public void RealForwardMatchesTheComplexTransform(int n)
    {
        var rng = new Random(n);
        var x = Enumerable.Range(0, Math.Max(1, n - 3)).Select(_ => rng.NextDouble() - 0.5).ToArray(); // shorter: zero-padded
        var re = new double[n];
        var im = new double[n];
        x.CopyTo(re, 0);
        Fft.ForwardInPlace(re, im);

        var hr = new double[n / 2 + 1];
        var hi = new double[n / 2 + 1];
        Fft.RealForwardHalf(x, n, hr, hi);
        var scale = Math.Sqrt(n);
        for (var k = 0; k <= n / 2; k++)
        {
            Assert.Equal(re[k], hr[k], 1e-12 * scale);
            Assert.Equal(im[k], hi[k], 1e-12 * scale);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(16)]
    [InlineData(4096)]
    public void RealInverseUndoesRealForward(int n)
    {
        var rng = new Random(n + 1);
        var x = Enumerable.Range(0, n).Select(_ => rng.NextDouble() - 0.5).ToArray();
        var re = new double[n / 2 + 1];
        var im = new double[n / 2 + 1];
        Fft.RealForwardHalf(x, n, re, im);
        var back = new double[n];
        Fft.RealInverseHalf(re, im, n, back);
        for (var i = 0; i < n; i++) Assert.Equal(x[i], back[i], 1e-12);
    }
}
