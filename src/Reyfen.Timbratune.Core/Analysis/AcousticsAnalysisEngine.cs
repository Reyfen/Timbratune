using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Core.Analysis.Tracks;
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

    private readonly IAnalysisScheduler _scheduler;

    public AcousticsAnalysisEngine() : this(null)
    {
    }

    /// <param name="scheduler">Where the analysis runs; default: the thread pool.</param>
    public AcousticsAnalysisEngine(IAnalysisScheduler? scheduler) => _scheduler = scheduler ?? NativeAnalysisScheduler.Default;

    /// <summary>The loudest share of frames ignored for the vowel-core loudness gate (see <see cref="RawAnalysisAssembler.LoudOutlierFraction"/>); 0 = analyze.py's gate.</summary>
    public double LoudOutlierFraction { get; init; } = RawAnalysisAssembler.LoudOutlierFraction;

    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    public Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default, IProgress<double>? progress = null) =>
        _scheduler.RunSavedAsync(() =>
        {
            Sound sound;
            using (var stream = File.OpenRead(wavPath)) sound = WavDecoder.Decode(stream);
            return Analyze(sound, registerFloorHz, cancellationToken, progress);
        }, cancellationToken);

    /// <summary>Analyzes a WAV read from <paramref name="wav"/> (no file needed, e.g. storage without paths).</summary>
    public Task<AnalysisResult> AnalyzeAsync(Stream wav, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default, IProgress<double>? progress = null) =>
        _scheduler.RunSavedAsync(() => Analyze(WavDecoder.Decode(wav), registerFloorHz, cancellationToken, progress), cancellationToken);

    /// <summary>Analyzes samples already in memory (e.g. decoded by a platform decoder).</summary>
    public Task<AnalysisResult> AnalyzeAsync(Sound sound, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default, IProgress<double>? progress = null) =>
        _scheduler.RunSavedAsync(() => Analyze(sound, registerFloorHz, cancellationToken, progress), cancellationToken);

    private AnalysisResult Analyze(Sound sound, double registerFloorHz, CancellationToken cancellationToken, IProgress<double>? progress)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Stage weights: share of the analysis time measured on a 27.6 s take (Release).
        var stages = new StageProgress(progress is null ? null : progress.Report, 93.5, 2, 1, 2.5);
        TrackView tracks;
        using (Timing.Measure("analysis.measure")) tracks = MeasureTracks(sound, LoudOutlierFraction, stages.Stage(0));
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
    }

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

    /// <summary>The frame-level tracks of the whole sound (all tracks, the default loud-vowel gate).</summary>
    /// <param name="progress">Called with the fraction done (0–1), from any thread.</param>
    public static TrackView MeasureTracks(Sound sound, Action<double>? progress = null) =>
        MeasureTracks(sound, RawAnalysisAssembler.LoudOutlierFraction, progress);

    /// <summary>The same with another loud-vowel gate (0 = analyze.py's).</summary>
    public static TrackView MeasureTracks(Sound sound, double loudOutlierFraction, Action<double>? progress = null) =>
        SavedTrackProvider.Measure(sound, TrackSet.All, loudOutlierFraction, progress);
}
