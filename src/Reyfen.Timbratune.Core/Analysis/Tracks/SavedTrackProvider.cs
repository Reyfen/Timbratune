using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Spectral;
using Reyfen.Timbratune.Acoustics.Voice;
using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Reyfen.Timbratune.Core.Diagnostics;

namespace Reyfen.Timbratune.Core.Analysis.Tracks;

/// <summary>
/// The tracks of a whole sound (the saved analysis), with the same kernels and settings as
/// analyze.py's Praat calls. Stage 1 runs its tracks in parallel on all cores, then stage 2
/// (pulses beside voiced frames, sounding, the gate and the formant ceiling path), then the
/// derived tracks.
/// </summary>
public static class SavedTrackProvider
{
    /// <param name="ceilingScoring">How the ceiling path scores the candidates; null = <see cref="CeilingScoring.Default"/>.</param>
    public static TrackView Measure(Sound sound, TrackSet set = TrackSet.All,
        double loudOutlierFraction = RawAnalysisAssembler.LoudOutlierFraction, Action<double>? progress = null,
        FormantCeilingMethod formantMethod = FormantCeilingMethod.Path, CeilingScoring? ceilingScoring = null)
    {
        set = set.WithDependencies();
        PitchContour pitch = null!;
        IntensityContour intensity = null!;
        HarmonicityContour harmonicity = null!;
        var ceilings = FormantCeilings.Candidates(formantMethod);
        FormantContour[] candidates = [];
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
                // All candidate ceilings share the resampling's forward transform.
                using (Timing.Measure("analysis.formants"))
                {
                    var formantStages = new StageProgress(stages.Stage(3), 85, 15);
                    Sound[] resampled;
                    using (Timing.Measure("analysis.formants.resample"))
                        resampled = FormantAnalyzer.ResampleForCeilings(sound, ceilings, formantStages.Stage(0),
                            halfRateLowPass: FormantCeilings.HalfRateLowPass(formantMethod), gridOrigin: FormantCeilings.GridOrigin(formantMethod));
                    FormantContour[] contours;
                    using (Timing.Measure("analysis.formants.frames"))
                        contours = FormantAnalyzer.BurgResampled(resampled, ceilings, 0, AcousticsAnalysisEngine.FormantCount,
                            AcousticsAnalysisEngine.FormantWindow, 50, formantStages.Stage(1), FormantCeilings.GridOrigin(formantMethod));
                    candidates = contours;
                }
            });
        if (set.Has(TrackSet.Ltas))
            stage1.Add(() => { using (Timing.Measure("analysis.ltas")) ltas = RawAnalysisAssembler.LtasPoints(Ltas.FromSound(sound, 100)); stages.Complete(4); });
        Parallel.Invoke([.. stage1]);

        var derived = new DerivedTracks();
        double[] pulses = [];
        IFormantTrack formants = candidates.Length > 0 ? candidates[FormantCeilings.FallbackIndex(ceilings)] : null!;
        CeilingPath? path = null;
        var stage2 = new List<Action>(3) { () => derived.ComputeStage2(pitch, intensity, set, loudOutlierFraction) };
        if (set.Has(TrackSet.Pulses))
            stage2.Add(() => { using (Timing.Measure("analysis.pulses")) pulses = PulseDetector.PeriodicCrossCorrelation(sound, pitch); });
        if (set.Has(TrackSet.FormantPath) && formantMethod == FormantCeilingMethod.Path)
            stage2.Add(() => { using (Timing.Measure("analysis.formants.path")) (formants, path) = FindPath(candidates, ceilings, pitch, ceilingScoring); });
        Parallel.Invoke([.. stage2]);
        stages.Complete(5);

        var formantsRead = formants;
        Func<double, double, RawAnalysis.WeightRow?> weightRow = (t, f0) => RawAnalysisAssembler.MeasureWeightRow(sound, formantsRead, t, f0);
        var view = new TrackView
        {
            Tracks = set,
            Duration = sound.Duration,
            SamplingFrequency = sound.SamplingFrequency,
            Sound = sound,
            Pitch = pitch,
            Intensity = intensity,
            Harmonicity = harmonicity,
            FormantMethod = formantMethod,
            FormantCeilings = ceilings,
            FormantCandidates = candidates,
            Formants = formants,
            CeilingPath = path,
            Ltas = ltas,
            Pulses = pulses,
            StretchJitter = null!, // derived from the pulses when the series need it
            WeightRow = weightRow,
            Derived = derived,
        };
        using (Timing.Measure("analysis.derived")) derived.ComputeStage3(view, weightRow, set);
        return view;
    }

    /// <summary>
    /// The ceiling path over the candidates as measured (the median would hide the label slips the
    /// scores look for), then the formants along it from the used candidates, median-smoothed.
    /// </summary>
    private static (IFormantTrack Formants, CeilingPath Path) FindPath(FormantContour[] candidates, IReadOnlyList<double> ceilings, PitchContour pitch,
        CeilingScoring? scoring)
    {
        var path = new CeilingPath();
        new CeilingPathFinder(scoring).Find(candidates, pitch, FormantCeilings.FallbackIndex(ceilings), path);
        var smoothed = new IFormantTrack?[candidates.Length];
        for (var i = 0; i < smoothed.Length; i++)
            if (path.Uses(i)) smoothed[i] = FormantMedian.Smooth(candidates[i]);
        return (new SwitchedFormantTrack(smoothed, path), path);
    }
}
