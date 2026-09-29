using Euphonia.Acoustics.Numerics;

namespace Euphonia.Acoustics.Formants;

/// <summary>
/// Sample-rate conversion: when downsampling, an ideal (brick-wall) low-pass
/// in the frequency domain removes everything above the new Nyquist
/// frequency; the new samples are then read off with windowed-sinc
/// interpolation (Boersma 1993, §3). The time domain is preserved.
/// </summary>
public static class Resampler
{
    public static Sound Resample(Sound sound, double newSamplingFrequency, int sincDepth = 50)
    {
        var grid = sound.Grid;
        var ratio = newSamplingFrequency * grid.Step;
        if (Math.Abs(ratio - 1) < 1e-6) return sound;

        var n = Stats.RoundHalfUp((grid.XMax - grid.XMin) * newSamplingFrequency);
        var newStep = 1.0 / newSamplingFrequency;
        // New samples are centred in the (unchanged) time domain.
        var newGrid = new TimeGrid(grid.XMin, grid.XMax, n, newStep, 0.5 * (grid.XMin + grid.XMax - (n - 1) * newStep));

        var channels = new double[sound.ChannelCount][];
        for (var ch = 0; ch < channels.Length; ch++)
        {
            ReadOnlySpan<double> source = sound.Channel(ch);
            if (ratio < 1) source = LowPass(source, ratio);
            var output = new double[n];
            for (var i = 0; i < n; i++)
                output[i] = SincInterpolator.Interpolate(source, grid.XToIndex(newGrid.IndexToX(i)) + 1, sincDepth);
            channels[ch] = output;
        }
        return new Sound(channels, newGrid);
    }

    /// <summary>
    /// Zeroes all spectral content above ratio × (old Nyquist). The signal is
    /// padded with 1000 zeros on each side (to the next power of two) so the
    /// circular transform doesn't wrap the edges into each other.
    /// </summary>
    private static double[] LowPass(ReadOnlySpan<double> samples, double ratio)
    {
        const int padding = 1000;
        var nfft = Fft.NextPowerOfTwo(samples.Length + 2 * padding);
        var re = new double[nfft];
        var im = new double[nfft];
        samples.CopyTo(re.AsSpan(padding));
        Fft.ForwardInPlace(re, im);

        // Cut-off expressed in the packed real-FFT layout [DC, Nyquist, Re1, Im1, Re2, Im2, …]
        // (1-based): every packed entry from `cut` on is zeroed, as is the Nyquist term.
        var cut = (int)Math.Floor(ratio * nfft);
        for (var k = 1; k < nfft / 2; k++)
        {
            var keepRe = 2 * k + 1 < cut;
            var keepIm = 2 * k + 2 < cut;
            if (!keepRe) { re[k] = 0; re[nfft - k] = 0; }
            if (!keepIm) { im[k] = 0; im[nfft - k] = 0; }
        }
        re[nfft / 2] = 0;
        im[nfft / 2] = 0;
        Fft.InverseInPlace(re, im);
        return re.AsSpan(padding, samples.Length).ToArray();
    }
}
