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
    /// <param name="outputRange">
    /// Only the new samples with From ≤ t ≤ To (s, in the sound's time) are computed; the others are 0.
    /// For a caller that reads only part of the result (the live formant tracker keeps the middle of
    /// each block); the computed samples are identical to the whole resampling's.
    /// </param>
    /// <param name="halfLength">
    /// When every new rate is below a quarter of the old one: the low-passed signal is computed at half the
    /// old rate (every second sample, from an inverse transform of half the length, about half the work)
    /// and the new samples are interpolated from that. The low-pass is the same; the interpolation reads a
    /// signal sampled less densely, so the result differs very slightly from the full-length one.
    /// </param>
    public static Sound[] ResampleAll(Sound sound, IReadOnlyList<double> newSamplingFrequencies, int sincDepth = 50,
        Action<double>? progress = null, (double From, double To)? outputRange = null, bool halfLength = false)
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
            // The spectrum is taken straight from the channel; a copy is only needed for a rate
            // that doesn't low-pass (none in the formant analysis).
            var anyLowPass = newSamplingFrequencies.Any(f => f * grid.Step < 1);
            var channel = newSamplingFrequencies.Any(f => f * grid.Step >= 1) ? sound.Channel(ch).ToArray() : null;
            using var spectrum = anyLowPass ? Spectrum.Of(sound.Channel(ch), Part(0, forwardShare)) : null;
            // After the shared forward transform, the rates are independent: in parallel.
            var shares = new double[rates];
            Parallel.For(0, rates, Parallelism.Options, r =>
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
                if (ratio < 1)
                {
                    // The filtered signal is only needed for the interpolation: a pooled buffer.
                    var half = halfLength && ratio < 0.5;
                    var (buffer, start, length) = half ? spectrum!.LowPassedHalf(ratio, RatePart(0, 0.9)) : spectrum!.LowPassed(ratio, RatePart(0, 0.9));
                    try
                    {
                        // At half the rate the samples are the even ones: from the first, every 2 × the step.
                        var source = half ? grid with { Count = length, Step = 2 * grid.Step } : grid;
                        results[r][ch] = Interpolate(buffer, start, length, source, newSamplingFrequencies[r], sincDepth, RatePart(0.9, 0.1),
                            outputRange, grid);
                    }
                    finally
                    {
                        Release(buffer);
                    }
                }
                else results[r][ch] = Interpolate(channel!, 0, channel!.Length, grid, newSamplingFrequencies[r], sincDepth, RatePart(0.9, 0.1), outputRange, grid);
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
    /// <param name="source">The signal: <paramref name="length"/> samples from <paramref name="start"/>, on <paramref name="grid"/>.</param>
    /// <param name="original">The sound's own grid, which the new grid is made from.</param>
    private static double[] Interpolate(double[] source, int start, int length, TimeGrid grid, double newSamplingFrequency, int sincDepth,
        Action<double>? progress, (double From, double To)? outputRange, TimeGrid original)
    {
        var newGrid = NewGrid(original, newSamplingFrequency);
        var n = newGrid.Count;
        var (first, last) = (0, n);
        if (outputRange is { } range)
        {
            first = Math.Clamp((int)Math.Floor(newGrid.XToIndex(range.From)), 0, n);
            last = Math.Clamp((int)Math.Ceiling(newGrid.XToIndex(range.To)) + 1, first, n);
        }
        var chunks = (last - first + Chunk - 1) / Chunk;
        var counter = new FrameProgress(chunks, progress);
        var output = new double[n];
        // Every output sample is independent, so chunks run in parallel with identical results.
        Parallel.For(0, chunks, Parallelism.Options, c =>
        {
            var end = Math.Min(last, first + (c + 1) * Chunk);
            for (var i = first + c * Chunk; i < end; i++)
                output[i] = SincInterpolator.Interpolate(source.AsSpan(start, length), grid.XToIndex(newGrid.IndexToX(i)) + 1, sincDepth);
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

        /// <summary>
        /// The signal with all spectral content above ratio × (old Nyquist) zeroed (a brick-wall
        /// low-pass): <c>Length</c> samples from <c>Start</c> in a pooled <c>Buffer</c>, which the
        /// caller gives back with <see cref="Release"/>.
        /// </summary>
        public (double[] Buffer, int Start, int Length) LowPassed(double ratio, Action<double>? progress)
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
                return (padded, Padding, _length);
            }
            catch
            {
                Release(padded);
                throw;
            }
            finally
            {
                System.Buffers.ArrayPool<double>.Shared.Return(re);
                System.Buffers.ArrayPool<double>.Shared.Return(im);
            }
        }

        /// <summary>
        /// The same low-passed signal at half the rate (its even samples), from an inverse transform of half
        /// the length: with nothing left above a quarter of the bins, the half-length spectrum is the kept bins
        /// (halved, for the transform's 1/n). Needs <paramref name="ratio"/> &lt; 0.5. <c>Length</c> samples
        /// from <c>Start</c> (the even samples of the signal, the first being its first) in a pooled <c>Buffer</c>.
        /// </summary>
        public (double[] Buffer, int Start, int Length) LowPassedHalf(double ratio, Action<double>? progress)
        {
            var nfft = _nfft;
            var half = nfft / 2;
            var bins = half / 2 + 1;
            var re = System.Buffers.ArrayPool<double>.Shared.Rent(bins);
            var im = System.Buffers.ArrayPool<double>.Shared.Rent(bins);
            var padded = RentCleared(half);
            try
            {
                // The same cut-off as LowPassed, on the full spectrum's bins 0..nfft/4 (all above it are zero).
                var cut = (int)Math.Floor(ratio * nfft);
                for (var k = 0; k < bins; k++)
                {
                    re[k] = 0.5 * _re[k];
                    im[k] = 0.5 * _im[k];
                }
                for (var k = 1; k < bins; k++)
                {
                    if (2 * k + 1 >= cut) re[k] = 0;
                    if (2 * k + 2 >= cut) im[k] = 0;
                }
                re[bins - 1] = 0;
                im[bins - 1] = 0;
                im[0] = 0;
                var passes = Fft.PassCount(half / 2);
                var done = 0;
                Fft.RealInverseHalf(re, im, half, padded, progress is null ? null : () => progress((double)++done / passes));
                // Padding is even, so the signal's first sample is an even one.
                return (padded, Padding / 2, (_length + 1) / 2);
            }
            catch
            {
                Release(padded);
                throw;
            }
            finally
            {
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
