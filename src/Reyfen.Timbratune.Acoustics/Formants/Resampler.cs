using Reyfen.Timbratune.Acoustics.Numerics;

namespace Reyfen.Timbratune.Acoustics.Formants;

/// <summary>
/// Sample-rate conversion: when downsampling, an ideal (brick-wall) low-pass
/// in the frequency domain removes everything above the new Nyquist
/// frequency; the new samples are then read off with windowed-sinc
/// interpolation (Boersma 1993, §3). The time domain is preserved.
/// </summary>
public static class Resampler
{
    /// <summary>Output samples per parallel work item.</summary>
    private const int Chunk = 4096;

    /// <param name="progress">Called with the fraction resampled so far (0–1), from any thread.</param>
    public static Sound Resample(Sound sound, double newSamplingFrequency, int sincDepth = 50, Action<double>? progress = null)
    {
        var ratio = newSamplingFrequency * sound.Grid.Step;
        if (Math.Abs(ratio - 1) < 1e-6) return sound;
        return ResampleAll(sound, [newSamplingFrequency], sincDepth, progress)[0];
    }

    /// <summary>
    /// The same sound resampled to several rates, sharing the work they have in common: the
    /// forward transform of each channel is computed once and only the cut-off and the inverse
    /// transform are repeated per rate (the formant analysis needs 10 and 11 kHz versions of
    /// every take). Each result is identical to <see cref="Resample"/> at that rate.
    /// </summary>
    public static Sound[] ResampleAll(Sound sound, IReadOnlyList<double> newSamplingFrequencies, int sincDepth = 50,
        Action<double>? progress = null)
    {
        var grid = sound.Grid;
        var rates = newSamplingFrequencies.Count;
        var results = new double[rates][][];
        for (var r = 0; r < rates; r++) results[r] = new double[sound.ChannelCount][];
        // Shares of the time per channel: the forward transform, then per rate the filter +
        // inverse transform (~90% together at 44.1 → 11 kHz) and the interpolation.
        var forwardShare = 0.3;
        for (var ch = 0; ch < sound.ChannelCount; ch++)
        {
            var offset = (double)ch / sound.ChannelCount;
            Action<double>? Part(double from, double share) => progress is null ? null
                : f => progress(offset + (from + share * f) / sound.ChannelCount);
            var channel = sound.Channel(ch).ToArray();
            var anyLowPass = newSamplingFrequencies.Any(f => f * grid.Step < 1);
            using var spectrum = anyLowPass ? Spectrum.Of(channel, Part(0, forwardShare)) : null;
            // After the shared forward transform, the rates are independent: in parallel.
            var shares = new double[rates];
            Parallel.For(0, rates, r =>
            {
                Action<double>? RatePart(double from, double share) => progress is null ? null : f =>
                {
                    lock (shares)
                    {
                        shares[r] = Math.Max(shares[r], from + share * f);
                        Part(forwardShare, 1 - forwardShare)!(shares.Average());
                    }
                };
                var ratio = newSamplingFrequencies[r] * grid.Step;
                var source = ratio < 1 ? spectrum!.LowPassed(ratio, RatePart(0, 0.9)) : channel.ToArray();
                results[r][ch] = Interpolate(source, grid, newSamplingFrequencies[r], sincDepth, RatePart(0.9, 0.1));
            });
        }
        return results.Select((channels, r) => new Sound(channels, NewGrid(grid, newSamplingFrequencies[r]))).ToArray();
    }

    private static TimeGrid NewGrid(TimeGrid grid, double newSamplingFrequency)
    {
        var n = Stats.RoundHalfUp((grid.XMax - grid.XMin) * newSamplingFrequency);
        var newStep = 1.0 / newSamplingFrequency;
        // New samples are centred in the (unchanged) time domain.
        return new TimeGrid(grid.XMin, grid.XMax, n, newStep, 0.5 * (grid.XMin + grid.XMax - (n - 1) * newStep));
    }

    /// <summary>The new samples read off <paramref name="source"/> with windowed-sinc interpolation.</summary>
    private static double[] Interpolate(double[] source, TimeGrid grid, double newSamplingFrequency, int sincDepth, Action<double>? progress)
    {
        var newGrid = NewGrid(grid, newSamplingFrequency);
        var n = newGrid.Count;
        var chunks = (n + Chunk - 1) / Chunk;
        var counter = new FrameProgress(chunks, progress);
        var output = new double[n];
        // Every output sample is independent, so chunks run in parallel with identical results.
        Parallel.For(0, chunks, c =>
        {
            var end = Math.Min(n, (c + 1) * Chunk);
            for (var i = c * Chunk; i < end; i++)
                output[i] = SincInterpolator.Interpolate(source, grid.XToIndex(newGrid.IndexToX(i)) + 1, sincDepth);
            counter.Done();
        });
        return output;
    }

    /// <summary>
    /// The spectrum of one channel, padded with 1000 zeros on each side (to the next power of
    /// two) so the circular transform doesn't wrap the edges into each other. The signal is
    /// real, so only bins 0..n/2 are kept, computed with half-length transforms.
    /// </summary>
    private sealed class Spectrum : IDisposable
    {
        private const int Padding = 1000;
        private readonly int _length;
        private readonly int _nfft;
        private readonly double[] _re;
        private readonly double[] _im;

        private Spectrum(int length, int nfft, double[] re, double[] im)
        {
            _length = length;
            _nfft = nfft;
            _re = re;
            _im = im;
        }

        public static Spectrum Of(ReadOnlySpan<double> samples, Action<double>? progress)
        {
            var nfft = Fft.NextPowerOfTwo(samples.Length + 2 * Padding);
            var bins = nfft / 2 + 1;
            var padded = RentCleared(nfft);
            var re = System.Buffers.ArrayPool<double>.Shared.Rent(bins);
            var im = System.Buffers.ArrayPool<double>.Shared.Rent(bins);
            try
            {
                samples.CopyTo(padded.AsSpan(Padding));
                var passes = Fft.PassCount(nfft / 2);
                var done = 0;
                Fft.RealForwardHalf(padded, nfft, re, im, progress is null ? null : () => progress((double)++done / passes));
                return new Spectrum(samples.Length, nfft, re, im);
            }
            finally
            {
                Release(padded);
            }
        }

        /// <summary>The signal with all spectral content above ratio × (old Nyquist) zeroed (a brick-wall low-pass).</summary>
        public double[] LowPassed(double ratio, Action<double>? progress)
        {
            var nfft = _nfft;
            var bins = nfft / 2 + 1;
            var re = System.Buffers.ArrayPool<double>.Shared.Rent(bins);
            var im = System.Buffers.ArrayPool<double>.Shared.Rent(bins);
            var padded = RentCleared(nfft);
            try
            {
                Array.Copy(_re, re, bins);
                Array.Copy(_im, im, bins);
                // Cut-off expressed in the packed real-FFT layout [DC, Nyquist, Re1, Im1, Re2, Im2, …]
                // (1-based): every packed entry from `cut` on is zeroed, as is the Nyquist term.
                var cut = (int)Math.Floor(ratio * nfft);
                for (var k = 1; k < nfft / 2; k++)
                {
                    if (2 * k + 1 >= cut) re[k] = 0;
                    if (2 * k + 2 >= cut) im[k] = 0;
                }
                re[nfft / 2] = 0;
                im[nfft / 2] = 0;
                im[0] = 0;
                var passes = Fft.PassCount(nfft / 2);
                var done = 0;
                Fft.RealInverseHalf(re, im, nfft, padded, progress is null ? null : () => progress((double)++done / passes));
                return padded.AsSpan(Padding, _length).ToArray();
            }
            finally
            {
                Release(padded);
                System.Buffers.ArrayPool<double>.Shared.Return(re);
                System.Buffers.ArrayPool<double>.Shared.Return(im);
            }
        }

        public void Dispose()
        {
            System.Buffers.ArrayPool<double>.Shared.Return(_re);
            System.Buffers.ArrayPool<double>.Shared.Return(_im);
        }
    }

    /// <summary>A zeroed array of exactly <paramref name="length"/> (a power of two, so the pool has that size).</summary>
    private static double[] RentCleared(int length)
    {
        var array = System.Buffers.ArrayPool<double>.Shared.Rent(length);
        if (array.Length != length)
        {
            System.Buffers.ArrayPool<double>.Shared.Return(array);
            return new double[length];
        }
        Array.Clear(array);
        return array;
    }

    /// <summary>Back to the pool — only sizes it holds (powers of two from 16): smaller ones were allocated, not rented.</summary>
    private static void Release(double[] array)
    {
        if (array.Length >= 16) System.Buffers.ArrayPool<double>.Shared.Return(array);
    }
}
