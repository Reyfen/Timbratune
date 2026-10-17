using Reyfen.Timbratune.Acoustics.Formants;
using Xunit.Abstractions;

namespace Reyfen.Timbratune.Acoustics.Tests;

/// <summary>The resampler's shortcuts: a part of the output, and the low-pass computed at half the rate.</summary>
public sealed class ResamplerTests(ITestOutputHelper output)
{
    private static Sound Fixture(string clip)
    {
        using var s = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", clip + ".wav"));
        return WavDecoder.Decode(s);
    }

    private static readonly double[] Rates = [9000, 10000, 11000, 12000, 13000];

    [Theory]
    [InlineData("vctk_f294")]
    [InlineData("vctk_m311")]
    public void TheHalfRateLowPassGivesNearlyTheSameSamples(string clip)
    {
        var sound = Fixture(clip);
        var full = Resampler.ResampleAll(sound, Rates);
        var half = Resampler.ResampleAll(sound, Rates, halfLength: true);
        for (var r = 0; r < Rates.Length; r++)
        {
            var a = full[r].Channel(0).ToArray();
            var b = half[r].Channel(0).ToArray();
            Assert.Equal(full[r].Grid, half[r].Grid);
            // Away from the ends (where the interpolation runs out of samples on one side).
            double signal = 0, error = 0, worst = 0;
            for (var i = 200; i < a.Length - 200; i++)
            {
                signal += a[i] * a[i];
                error += (a[i] - b[i]) * (a[i] - b[i]);
                worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
            }
            var snr = 10 * Math.Log10(signal / error);
            output.WriteLine($"{clip} at {Rates[r]} Hz: error {snr:F1} dB below the signal, worst sample {worst:E2}");
            // Measured 120.6–121.3 dB: far below a 16-bit recording's own quantisation (about 96 dB).
            Assert.True(snr > 100, $"{Rates[r]} Hz: {snr:F1} dB");
        }
    }

    [Fact]
    public void APartOfTheOutputIsTheSameAsInTheWhole()
    {
        var sound = Fixture("vctk_f339");
        var whole = Resampler.ResampleAll(sound, [11000.0])[0].Channel(0).ToArray();
        var part = Resampler.ResampleAll(sound, [11000.0], outputRange: (2.0, 3.5))[0];
        var p = part.Channel(0).ToArray();
        for (var i = 0; i < p.Length; i++)
        {
            var t = part.Grid.IndexToX(i);
            if (t >= 2.0 && t <= 3.5) Assert.Equal(whole[i], p[i]);
            else if (t < 1.99 || t > 3.51) Assert.Equal(0, p[i]);
        }
    }
}
