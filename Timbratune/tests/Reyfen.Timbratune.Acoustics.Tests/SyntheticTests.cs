using System.Numerics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Numerics;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Voice;

namespace Reyfen.Timbratune.Acoustics.Tests;

/// <summary>Behaviour on signals with known answers — runs everywhere, no Praat needed.</summary>
public sealed class SyntheticTests
{
    private const double Fs = 44100;

    private static Sound Tone(double f, double seconds, double amplitude = 0.5, double silenceBefore = 0, double silenceAfter = 0)
    {
        var n = (int)((silenceBefore + seconds + silenceAfter) * Fs);
        var x = new double[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / Fs;
            if (t >= silenceBefore && t < silenceBefore + seconds) x[i] = amplitude * Math.Sin(2 * Math.PI * f * t);
        }
        return new Sound(x, Fs);
    }

    [Fact]
    public void FftMatchesNaiveDft()
    {
        var rng = new Random(1);
        const int n = 64;
        var re = Enumerable.Range(0, n).Select(_ => rng.NextDouble() - 0.5).ToArray();
        var im = new double[n];
        var input = (double[])re.Clone();
        Fft.ForwardInPlace(re, im);
        for (var k = 0; k < n; k++)
        {
            var sum = Complex.Zero;
            for (var j = 0; j < n; j++) sum += input[j] * Complex.FromPolarCoordinates(1, -2 * Math.PI * j * k / n);
            Assert.Equal(sum.Real, re[k], 10);
            Assert.Equal(sum.Imaginary, im[k], 10);
        }
        Fft.InverseInPlace(re, im);
        for (var j = 0; j < n; j++) Assert.Equal(input[j], re[j], 12);
    }

    [Fact]
    public void RootsOfKnownPolynomial()
    {
        // (z − 2)(z + 0.5) = z² − 1.5z − 1, times (z² − 2·0.9·cos(0.7)·z + 0.81):
        // roots 2, −0.5 and 0.9·e^(±0.7i). Coefficients are in ascending powers.
        var quadratic = new[] { 0.81, -2 * 0.9 * Math.Cos(0.7), 1 };
        var roots = Polynomial.Roots(Multiply([-1.0, -1.5, 1.0], quadratic));
        Assert.Contains(roots, r => Complex.Abs(r - 2) < 1e-12);
        Assert.Contains(roots, r => Complex.Abs(r + 0.5) < 1e-12);
        Assert.Contains(roots, r => Complex.Abs(r - Complex.FromPolarCoordinates(0.9, 0.7)) < 1e-12);
        Assert.Contains(roots, r => Complex.Abs(r - Complex.FromPolarCoordinates(0.9, -0.7)) < 1e-12);
    }

    private static double[] Multiply(double[] a, double[] b)
    {
        var c = new double[a.Length + b.Length - 1];
        for (var i = 0; i < a.Length; i++)
            for (var j = 0; j < b.Length; j++) c[i + j] += a[i] * b[j];
        return c;
    }

    [Fact]
    public void BurgRecoversAnAutoregressiveProcess()
    {
        double[] truth = [0, 1.3, -0.8, 0.2]; // x[n] = 1.3x[n−1] − 0.8x[n−2] + 0.2x[n−3] + e[n]
        var rng = new Random(7);
        var x = new double[20000];
        for (var n = 3; n < x.Length; n++)
            x[n] = truth[1] * x[n - 1] + truth[2] * x[n - 2] + truth[3] * x[n - 3] + (rng.NextDouble() - 0.5);
        var a = BurgLpc.Coefficients(x, 3);
        for (var k = 1; k <= 3; k++) Assert.Equal(truth[k], a[k], 1);
    }

    [Fact]
    public void PitchOfASineIsItsFrequency()
    {
        var pitch = PitchAnalyzer.Autocorrelation(Tone(200, 1.0), 0, 75, 500);
        Assert.Equal(200, pitch.Mean(), 1);
        Assert.True(pitch.StandardDeviation() < 0.1);
        Assert.All(Enumerable.Range(5, pitch.FrameCount - 10), i => Assert.True(pitch.IsVoiced(i)));
    }

    [Fact]
    public void SilenceIsUnvoicedAndHasNoPulses()
    {
        var silence = new Sound(new double[(int)Fs], Fs);
        var pitch = PitchAnalyzer.Autocorrelation(silence, 0, 75, 500);
        Assert.Empty(pitch.VoicedValues());
        Assert.Empty(PulseDetector.PeriodicCrossCorrelation(silence, pitch));
        Assert.True(double.IsNaN(HarmonicityAnalyzer.CrossCorrelation(silence).Mean()));
    }

    [Fact]
    public void PerfectlyPeriodicSignalHasNoJitterAndHighHarmonicity()
    {
        var sound = Tone(150, 1.0);
        var pitch = PitchAnalyzer.Autocorrelation(sound, 0, 75, 500);
        var pulses = PulseDetector.PeriodicCrossCorrelation(sound, pitch);
        Assert.InRange(pulses.Length, 140, 155);
        Assert.True(VoiceReport.JitterLocal(pulses) < 0.001);
        Assert.True(VoiceReport.ShimmerLocal(pulses, sound) < 0.01);
        Assert.True(HarmonicityAnalyzer.CrossCorrelation(sound).Mean() > 30);
    }

    [Fact]
    public void ToneBetweenSilencesIsOneSoundingInterval()
    {
        var sound = Tone(220, 0.5, silenceBefore: 0.4, silenceAfter: 0.4);
        var intervals = SilenceDetector.Detect(IntensityAnalyzer.Analyze(sound, 75));
        var sounding = intervals.Where(i => i.IsSounding).ToList();
        Assert.Single(sounding);
        Assert.InRange(sounding[0].Start, 0.35, 0.45);
        Assert.InRange(sounding[0].End, 0.85, 0.95);
    }

    [Fact]
    public void IntensityOfAFullScaleSineIsAbout91Db()
    {
        // RMS of a 1.0-amplitude sine = 0.707 → 20·log10(0.707 / 2e-5) ≈ 90.97 dB
        var intensity = IntensityAnalyzer.Analyze(Tone(1000, 1.0, amplitude: 1.0), 75);
        Assert.Equal(90.97, intensity.MeanEnergyDb(), 1);
    }

    [Fact]
    public void FormantsOfASyntheticVowel()
    {
        // Impulse train at 120 Hz through three resonators (700, 1200, 2600 Hz).
        var fs = 11000.0;
        var n = (int)(fs * 0.6);
        var x = new double[n];
        for (var i = 0; i < n; i += (int)(fs / 120)) x[i] = 1;
        foreach (var (f, bw) in new[] { (700.0, 80.0), (1200.0, 90.0), (2600.0, 120.0) })
        {
            var r = Math.Exp(-Math.PI * bw / fs);
            var c1 = 2 * r * Math.Cos(2 * Math.PI * f / fs);
            var c2 = -r * r;
            var y = new double[n];
            for (var i = 0; i < n; i++) y[i] = x[i] + (i >= 1 ? c1 * y[i - 1] : 0) + (i >= 2 ? c2 * y[i - 2] : 0);
            x = y;
        }
        var formants = FormantAnalyzer.Burg(new Sound(x, fs), 0, 5, 5500, 0.025, 50);
        var frame = formants.Frames[formants.Grid.Count / 2];
        // With 10 poles for 3 resonances the spare poles can land between them, so match each
        // true formant to the nearest estimate. LPC on a harmonic source is pulled towards nearby
        // harmonics; within ±60 Hz is the usual bias.
        foreach (var target in new[] { 700.0, 1200.0, 2600.0 })
        {
            var nearest = frame.MinBy(f => Math.Abs(f.Frequency - target));
            Assert.InRange(nearest.Frequency, target - 60, target + 60);
        }
    }

    [Fact]
    public void SincInterpolationReproducesSamplesAndIsLinearAtDepthOne()
    {
        double[] y = [0, 1, 4, 9, 16];
        Assert.Equal(4, SincInterpolator.Interpolate(y, 3, 70));
        Assert.Equal(6.5, SincInterpolator.Interpolate(y, 3.5, SincInterpolator.Linear));
        Assert.Equal(0, SincInterpolator.Interpolate(y, -2, 70)); // constant extrapolation
    }
}
