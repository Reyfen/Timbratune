using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Spectral;
using Reyfen.Timbratune.Acoustics.Voice;
using Reyfen.Timbratune.Core.Diagnostics;

namespace Reyfen.Timbratune.Core.Analysis.Tracks;

/// <summary>
/// The tracks of a whole sound (the saved analysis), with the same kernels and settings as
/// analyze.py's Praat calls. Stage 1 runs its tracks in parallel on all cores, then stage 2
/// (pulses beside voiced frames, sounding and the gate), then the derived tracks.
/// </summary>
public static class SavedTrackProvider
{
    public static TrackView Measure(Sound sound, TrackSet set = TrackSet.All,
        double loudOutlierFraction = RawAnalysisAssembler.LoudOutlierFraction, Action<double>? progress = null)
    {
        set = set.WithDependencies();
        PitchContour pitch = null!;
        IntensityContour intensity = null!;
        HarmonicityContour harmonicity = null!;
        IFormantTrack formant5500 = null!, formant5000 = null!;
        IReadOnlyList<(double X, double Y)> ltas = [];
        // Weights: how long each stage takes (ms, 30 s take on a Pixel 4a, all running together);
        // pulses run after the rest, so theirs is scaled to their share of the wall time.
        var stages = new StageProgress(progress, 90, 60, 240, 420, 120, 67);
        var stage1 = new List<Action>(5);
        if (set.Has(TrackSet.Pitch))
            stage1.Add(() => { using (Timing.Measure("analysis.pitch")) pitch = PitchAnalyzer.Autocorrelation(sound, 0, AcousticsAnalysisEngine.PitchFloor, AcousticsAnalysisEngine.PitchCeiling, progress: stages.Stage(0)); });
        if (set.Has(TrackSet.Intensity))
            stage1.Add(() => { using (Timing.Measure("analysis.intensity")) intensity = IntensityAnalyzer.Analyze(sound, AcousticsAnalysisEngine.PitchFloor); stages.Complete(1); });
        if (set.Has(TrackSet.Harmonicity))
            stage1.Add(() => { using (Timing.Measure("analysis.hnr")) harmonicity = HarmonicityAnalyzer.CrossCorrelation(sound, AcousticsAnalysisEngine.HnrStep, AcousticsAnalysisEngine.PitchFloor, 0.1, 1.0, stages.Stage(2)); });
        if (set.Has(TrackSet.Formants))
            stage1.Add(() =>
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
                        contours = FormantAnalyzer.BurgResampled(resampled, ceilings, 0, AcousticsAnalysisEngine.FormantCount,
                            AcousticsAnalysisEngine.FormantWindow, 50, formantStages.Stage(1));
                    (formant5500, formant5000) = (contours[0], contours[1]);
                }
            });
        if (set.Has(TrackSet.Ltas))
            stage1.Add(() => { using (Timing.Measure("analysis.ltas")) ltas = RawAnalysisAssembler.LtasPoints(Ltas.FromSound(sound, 100)); stages.Complete(4); });
        Parallel.Invoke([.. stage1]);

        var derived = new DerivedTracks();
        double[] pulses = [];
        if (set.Has(TrackSet.Pulses))
            Parallel.Invoke(
                () => { using (Timing.Measure("analysis.pulses")) pulses = PulseDetector.PeriodicCrossCorrelation(sound, pitch); },
                () => derived.ComputeStage2(pitch, intensity, set, loudOutlierFraction));
        else derived.ComputeStage2(pitch, intensity, set, loudOutlierFraction);
        stages.Complete(5);

        var formants = formant5500;
        Func<double, double, RawAnalysis.WeightRow?> weightRow = (t, f0) => RawAnalysisAssembler.MeasureWeightRow(sound, formants, t, f0);
        using (Timing.Measure("analysis.derived")) derived.ComputeStage3(formant5500, formant5000, weightRow, set);

        return new TrackView
        {
            Tracks = set,
            Duration = sound.Duration,
            SamplingFrequency = sound.SamplingFrequency,
            Sound = sound,
            Pitch = pitch,
            Intensity = intensity,
            Harmonicity = harmonicity,
            Formants5500 = formant5500,
            Formants5000 = formant5000,
            Ltas = ltas,
            Pulses = pulses,
            StretchJitter = null!, // derived from the pulses when the series need it
            WeightRow = weightRow,
            Derived = derived,
        };
    }
}
