namespace Reyfen.Timbratune.Core.Analysis;

/// <summary>
/// Turns a WAV file into metrics + register detail. The app uses
/// <see cref="AcousticsAnalysisEngine"/> (pure C#, every platform); the test
/// suite also has a Praat-backed engine used as a reference.
/// </summary>
public interface IAnalysisEngine
{
    /// <summary>True when the engine can run.</summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable reason when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    /// <param name="progress">Receives the fraction done (0–1) while the analysis runs, when the engine can tell.</param>
    Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default, IProgress<double>? progress = null);

    /// <summary>
    /// Analyzes a WAV read from a stream. Engines that need a file (the Praat reference) get
    /// this default: the stream is copied to a temporary file first.
    /// </summary>
    async Task<AnalysisResult> AnalyzeAsync(Stream wav, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default, IProgress<double>? progress = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"timbratune-analyze-{Guid.NewGuid():N}.wav");
        try
        {
            await using (var file = File.Create(temp)) await wav.CopyToAsync(file, cancellationToken);
            return await AnalyzeAsync(temp, registerFloorHz, cancellationToken, progress);
        }
        finally
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }
}
