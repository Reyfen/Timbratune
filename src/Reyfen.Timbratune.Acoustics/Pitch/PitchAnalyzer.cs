using Reyfen.Timbratune.Acoustics.Numerics;

namespace Reyfen.Timbratune.Acoustics.Pitch;

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
        var duration = grid.Step * grid.Count;
        if (pitchFloor < periodsPerWindow / duration)
            throw new ArgumentException($"The pitch floor must be at least {periodsPerWindow / duration} Hz for this sound.");

        var analyzer = new PitchFrameAnalyzer(sound.SamplingFrequency, sound.ChannelCount, method, periodsPerWindow, timeStep,
            pitchFloor, pitchCeiling, settings);
        var frames = TimeGrid.ShortTermFrames(grid, analyzer.FrameWindowDuration, analyzer.TimeStep);
        var channels = Enumerable.Range(0, sound.ChannelCount).Select(sound.ChannelArray).ToArray();
        var globalPeak = PitchFrameAnalyzer.GlobalPeak(channels, grid.Count);

        var result = new PitchFrame[frames.Count];
        if (globalPeak == 0)
        {
            for (var i = 0; i < result.Length; i++) result[i] = PitchFrame.Silent;
        }
        else
        {
            Parallel.For(0, frames.Count, analyzer.CreateBuffers,
                (i, _, buffers) =>
                {
                    result[i] = analyzer.AnalyzeFrame(channels, grid, frames.IndexToX(i), buffers);
                    return buffers;
                },
                _ => { });
        }
        return analyzer.ChoosePath(frames, result, globalPeak);
    }
}

/// <summary>
/// Everything needed to analyze single frames, independent of any whole-sound
/// state: the full analysis and the live (streaming) analysis share it, so a
/// frame computed from the same samples is identical in both.
/// </summary>
internal sealed class PitchFrameAnalyzer
{
    private readonly PitchAnalyzer.Method _method;
    private readonly double _dx;
    private readonly double _pitchFloor;
    private readonly int _maxCandidates;
    private readonly PitchSettings _settings;
    private readonly int _periodSamples, _halfPeriodSamples, _halfWindowSamples, _maximumLag, _refineDepth;
    private readonly double _windowDuration;
    private readonly int _channelCount;
    private readonly int _fftLength;
    private readonly double[] _window = [];
    private readonly double[] _windowAutocorrelation = [];

    public PitchFrameAnalyzer(double samplingFrequency, int channelCount, PitchAnalyzer.Method method, double periodsPerWindow,
        double timeStep, double pitchFloor, double pitchCeiling, PitchSettings settings)
    {
        _method = method;
        _dx = 1.0 / samplingFrequency;
        _pitchFloor = pitchFloor;
        _settings = settings;
        _channelCount = channelCount;
        _maxCandidates = settings.MaxCandidates;
        if (_maxCandidates < pitchCeiling / pitchFloor) _maxCandidates = (int)Math.Floor(pitchCeiling / pitchFloor);
        TimeStep = timeStep > 0 ? timeStep : periodsPerWindow / pitchFloor / 4.0;

        var accurate = method == PitchAnalyzer.Method.CrossCorrelationAccurate;
        var interpolationDepth = accurate ? 1.0 : 0.5;
        _refineDepth = accurate ? PeakRefinement.SincDepth700 : PeakRefinement.SincDepth70;

        // Longest period in samples: the local mean looks one period each way, the local peak half a period.
        _periodSamples = (int)Math.Floor(1.0 / _dx / pitchFloor);
        _halfPeriodSamples = _periodSamples / 2 + 1;
        Ceiling = Math.Min(pitchCeiling, 0.5 / _dx);

        _windowDuration = periodsPerWindow / pitchFloor;
        _halfWindowSamples = (int)Math.Floor(_windowDuration / _dx) / 2 - 1;
        if (_halfWindowSamples < 2) throw new ArgumentException("The analysis window is too short.");
        WindowSamples = 2 * _halfWindowSamples;
        _maximumLag = Math.Min((int)Math.Floor(WindowSamples / periodsPerWindow) + 2, WindowSamples);
        LagRange = (int)Math.Floor(WindowSamples * interpolationDepth); // largest lag kept in r
        FrameWindowDuration = accurate ? 1.0 / pitchFloor + _windowDuration : _windowDuration;
        CrossFftLength = accurate ? Fft.NextPowerOfTwo(2 * WindowSamples + _maximumLag) : 1;

        if (!accurate)
        {
            _fftLength = Fft.NextPowerOfTwo((int)Math.Ceiling(WindowSamples * (1 + interpolationDepth)));
            while (_fftLength < WindowSamples * (1 + interpolationDepth)) _fftLength *= 2;
            _window = new double[WindowSamples];
            for (var i = 0; i < WindowSamples; i++)
                _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (i + 1) / (WindowSamples + 1)); // Hanning, zero just outside
            _windowAutocorrelation = Autocorrelate([_window], _fftLength, new double[_fftLength], new double[_fftLength], new double[_fftLength]);
            var zeroLag = _windowAutocorrelation[0];
            for (var i = 0; i < _windowAutocorrelation.Length; i++) _windowAutocorrelation[i] /= zeroLag;
        }
    }

    public double TimeStep { get; }
    public double Ceiling { get; }
    /// <summary>Span of signal one frame needs; frames are laid out so that it fits (<see cref="TimeGrid.ShortTermFrames"/>).</summary>
    public double FrameWindowDuration { get; }
    internal int WindowSamples { get; }
    internal int LagRange { get; }
    internal int CrossFftLength { get; }
    internal int FftLength => _fftLength;

    /// <summary>Largest |sample − channel mean| over all channels: the reference level for the silence score.</summary>
    public static double GlobalPeak(double[][] channels, int count)
    {
        var peak = 0.0;
        foreach (var channel in channels)
        {
            var samples = channel.AsSpan(0, count);
            var mean = Stats.Mean(samples);
            foreach (var s in samples) peak = Math.Max(peak, Math.Abs(s - mean));
        }
        return peak;
    }

    /// <summary>Picks the path through the frames' candidates and wraps them as a contour.</summary>
    public PitchContour ChoosePath(TimeGrid frameGrid, PitchFrame[] frames, double globalPeak)
    {
        var chosen = globalPeak == 0
            ? new int[frames.Length]
            : PitchPath.ChooseIndices(frames, frameGrid.Step, globalPeak, _settings.SilenceThreshold, _settings.VoicingThreshold,
                _settings.OctaveCost, _settings.OctaveJumpCost, _settings.VoicedUnvoicedCost, Ceiling);
        return new PitchContour(frameGrid, frames, chosen, Ceiling);
    }

    /// <summary>Highest 0-based sample index a frame at time t reads when nothing is clipped.</summary>
    public int LastSampleNeeded(TimeGrid samples, double t)
    {
        var leftSample = (int)Math.Floor(samples.XToIndex(t) + 1); // 1-based
        var last = leftSample + Math.Max(_periodSamples, _halfWindowSamples) - 1;
        if (_method == PitchAnalyzer.Method.CrossCorrelationAccurate)
        {
            var startTime = t - 0.5 * (1.0 / _pitchFloor + _windowDuration);
            var start = Math.Max(1, (int)Math.Floor(samples.XToIndex(startTime) + 1));
            last = Math.Max(last, start - 1 + _maximumLag + WindowSamples - 1);
        }
        return last;
    }

    /// <summary>Per-thread scratch space.</summary>
    public FrameBuffers CreateBuffers() => new(this, _channelCount);

    internal sealed class FrameBuffers(PitchFrameAnalyzer a, int channels)
    {
        public readonly double[][] Frame = Enumerable.Range(0, channels).Select(_ => new double[a.WindowSamples]).ToArray();
        public readonly double[] FftRe = new double[Math.Max(a.FftLength, 1)];
        public readonly double[] FftIm = new double[Math.Max(a.FftLength, 1)];
        public readonly double[] Power = new double[Math.Max(a.FftLength, 1)];
        /// <summary>Correlation for lags −lagRange..lagRange; index = lag + lagRange.</summary>
        public readonly double[] R = new double[2 * a.LagRange + 1];
        public readonly double[] LocalMean = new double[channels];
        // Cross-correlation mode only (length 1 otherwise).
        public readonly double[] CrossRe = new double[a.CrossFftLength];
        public readonly double[] CrossIm = new double[a.CrossFftLength];
        public readonly double[] SpanRe = new double[a.CrossFftLength];
        public readonly double[] SpanIm = new double[a.CrossFftLength];
        public readonly double[] ProductRe = new double[a.CrossFftLength];
        public readonly double[] ProductIm = new double[a.CrossFftLength];
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

    /// <summary>
    /// Candidates for the frame centred at t. <paramref name="samples"/> describes the
    /// valid part of <paramref name="channels"/> (arrays may be longer than its Count).
    /// </summary>
    public PitchFrame AnalyzeFrame(double[][] channels, TimeGrid samples, double t, FrameBuffers b)
    {
        // 1-based sample numbers, as in the frame-placement description.
        var leftSample = (int)Math.Floor(samples.XToIndex(t) + 1);
        var rightSample = leftSample + 1;
        var accurate = _method == PitchAnalyzer.Method.CrossCorrelationAccurate;

        for (var ch = 0; ch < channels.Length; ch++)
        {
            var z = channels[ch];
            var sum = 0.0;
            for (var i = rightSample - _periodSamples; i <= leftSample + _periodSamples; i++) sum += z[i - 1];
            b.LocalMean[ch] = sum / (2 * _periodSamples);

            var start = rightSample - _halfWindowSamples;
            var frame = b.Frame[ch];
            for (var j = 0; j < WindowSamples; j++)
            {
                var v = z[start + j - 1] - b.LocalMean[ch];
                frame[j] = accurate ? v : v * _window[j];
            }
        }

        // Local peak over half a longest period around the frame centre.
        var peakFrom = Math.Max(1, _halfWindowSamples + 1 - _halfPeriodSamples);
        var peakTo = Math.Min(WindowSamples, _halfWindowSamples + _halfPeriodSamples);
        var localPeak = 0.0;
        foreach (var frame in b.Frame)
            for (var j = peakFrom; j <= peakTo; j++)
                localPeak = Math.Max(localPeak, Math.Abs(frame[j - 1]));

        var r = b.R;
        var zero = LagRange; // r[zero + lag]
        if (accurate) CrossCorrelation(channels, samples, t, b, r, zero);
        else
        {
            var ac = Autocorrelate(b.Frame, _fftLength, b.FftRe, b.FftIm, b.Power);
            r[zero] = 1.0;
            for (var lag = 1; lag <= LagRange; lag++)
                r[zero + lag] = r[zero - lag] = ac[lag] / (ac[0] * _windowAutocorrelation[lag]);
        }

        var candidates = new List<PitchCandidate>(_maxCandidates) { new(0, 0) };
        if (localPeak == 0) return new PitchFrame(0, candidates);

        var voicingThreshold = _settings.VoicingThreshold;
        var octaveCost = _settings.OctaveCost;
        var lagOfCandidate = new List<int> { 0 };
        for (var lag = 2; lag < _maximumLag && lag < LagRange; lag++)
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
            if (candidates.Count < _maxCandidates)
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
                    var local = candidates[k].Strength - octaveCost * Math.Log2(_pitchFloor / candidates[k].Frequency);
                    if (local < weakest)
                    {
                        weakest = local;
                        place = k;
                    }
                }
                if (strength - octaveCost * Math.Log2(_pitchFloor / frequency) <= weakest) place = -1;
            }
            if (place < 0) continue;
            candidates[place] = new PitchCandidate(frequency, strength);
            lagOfCandidate[place] = lag;
        }

        // Second pass: maximize the sinc interpolant around each peak for extra precision.
        for (var k = 1; k < candidates.Count; k++)
        {
            var depth = candidates[k].Frequency > 0.3 / _dx ? PeakRefinement.SincDepth700 : _refineDepth;
            var (value, position) = PeakRefinement.SincMaximum(r, lagOfCandidate[k] + LagRange + 1, depth);
            var lag = position - LagRange - 1;
            candidates[k] = new PitchCandidate(1.0 / _dx / lag, value > 1 ? 1 / value : value);
        }
        return new PitchFrame(localPeak, candidates);
    }

    /// <summary>Normalized cross-correlation between the window at t and its lagged copies.</summary>
    private void CrossCorrelation(double[][] channels, TimeGrid samples, double t, FrameBuffers b, double[] r, int zero)
    {
        var startTime = t - 0.5 * (1.0 / _pitchFloor + _windowDuration);
        var start = Math.Max(1, (int)Math.Floor(samples.XToIndex(startTime) + 1)); // 1-based
        var span = Math.Min(_maximumLag + WindowSamples, samples.Count + 1 - start);
        var localMaximumLag = span - WindowSamples;
        var offset = start - 1;

        var sumX2 = 0.0;
        for (var ch = 0; ch < channels.Length; ch++)
        {
            var z = channels[ch];
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
        for (var ch = 0; ch < channels.Length; ch++)
        {
            var z = channels[ch];
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
            for (var ch = 0; ch < channels.Length; ch++)
            {
                var z = channels[ch];
                var mean = b.LocalMean[ch];
                var leaving = z[offset + lag - 1] - mean;
                var entering = z[offset + lag + WindowSamples - 1] - mean;
                sumY2 += entering * entering - leaving * leaving;
            }
            // The running energy can reach 0 (or a rounding-level negative) over digital
            // silence; there is no correlation to measure there.
            var energy = sumX2 * sumY2;
            r[zero + lag] = r[zero - lag] = energy > 0 ? b.ProductRe[lag] / Math.Sqrt(energy) : 0;
        }
        // Lags beyond the end of the sound stay at their previous values in the
        // reference; zero them so frames are independent of processing order.
        for (var lag = localMaximumLag + 1; lag <= LagRange; lag++) r[zero + lag] = r[zero - lag] = 0;
    }
}
