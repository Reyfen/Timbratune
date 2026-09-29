using Euphonia.Acoustics.Numerics;

namespace Euphonia.Acoustics.Spectral;

/// <summary>
/// Complex spectrum of a sound, bins 0..N/2 at k·Δf, scaled by the sampling
/// period so that values approximate the continuous Fourier transform
/// (units Pa/Hz for a signal in Pa).
/// </summary>
public sealed class Spectrum
{
    internal Spectrum(double[] re, double[] im, double binWidth, double nyquist, int fftLength, int sampleCount, double sampleStep)
    {
        Re = re;
        Im = im;
        BinWidth = binWidth;
        Nyquist = nyquist;
        FftLength = fftLength;
        SampleCount = sampleCount;
        SampleStep = sampleStep;
    }

    public IReadOnlyList<double> Re { get; }
    public IReadOnlyList<double> Im { get; }
    public int BinCount => Re.Count;
    public double BinWidth { get; }
    public double Nyquist { get; }
    internal int FftLength { get; }
    internal int SampleCount { get; }
    internal double SampleStep { get; }

    public double FrequencyOfBin(int bin) => bin * BinWidth;

    /// <summary>|X(f)|² of one bin.</summary>
    public double Power(int bin) => Re[bin] * Re[bin] + Im[bin] * Im[bin];

    /// <summary>
    /// Spectrum of the channel average, zero-padded to the next power of two
    /// (the "fast" option).
    /// </summary>
    public static Spectrum FromSound(Sound sound)
    {
        var n = Fft.NextPowerOfTwo(sound.SampleCount);
        var (re, im) = Fft.RealForward(sound.ToMono(), n);
        var dx = sound.Grid.Step;
        for (var k = 0; k < re.Length; k++)
        {
            re[k] *= dx;
            im[k] *= dx;
        }
        return new Spectrum(re, im, 1.0 / (dx * n), 0.5 / dx, n, sound.SampleCount, dx);
    }
}

/// <summary>
/// Long-term average spectrum: the spectral power density averaged over
/// bands of fixed width, in dB (Praat manual, Sound: To Ltas…).
/// </summary>
public sealed class Ltas
{
    private Ltas(double[] db, double bandwidth)
    {
        Db = db;
        Bandwidth = bandwidth;
    }

    /// <summary>One value per band; band i covers [i·bw, (i+1)·bw] and is centred at (i + ½)·bw.</summary>
    public IReadOnlyList<double> Db { get; }
    public double Bandwidth { get; }

    public static Ltas FromSound(Sound sound, double bandwidth) => FromSpectrum(Spectrum.FromSound(sound), bandwidth);

    public static Ltas FromSpectrum(Spectrum spectrum, double bandwidth)
    {
        if (bandwidth <= spectrum.BinWidth) throw new ArgumentException("The band width must exceed the bin width.");
        var bands = (int)Math.Ceiling(spectrum.Nyquist / bandwidth);
        var db = new double[bands];
        // Undo the zero padding so the level reflects the real signal length.
        var paddingCorrection = -10 * Math.Log10(spectrum.BinWidth * spectrum.SampleCount * spectrum.SampleStep);
        for (var b = 0; b < bands; b++)
        {
            var fmin = b * bandwidth;
            var fmax = Math.Min(fmin + bandwidth, spectrum.Nyquist);
            var power = BandMeanEnergyDensity(spectrum, fmin, fmax) * spectrum.BinWidth;
            db[b] = (power == 0 ? -300 : 10 * Math.Log10(power / 4e-10)) + paddingCorrection;
        }
        return new Ltas(db, bandwidth);
    }

    /// <summary>
    /// Mean of the energy density 2|X|² over [fmin, fmax], weighting each bin by
    /// how much of its ±½-bin extent falls inside the band.
    /// </summary>
    private static double BandMeanEnergyDensity(Spectrum s, double fmin, double fmax)
    {
        double Energy(int bin) => 2 * s.Power(bin);
        var n = s.BinCount;
        var rmin = fmin / s.BinWidth; // fractional 0-based bin positions
        var rmax = fmax / s.BinWidth;
        if (rmax < -0.5 || rmin >= n - 0.5) return double.NaN;
        var imin = rmin < -0.5 ? -1 : Stats.RoundHalfUp(rmin);
        var imax = rmax >= n - 0.5 ? n : Stats.RoundHalfUp(rmax);
        double sum = 0, weight = 0;
        for (var i = imin + 1; i < imax; i++)
        {
            sum += Energy(i);
            weight += 1;
        }
        if (imin == imax)
        {
            var w = rmax - rmin;
            sum += w * Energy(imin);
            weight += w;
        }
        else
        {
            if (imin >= 0)
            {
                var w = imin - rmin + 0.5;
                sum += w * Energy(imin);
                weight += w;
            }
            if (imax < n)
            {
                var w = rmax - imax + 0.5;
                sum += w * Energy(imax);
                weight += w;
            }
        }
        return sum / weight;
    }

    /// <summary>Value at frequency f, linearly interpolated (in dB) between band centres.</summary>
    public double ValueAtFrequency(double f)
    {
        var first = 0.5 * Bandwidth;
        var n = Db.Count;
        if (f < first - 0.5 * Bandwidth || f > first + (n - 0.5) * Bandwidth) return double.NaN;
        var index = (f - first) / Bandwidth; // 0-based fractional
        if (index <= 0) return Db[0];
        if (index >= n - 1) return Db[n - 1];
        var left = (int)Math.Floor(index);
        if (index == left) return Db[left];
        return Db[left] + (index - left) * (Db[left + 1] - Db[left]);
    }
}
