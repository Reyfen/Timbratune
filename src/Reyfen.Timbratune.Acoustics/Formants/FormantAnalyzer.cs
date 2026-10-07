using System.Numerics;
using Reyfen.Timbratune.Acoustics.Numerics;

namespace Reyfen.Timbratune.Acoustics.Formants;

/// <summary>A resonance: centre frequency and −3 dB bandwidth, both in Hz.</summary>
public readonly record struct FormantValue(double Frequency, double Bandwidth);

/// <summary>Formant values over time, however the frames are laid out.</summary>
public interface IFormantTrack
{
    /// <summary>Frequency of formant <paramref name="number"/> (1 = F1) at time t; NaN where unknown.</summary>
    double ValueAtTime(int number, double t);

    /// <summary>Bandwidth of formant <paramref name="number"/> at time t; NaN where unknown.</summary>
    double BandwidthAtTime(int number, double t);
}

/// <summary>Formant tracks: per frame the resonances found, lowest first.</summary>
public sealed class FormantContour : IFormantTrack
{
    internal FormantContour(TimeGrid grid, FormantValue[][] frames)
    {
        Grid = grid;
        Frames = frames;
    }

    public TimeGrid Grid { get; }
    public IReadOnlyList<IReadOnlyList<FormantValue>> Frames { get; }

    /// <summary>Frequency of formant <paramref name="number"/> (1 = F1) at time t, interpolated between frames.</summary>
    public double ValueAtTime(int number, double t) => AtTime(number, t, f => f.Frequency);

    /// <summary>Bandwidth of formant <paramref name="number"/> at time t, interpolated between frames.</summary>
    public double BandwidthAtTime(int number, double t) => AtTime(number, t, f => f.Bandwidth);

    /// <summary>
    /// Linear interpolation between the nearest two frames. NaN if the nearer frame
    /// lacks this formant; the nearer value alone if only the farther one does.
    /// </summary>
    private double AtTime(int number, double t, Func<FormantValue, double> select)
    {
        if (t < Grid.XMin || t > Grid.XMax) return double.NaN;
        var index = Grid.XToIndex(t) + 1; // 1-based fractional frame number
        var leftIndex = (int)Math.Floor(index);
        var phase = index - leftIndex;
        int near, far;
        if (phase < 0.5)
        {
            near = leftIndex;
            far = leftIndex + 1;
        }
        else
        {
            far = leftIndex;
            near = leftIndex + 1;
            phase = 1 - phase;
        }
        var nearValue = Get(near);
        if (double.IsNaN(nearValue)) return double.NaN;
        var farValue = Get(far);
        return double.IsNaN(farValue) ? nearValue : nearValue + phase * (farValue - nearValue);

        double Get(int frameNumber)
        {
            if (frameNumber < 1 || frameNumber > Frames.Count) return double.NaN;
            var formants = Frames[frameNumber - 1];
            return number >= 1 && number <= formants.Count ? select(formants[number - 1]) : double.NaN;
        }
    }
}

/// <summary>
/// Formant estimation by linear prediction (Markel &amp; Gray 1976): resample
/// to twice the formant ceiling, pre-emphasize, cut Gaussian-windowed frames,
/// fit an all-pole model with Burg's method and read the resonances off the
/// roots of the prediction polynomial (Praat manual, Sound: To Formant (burg)…).
/// </summary>
public static class FormantAnalyzer
{
    private const double SafetyMarginHz = 50;

    /// <summary>
    /// <see cref="Burg"/> at several formant ceilings at once (e.g. 5500 and 5000 Hz): the
    /// resampling they all start with shares its forward transform (<see cref="Resampler.ResampleAll"/>),
    /// then each ceiling's frames run in parallel. Each contour is identical to <see cref="Burg"/>'s.
    /// </summary>
    public static FormantContour[] BurgAll(Sound sound, IReadOnlyList<double> formantCeilings, double timeStep, double maxFormants,
        double windowLength, double preEmphasisFrom, Action<double>? progress = null)
    {
        const double resampleShare = 0.85;
        var resampled = ResampleForCeilings(sound, formantCeilings, progress is null ? null : f => progress(resampleShare * f));
        return BurgResampled(resampled, formantCeilings, timeStep, maxFormants, windowLength, preEmphasisFrom,
            progress is null ? null : f => progress(resampleShare + (1 - resampleShare) * f));
    }

    /// <summary>The channel average at 2 × each ceiling (the first half of <see cref="BurgAll"/>; shared forward transform).</summary>
    public static Sound[] ResampleForCeilings(Sound sound, IReadOnlyList<double> formantCeilings, Action<double>? progress = null)
    {
        // As in Burg: the channel average first, then each ceiling's 2 × ceiling sampling rate.
        var mono = new Sound([sound.ToMono()], sound.Grid);
        var nyquist = 0.5 * sound.SamplingFrequency;
        var needed = formantCeilings.Select((c, i) => (c, i)).Where(x => Math.Abs(x.c / nyquist - 1) >= 1e-12).ToList();
        var resampled = Resampler.ResampleAll(mono, needed.Select(x => 2 * x.c).ToList(), 50, progress);
        var result = new Sound[formantCeilings.Count];
        for (var i = 0; i < result.Length; i++) result[i] = mono;
        for (var k = 0; k < needed.Count; k++) result[needed[k].i] = resampled[k];
        return result;
    }

    /// <summary>The second half of <see cref="BurgAll"/>: each ceiling's frames (in parallel) on its resampled sound.</summary>
    public static FormantContour[] BurgResampled(Sound[] resampled, IReadOnlyList<double> formantCeilings, double timeStep,
        double maxFormants, double windowLength, double preEmphasisFrom, Action<double>? progress = null)
    {
        const double resampleShare = 0.85;
        var results = new FormantContour[formantCeilings.Count];
        var stages = new double[formantCeilings.Count];
        Parallel.For(0, formantCeilings.Count, i =>
        {
            // Already at 2 × ceiling, so Burg goes straight to the frames.
            results[i] = Burg(resampled[i], timeStep, maxFormants, formantCeilings[i], windowLength, preEmphasisFrom,
                progress is null ? null : f =>
                {
                    lock (stages)
                    {
                        // Burg counts its (here skipped) resampling as the first 85%: only its frames are left.
                        stages[i] = Math.Clamp((f - resampleShare) / (1 - resampleShare), 0, 1);
                        progress(stages.Average());
                    }
                });
        });
        return results;
    }

    /// <param name="timeStep">0 = a quarter of the window length.</param>
    /// <param name="maxFormants">Formants sought per frame (poles = 2 × this), e.g. 5.</param>
    /// <param name="formantCeiling">Highest formant frequency (Hz), e.g. 5500 for most female voices.</param>
    /// <param name="windowLength">Effective window length in seconds; the Gaussian window is twice this long.</param>
    /// <param name="preEmphasisFrom">Frequency (Hz) above which a +6 dB/octave pre-emphasis applies.</param>
    /// <param name="progress">Called with the fraction of frames analyzed so far (0–1), from any thread.</param>
    public static FormantContour Burg(Sound sound, double timeStep, double maxFormants, double formantCeiling,
        double windowLength, double preEmphasisFrom, Action<double>? progress = null)
    {
        var halfWindow = windowLength;
        var dt = timeStep > 0 ? timeStep : halfWindow / 4.0;
        var poles = Stats.RoundHalfUp(2.0 * maxFormants);

        // Everything below is linear, so averaging the channels first is equivalent to analysing their mean.
        var mono = new Sound([sound.ToMono()], sound.Grid);
        var nyquist = 0.5 * sound.SamplingFrequency;
        // Resampling is most of the work (~85% on a 27.6 s take at 44.1 kHz), the frames the rest.
        const double resampleShare = 0.85;
        var resampled = Math.Abs(formantCeiling / nyquist - 1) < 1e-12 ? mono
            : Resampler.Resample(mono, 2 * formantCeiling, 50, progress is null ? null : f => progress(resampleShare * f));
        var frameProgress = progress is null ? null : (Action<double>)(f => progress(resampleShare + (1 - resampleShare) * f));
        var grid = resampled.Grid;
        var dx = grid.Step;
        var samples = resampled.Channel(0).ToArray();
        PreEmphasize(samples, dx, preEmphasisFrom);

        var physicalDuration = grid.Count * dx;
        var windowDuration = 2 * halfWindow;
        var frameCount = 1 + (int)Math.Floor((physicalDuration - windowDuration) / dt);
        var windowSamples = (int)Math.Floor(windowDuration / dx);
        var first = grid.First + 0.5 * (physicalDuration - dx - (frameCount - 1) * dt);
        if (frameCount < 1)
        {
            frameCount = 1;
            first = grid.First + 0.5 * physicalDuration;
            windowSamples = grid.Count;
        }
        if (windowSamples < poles + 1) throw new ArgumentException("The analysis window is too short for this many formants.");
        var halfWindowSamples = windowSamples / 2;
        var window = GaussianWindow(windowSamples);
        var newNyquist = 0.5 / dx;

        var frames = new FormantValue[frameCount][];
        var counter = new FrameProgress(frameCount, frameProgress);
        Parallel.For(0, frameCount, f =>
        {
            var t = first + f * dt;
            var leftSample = (int)Math.Floor((t - grid.First) / dx + 1); // 1-based
            var start = Math.Max(1, leftSample + 1 - halfWindowSamples);
            var end = Math.Min(grid.Count, leftSample + halfWindowSamples);
            var length = end - start + 1;
            var frame = new double[length];
            var peak = 0.0;
            for (var j = 0; j < length; j++)
            {
                var s = samples[start + j - 1];
                peak = Math.Max(peak, s * s);
                frame[j] = s * window[j];
            }
            frames[f] = peak == 0 ? [] : FrameFormants(frame, poles, newNyquist);
            counter.Done();
        });
        progress?.Invoke(1);
        return new FormantContour(new TimeGrid(sound.Grid.XMin, sound.Grid.XMax, frameCount, dt, first), frames);
    }

    /// <summary>First-order pre-emphasis y[i] = x[i] − e^(−2π·F·Δt)·x[i−1], in place.</summary>
    internal static void PreEmphasize(double[] samples, double dx, double fromFrequency)
    {
        var factor = Math.Exp(-2 * Math.PI * fromFrequency * dx);
        for (var i = samples.Length - 1; i >= 1; i--) samples[i] -= factor * samples[i - 1];
    }

    /// <summary>Gaussian window exp(−48(i − mid)²/(N + 1)²), shifted and scaled to be 0 at the virtual edges.</summary>
    internal static double[] GaussianWindow(int n)
    {
        var mid = 0.5 * (n + 1);
        var edge = Math.Exp(-12.0);
        var w = new double[n];
        for (var i = 1; i <= n; i++)
            w[i - 1] = (Math.Exp(-48.0 * (i - mid) * (i - mid) / (n + 1) / (n + 1)) - edge) / (1.0 - edge);
        return w;
    }

    /// <summary>Resonances of one frame: roots of z^p − a₁z^(p−1) − … − a_p inside the unit circle.</summary>
    internal static FormantValue[] FrameFormants(double[] frame, int poles, double nyquist)
    {
        var a = BurgLpc.Coefficients(frame, poles);
        var coefficients = new double[poles + 1]; // ascending powers
        coefficients[poles] = 1.0;
        for (var j = 1; j <= poles; j++) coefficients[poles - j] = -a[j];

        var result = new List<FormantValue>(poles / 2 + 1);
        foreach (var root in Polynomial.Roots(coefficients))
        {
            // Reflect unstable roots into the unit circle (same frequency, positive bandwidth).
            var z = root.Magnitude > 1 ? 1.0 / Complex.Conjugate(root) : root;
            if (z.Imaginary < 0) continue; // one of each conjugate pair
            var frequency = Math.Abs(Math.Atan2(z.Imaginary, z.Real)) * nyquist / Math.PI;
            if (frequency < SafetyMarginHz || frequency > nyquist - SafetyMarginHz) continue;
            var bandwidth = -Math.Log(z.Real * z.Real + z.Imaginary * z.Imaginary) * nyquist / Math.PI;
            result.Add(new FormantValue(frequency, bandwidth));
        }
        result.Sort((x, y) => x.Frequency.CompareTo(y.Frequency));
        return [.. result];
    }
}
