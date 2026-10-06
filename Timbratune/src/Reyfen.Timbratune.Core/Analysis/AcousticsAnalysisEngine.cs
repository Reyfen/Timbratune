using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Acoustics.Intensity;
using Reyfen.Timbratune.Acoustics.Pitch;
using Reyfen.Timbratune.Acoustics.Spectral;
using Reyfen.Timbratune.Acoustics.Voice;

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

    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            Sound sound;
            using (var stream = File.OpenRead(wavPath)) sound = WavDecoder.Decode(stream);
            cancellationToken.ThrowIfCancellationRequested();
            return AnalysisPostProcessor.Process(Measure(sound), registerFloorHz);
        }, cancellationToken);

    /// <summary>All raw measurements of one sound (independent analyses run in parallel).</summary>
    public static RawAnalysis Measure(Sound sound) => RawAnalysisAssembler.Assemble(MeasureTracks(sound));

    /// <summary>The frame-level tracks of the whole sound.</summary>
    public static FrameTracks MeasureTracks(Sound sound)
    {
        PitchContour pitch = null!;
        IntensityContour intensity = null!;
        HarmonicityContour harmonicity = null!;
        FormantContour formant5500 = null!, formant5000 = null!;
        Ltas ltas = null!;
        Parallel.Invoke(
            () => pitch = PitchAnalyzer.Autocorrelation(sound, 0, PitchFloor, PitchCeiling),
            () => intensity = IntensityAnalyzer.Analyze(sound, PitchFloor),
            () => harmonicity = HarmonicityAnalyzer.CrossCorrelation(sound, 0.01, PitchFloor, 0.1, 1.0),
            () => formant5500 = FormantAnalyzer.Burg(sound, 0, 5, RawAnalysisAssembler.FormantCeiling, 0.025, 50),
            () => formant5000 = FormantAnalyzer.Burg(sound, 0, 5, 5000, 0.025, 50),
            () => ltas = Ltas.FromSound(sound, 100));

        var pulses = PulseDetector.PeriodicCrossCorrelation(sound, pitch);
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
        };
    }
}
