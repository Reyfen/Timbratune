using Euphonia.Acoustics.Numerics;

namespace Euphonia.Acoustics.Pitch;

/// <summary>Settings of the candidate search and the path finder (defaults = Praat's "To Pitch (ac)…").</summary>
public sealed record PitchSettings
{
    public int MaxCandidates { get; init; } = 15;
    public double SilenceThreshold { get; init; } = 0.03;
    public double VoicingThreshold { get; init; } = 0.45;
    public double OctaveCost { get; init; } = 0.01;
    public double OctaveJumpCost { get; init; } = 0.35;
    public double VoicedUnvoicedCost { get; init; } = 0.14;
}

/// <summary>
/// Short-term periodicity analysis after Boersma (1993):
/// <list type="bullet">
/// <item><b>Autocorrelation</b>: each frame is mean-removed, Hanning-windowed and
/// autocorrelated via the FFT; dividing by the window's own autocorrelation
/// removes the window's taper from the lag curve (Boersma 1993, eq. 9).</item>
/// <item><b>Cross-correlation</b>: the normalized correlation between a window and
/// its lagged copy, without tapering (used for harmonicity).</item>
/// </list>
/// Local maxima of the correlation become candidates (frequency by parabolic
/// interpolation, strength by sinc interpolation, then both refined by
/// maximizing the sinc interpolant); <see cref="PitchPath"/> picks the path.
/// </summary>
public static class PitchAnalyzer
{
    internal enum Method { AutocorrelationHanning, CrossCorrelationAccurate }

    /// <summary>
    /// Autocorrelation pitch analysis (Praat: Sound: To Pitch (ac)… with
    /// "very accurate" off, i.e. 3 periods per Hanning window).
    /// </summary>
    /// <param name="timeStep">0 = 0.75 / pitchFloor.</param>
    public static PitchContour Autocorrelation(Sound sound, double timeStep, double pitchFloor, double pitchCeiling,
        PitchSettings? settings = null)
    {
        settings ??= new PitchSettings();
        return Analyze(sound, Method.AutocorrelationHanning, 3.0, timeStep, pitchFloor, pitchCeiling, settings);
    }

    internal static PitchContour Analyze(Sound sound, Method method, double periodsPerWindow, double timeStep,
        double pitchFloor, double pitchCeiling, PitchSettings settings)
    {
        var grid = sound.Grid;
        var dx = grid.Step;
        var maxCandidates = settings.MaxCandidates;
        if (maxCandidates < pitchCeiling / pitchFloor) maxCandidates = (int)Math.Floor(pitchCeiling / pitchFloor);
        if (timeStep <= 0) timeStep = periodsPerWindow / pitchFloor / 4.0;

        var accurate = method == Method.CrossCorrelationAccurate;
        var interpolationDepth = accurate ? 1.0 : 0.5;
        var refineDepth = accurate ? PeakRefinement.SincDepth700 : PeakRefinement.SincDepth70;

        var duration = dx * grid.Count;
        if (pitchFloor < periodsPerWindow / duration)
            throw new ArgumentException($"The pitch floor must be at least {periodsPerWindow / duration} Hz for this sound.");

        // Longest period in samples: the local mean looks one period each way, the local peak half a period.
        var periodSamples = (int)Math.Floor(1.0 / dx / pitchFloor);
        var halfPeriodSamples = periodSamples / 2 + 1;
        pitchCeiling = Math.Min(pitchCeiling, 0.5 / dx);

        var windowDuration = periodsPerWindow / pitchFloor;
        var halfWindowSamples = (int)Math.Floor(windowDuration / dx) / 2 - 1;
        if (halfWindowSamples < 2) throw new ArgumentException("The analysis window is too short.");
        var windowSamples = 2 * halfWindowSamples;
        var maximumLag = Math.Min((int)Math.Floor(windowSamples / periodsPerWindow) + 2, windowSamples);

        var frames = TimeGrid.ShortTermFrames(grid, accurate ? 1.0 / pitchFloor + windowDuration : windowDuration, timeStep);

        var globalPeak = 0.0;
        for (var ch = 0; ch < sound.ChannelCount; ch++)
        {
            var samples = sound.Channel(ch);
            var mean = Stats.Mean(samples);
            foreach (var s in samples) globalPeak = Math.Max(globalPeak, Math.Abs(s - mean));
        }

        var result = new PitchFrame[frames.Count];
        if (globalPeak == 0)
        {
            for (var i = 0; i < result.Length; i++) result[i] = new PitchFrame(0, [new PitchCandidate(0, 0)]);
            return new PitchContour(frames, result, pitchCeiling);
        }

        var lagRange = (int)Math.Floor(windowSamples * interpolationDepth); // largest lag kept in r
        var context = new FrameContext(sound, method, pitchFloor, maxCandidates, settings.VoicingThreshold, settings.OctaveCost,
            windowDuration, windowSamples, halfWindowSamples, maximumLag, periodSamples, halfPeriodSamples, lagRange, refineDepth,
            globalPeak);
        if (!accurate) context.PrepareAutocorrelation(interpolationDepth);

        Parallel.For(0, frames.Count, () => new FrameBuffers(context),
            (i, _, buffers) =>
            {
                result[i] = context.AnalyzeFrame(frames.IndexToX(i), buffers);
                return buffers;
            },
            _ => { });

        var contour = new PitchContour(frames, result, pitchCeiling);
        PitchPath.Choose(contour, settings.SilenceThreshold, settings.VoicingThreshold, settings.OctaveCost,
            settings.OctaveJumpCost, settings.VoicedUnvoicedCost, pitchCeiling);
        return contour;
    }

    /// <summary>Per-thread scratch space.</summary>
    private sealed class FrameBuffers(FrameContext c)
    {
        public readonly double[][] Frame = Enumerable.Range(0, c.Sound.ChannelCount).Select(_ => new double[c.WindowSamples]).ToArray();
        public readonly double[] FftRe = new double[Math.Max(c.FftLength, 1)];
        public readonly double[] FftIm = new double[Math.Max(c.FftLength, 1)];
        public readonly double[] Power = new double[Math.Max(c.FftLength, 1)];
        /// <summary>Correlation for lags −lagRange..lagRange; index = lag + lagRange.</summary>
        public readonly double[] R = new double[2 * c.LagRange + 1];
        public readonly double[] LocalMean = new double[c.Sound.ChannelCount];
        // Cross-correlation mode only (length 1 otherwise).
        public readonly double[] CrossRe = new double[c.CrossFftLength];
        public readonly double[] CrossIm = new double[c.CrossFftLength];
        public readonly double[] SpanRe = new double[c.CrossFftLength];
        public readonly double[] SpanIm = new double[c.CrossFftLength];
        public readonly double[] ProductRe = new double[c.CrossFftLength];
        public readonly double[] ProductIm = new double[c.CrossFftLength];
    }

    private sealed class FrameContext(
        Sound sound, Method method, double pitchFloor, int maxCandidates, double voicingThreshold, double octaveCost,
        double windowDuration, int windowSamples, int halfWindowSamples, int maximumLag, int periodSamples,
        int halfPeriodSamples, int lagRange, int refineDepth, double globalPeak)
    {
        public Sound Sound { get; } = sound;
        public int WindowSamples { get; } = windowSamples;
        public int LagRange { get; } = lagRange;
        public int FftLength { get; private set; }
        /// <summary>FFT length for cross-correlation: holds the window plus the longest span without wrap-around.</summary>
        public int CrossFftLength { get; } =
            method == Method.CrossCorrelationAccurate ? Fft.NextPowerOfTwo(2 * windowSamples + maximumLag) : 1;
        private double[] _window = [];
        private double[] _windowAutocorrelation = [];
        private readonly double _dx = sound.Grid.Step;

        public void PrepareAutocorrelation(double interpolationDepth)
        {
            FftLength = Fft.NextPowerOfTwo((int)Math.Ceiling(WindowSamples * (1 + interpolationDepth)));
            while (FftLength < WindowSamples * (1 + interpolationDepth)) FftLength *= 2;
            _window = new double[WindowSamples];
            for (var i = 0; i < WindowSamples; i++)
                _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 1) / (WindowSamples + 1)); // Hanning, zero just outside
            _windowAutocorrelation = Autocorrelate([_window], FftLength, new double[FftLength], new double[FftLength], new double[FftLength]);
            var zeroLag = _windowAutocorrelation[0];
            for (var i = 0; i < _windowAutocorrelation.Length; i++) _windowAutocorrelation[i] /= zeroLag;
        }

        /// <summary>Unnormalized autocorrelation of (the sum over channels of) zero-padded signals, via |FFT|².</summary>
        private static double[] Autocorrelate(double[][] signals, int n, double[] re, double[] im, double[] power)
        {
            Array.Clear(power);
            foreach (var signal in signals)
            {
                Array.Clear(re);
                Array.Clear(im);
                signal.AsSpan().CopyTo(re);
                Fft.ForwardInPlace(re, im);
                for (var k = 0; k < n; k++) power[k] += re[k] * re[k] + im[k] * im[k];
            }
            power.AsSpan().CopyTo(re);
            Array.Clear(im);
            Fft.InverseInPlace(re, im);
            return (double[])re.Clone();
        }

        public PitchFrame AnalyzeFrame(double t, FrameBuffers b)
        {
            var grid = Sound.Grid;
            var n = grid.Count;
            // 1-based sample numbers, as in the frame-placement description.
            var leftSample = (int)Math.Floor(grid.XToIndex(t) + 1);
            var rightSample = leftSample + 1;
            var accurate = method == Method.CrossCorrelationAccurate;

            for (var ch = 0; ch < Sound.ChannelCount; ch++)
            {
                var z = Sound.ChannelArray(ch);
                var sum = 0.0;
                for (var i = rightSample - periodSamples; i <= leftSample + periodSamples; i++) sum += z[i - 1];
                b.LocalMean[ch] = sum / (2 * periodSamples);

                var start = rightSample - halfWindowSamples;
                var frame = b.Frame[ch];
                for (var j = 0; j < WindowSamples; j++)
                {
                    var v = z[start + j - 1] - b.LocalMean[ch];
                    frame[j] = accurate ? v : v * _window[j];
                }
            }

            // Local peak over half a longest period around the frame centre.
            var peakFrom = Math.Max(1, halfWindowSamples + 1 - halfPeriodSamples);
            var peakTo = Math.Min(WindowSamples, halfWindowSamples + halfPeriodSamples);
            var localPeak = 0.0;
            foreach (var frame in b.Frame)
                for (var j = peakFrom; j <= peakTo; j++)
                    localPeak = Math.Max(localPeak, Math.Abs(frame[j - 1]));
            var intensity = localPeak > globalPeak ? 1.0 : localPeak / globalPeak;

            var r = b.R;
            var zero = LagRange; // r[zero + lag]
            if (accurate) CrossCorrelation(t, b, r, zero);
            else
            {
                var ac = Autocorrelate(b.Frame, FftLength, b.FftRe, b.FftIm, b.Power);
                r[zero] = 1.0;
                for (var lag = 1; lag <= LagRange; lag++)
                    r[zero + lag] = r[zero - lag] = ac[lag] / (ac[0] * _windowAutocorrelation[lag]);
            }

            var candidates = new List<PitchCandidate>(maxCandidates) { new(0, 0) };
            if (localPeak == 0) return new PitchFrame(intensity, candidates);

            var lagOfCandidate = new List<int> { 0 };
            for (var lag = 2; lag < maximumLag && lag < LagRange; lag++)
            {
                var ri = r[zero + lag];
                if (!(ri > 0.5 * voicingThreshold && ri > r[zero + lag - 1] && ri >= r[zero + lag + 1])) continue;

                var slope = 0.5 * (r[zero + lag + 1] - r[zero + lag - 1]);
                var curvature = (ri - r[zero + lag - 1]) + (ri - r[zero + lag + 1]);
                var frequency = 1.0 / _dx / (lag + slope / curvature);
                // r is 1-based for the interpolator: lag L sits at sample L + LagRange + 1.
                var strength = SincInterpolator.Interpolate(r, 1.0 / _dx / frequency + LagRange + 1, 30);
                if (strength > 1) strength = 1 / strength; // short windows can overshoot; reflect around 1

                int place;
                if (candidates.Count < maxCandidates)
                {
                    candidates.Add(default);
                    lagOfCandidate.Add(0);
                    place = candidates.Count - 1;
                }
                else
                {
                    // Replace the weakest candidate (favouring high frequencies), if this one beats it.
                    var weakest = 2.0;
                    place = -1;
                    for (var k = 1; k < candidates.Count; k++)
                    {
                        var local = candidates[k].Strength - octaveCost * Math.Log2(pitchFloor / candidates[k].Frequency);
                        if (local < weakest)
                        {
                            weakest = local;
                            place = k;
                        }
                    }
                    if (strength - octaveCost * Math.Log2(pitchFloor / frequency) <= weakest) place = -1;
                }
                if (place < 0) continue;
                candidates[place] = new PitchCandidate(frequency, strength);
                lagOfCandidate[place] = lag;
            }

            // Second pass: maximize the sinc interpolant around each peak for extra precision.
            for (var k = 1; k < candidates.Count; k++)
            {
                var depth = candidates[k].Frequency > 0.3 / _dx ? PeakRefinement.SincDepth700 : refineDepth;
                var (value, position) = PeakRefinement.SincMaximum(r, lagOfCandidate[k] + LagRange + 1, depth);
                var lag = position - LagRange - 1;
                candidates[k] = new PitchCandidate(1.0 / _dx / lag, value > 1 ? 1 / value : value);
            }
            return new PitchFrame(intensity, candidates);
        }

        /// <summary>Normalized cross-correlation between the window at t and its lagged copies.</summary>
        private void CrossCorrelation(double t, FrameBuffers b, double[] r, int zero)
        {
            var grid = Sound.Grid;
            var startTime = t - 0.5 * (1.0 / pitchFloor + windowDuration);
            var start = Math.Max(1, (int)Math.Floor(grid.XToIndex(startTime) + 1)); // 1-based
            var span = Math.Min(maximumLag + WindowSamples, grid.Count + 1 - start);
            var localMaximumLag = span - WindowSamples;
            var offset = start - 1;

            var sumX2 = 0.0;
            for (var ch = 0; ch < Sound.ChannelCount; ch++)
            {
                var z = Sound.ChannelArray(ch);
                for (var i = 1; i <= WindowSamples; i++)
                {
                    var x = z[offset + i - 1] - b.LocalMean[ch];
                    sumX2 += x * x;
                }
            }
            // Numerators for every lag at once: the cross-correlation of the window with
            // the whole span (window + lags), computed as IFFT(conj(FFT(window))·FFT(span)).
            // The transform is long enough (≥ window + span) that no lag wraps around.
            // O(N log N) per frame instead of O(window × lags).
            var n = b.CrossRe.Length;
            Array.Clear(b.ProductRe);
            Array.Clear(b.ProductIm);
            for (var ch = 0; ch < Sound.ChannelCount; ch++)
            {
                var z = Sound.ChannelArray(ch);
                var mean = b.LocalMean[ch];
                Array.Clear(b.CrossRe);
                Array.Clear(b.CrossIm);
                Array.Clear(b.SpanRe);
                Array.Clear(b.SpanIm);
                for (var j = 0; j < WindowSamples; j++) b.CrossRe[j] = z[offset + j] - mean;
                for (var j = 0; j < span; j++) b.SpanRe[j] = z[offset + j] - mean;
                Fft.ForwardInPlace(b.CrossRe, b.CrossIm);
                Fft.ForwardInPlace(b.SpanRe, b.SpanIm);
                for (var k = 0; k < n; k++)
                {
                    // conj(X)·S
                    b.ProductRe[k] += b.CrossRe[k] * b.SpanRe[k] + b.CrossIm[k] * b.SpanIm[k];
                    b.ProductIm[k] += b.CrossRe[k] * b.SpanIm[k] - b.CrossIm[k] * b.SpanRe[k];
                }
            }
            Fft.InverseInPlace(b.ProductRe, b.ProductIm); // ProductRe[lag] = Σ_j x[j]·y[j + lag]

            var sumY2 = sumX2;
            r[zero] = 1.0;
            for (var lag = 1; lag <= localMaximumLag; lag++)
            {
                for (var ch = 0; ch < Sound.ChannelCount; ch++)
                {
                    var z = Sound.ChannelArray(ch);
                    var mean = b.LocalMean[ch];
                    var leaving = z[offset + lag - 1] - mean;
                    var entering = z[offset + lag + WindowSamples - 1] - mean;
                    sumY2 += entering * entering - leaving * leaving;
                }
                r[zero + lag] = r[zero - lag] = b.ProductRe[lag] / Math.Sqrt(sumX2 * sumY2);
            }
            // Lags beyond the end of the sound stay at their previous values in the
            // reference; zero them so frames are independent of processing order.
            for (var lag = localMaximumLag + 1; lag <= LagRange; lag++) r[zero + lag] = r[zero - lag] = 0;
        }
    }
}
