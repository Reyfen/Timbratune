namespace Euphonia.Core.Analysis;

/// <summary>
/// Turns a WAV file into metrics + register detail. The desktop app uses
/// <see cref="PraatAnalysisEngine"/>; Praat has no Android/iOS build, so a
/// mobile head will need another implementation behind this interface.
/// </summary>
public interface IAnalysisEngine
{
    /// <summary>True when the engine can run (e.g. the Praat binary was found).</summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable reason when <see cref="IsAvailable"/> is false.</summary>
    string? UnavailableReason { get; }

    Task<AnalysisResult> AnalyzeAsync(string wavPath, double registerFloorHz = AnalysisPostProcessor.DefaultRegisterFloorHz,
        CancellationToken cancellationToken = default);
}
