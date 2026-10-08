using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Spectral;
using Reyfen.Timbratune.Acoustics.Voice;
using Reyfen.Timbratune.Core.Diagnostics;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// The analysis engine: measures a take with Reyfen.Timbratune.Acoustics (pure C#, no
/// external programs) following the measurement steps of analyze.py, then
/// hands the raw values to <see cref="AnalysisPostProcessor"/>. Every setting
/// below matches the corresponding analyze.py / parselmouth call.
/// </summary>
public sealed class AcousticsAnalysisEngine : IAnalysisEngine
{
    public const double PitchFloor = 75, PitchCeiling = 500;

    /// <summary>Frame spacings (s) that follow from the settings: pitch 0.75 / floor, intensity 0.8 / floor.</summary>
    public const double PitchStep = 0.75 / PitchFloor, IntensityStep = 0.8 / PitchFloor, HnrStep = 0.01;

    /// <summary>Burg formant analysis: formants per frame and window length (s); frames every window / 4.</summary>
    public const int FormantCount = 5;
    public const double FormantWindow = 0.025;

    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default, IProgress<double>? progress = null) =>
        Task.Run(() =>
        {
            Sound sound;
            using (var stream = File.OpenRead(wavPath)) sound = WavDecoder.Decode(stream);
            cancellationToken.ThrowIfCancellationRequested();
            // Stage weights: share of the analysis time measured on a 27.6 s take (Release).
            var stages = new StageProgress(progress is null ? null : progress.Report, 93.5, 2, 1, 2.5);
            FrameTracks tracks;
            using (Timing.Measure("analysis.measure")) tracks = MeasureTracks(sound, stages.Stage(0));
            RawAnalysis raw;
            using (Timing.Measure("analysis.assemble")) raw = RawAnalysisAssembler.Assemble(tracks);
            stages.Complete(1);
            AnalysisResult result;
            using (Timing.Measure("analysis.postprocess")) result = AnalysisPostProcessor.Process(raw, registerFloorHz);
            stages.Complete(2);
            TakeSeries series;
            using (Timing.Measure("analysis.series")) series = FrameSeriesBuilder.ForTake(tracks);
            stages.Complete(3);
            return result with { Series = series };
        }, cancellationToken);

    /// <summary>All raw measurements of one sound (independent analyses run in parallel).</summary>
    public static RawAnalysis Measure(Sound sound, Action<double>? progress = null)
    {
        var stages = new StageProgress(progress, 0.97, 0.03);
        var tracks = MeasureTracks(sound, stages.Stage(0));
        RawAnalysis raw;
        using (Timing.Measure("analysis.assemble")) raw = RawAnalysisAssembler.Assemble(tracks);
        stages.Complete(1);
        return raw;
    }

    /// <summary>The frame-level tracks of the whole sound.</summary>
    /// <param name="progress">Called with the fraction done (0–1), from any thread.</param>
    public static FrameTracks MeasureTracks(Sound sound, Action<double>? progress = null)
    {
        PitchContour pitch = null!;
        IntensityContour intensity = null!;
        HarmonicityContour harmonicity = null!;
        FormantContour formant5500 = null!, formant5000 = null!;
        Ltas ltas = null!;
        // Weights: how long each stage takes (ms, 30 s take on a Pixel 4a, all running together);
        // pulses run after the rest, so theirs is scaled to their share of the wall time.
        var stages = new StageProgress(progress, 90, 60, 240, 420, 120, 67);
        Parallel.Invoke(
            () => { using (Timing.Measure("analysis.pitch")) pitch = PitchAnalyzer.Autocorrelation(sound, 0, PitchFloor, PitchCeiling, progress: stages.Stage(0)); },
            () => { using (Timing.Measure("analysis.intensity")) intensity = IntensityAnalyzer.Analyze(sound, PitchFloor); stages.Complete(1); },
            () => { using (Timing.Measure("analysis.hnr")) harmonicity = HarmonicityAnalyzer.CrossCorrelation(sound, HnrStep, PitchFloor, 0.1, 1.0, stages.Stage(2)); },
            () =>
            {
                // Both ceilings share the resampling's forward transform.
                using (Timing.Measure("analysis.formants"))
                {
                    double[] ceilings = [RawAnalysisAssembler.FormantCeiling, 5000];
                    var formantStages = new StageProgress(stages.Stage(3), 85, 15);
                    Sound[] resampled;
                    using (Timing.Measure("analysis.formants.resample"))
                        resampled = FormantAnalyzer.ResampleForCeilings(sound, ceilings, formantStages.Stage(0));
                    FormantContour[] contours;
                    using (Timing.Measure("analysis.formants.frames"))
                        contours = FormantAnalyzer.BurgResampled(resampled, ceilings, 0, FormantCount, FormantWindow, 50, formantStages.Stage(1));
                    (formant5500, formant5000) = (contours[0], contours[1]);
                }
            },
            () => { using (Timing.Measure("analysis.ltas")) ltas = Ltas.FromSound(sound, 100); stages.Complete(4); });

        double[] pulses;
        using (Timing.Measure("analysis.pulses")) pulses = PulseDetector.PeriodicCrossCorrelation(sound, pitch);
        stages.Complete(5);
        return new FrameTracks
        {
            Duration = sound.Duration,
            SamplingFrequency = sound.SamplingFrequency,
            Pitch = pitch,
            Intensity = intensity,
            HnrMean = harmonicity.Mean(),
            Jitter = VoiceReport.JitterLocal(pulses, 0.0001, 0.02, 1.3),
            Shimmer = VoiceReport.ShimmerLocal(pulses, sound, 0.0001, 0.02, 1.3, 1.6),
            Formants5500 = formant5500,
            Formants5000 = formant5000,
            WeightRow = (t, f0) => RawAnalysisAssembler.MeasureWeightRow(sound, formant5500, t, f0),
            Ltas = RawAnalysisAssembler.LtasPoints(ltas),
            Sounding = RawAnalysisAssembler.SoundingIntervals(intensity),
            Harmonicity = harmonicity,
            Pulses = pulses,
        };
    }
}
