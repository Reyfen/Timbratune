using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Ceilings;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// Takes analysed by an older version keep their numbers (owner's decision, RESONANCE-PLAN F7). This
/// analyses such a take's audio again with today's engine and keeps the result beside the original
/// (<see cref="RecordingStore.SaveReanalysis"/>), so both can be shown. How they are shown is the UI's
/// part; nothing here changes the take's own files.
/// </summary>
public sealed class TakeReanalysis(RecordingStore store, IAnalysisEngine engine)
{
    /// <summary>The formant method today's analysis uses; takes measured otherwise get a re-analysis.</summary>
    public const FormantCeilingMethod Current = FormantCeilingMethod.Path;

    /// <summary>Whether <paramref name="take"/> was measured by an older method and has no re-analysis yet.</summary>
    public bool Needs(Recording take) =>
        FormantCeilings.MethodOf(take) != Current && !store.HasReanalysis(take) && AudioOf(take) is not null;

    /// <summary>
    /// Analyses the take's audio with today's method and saves it beside the take; null when the take
    /// doesn't need it (see <see cref="Needs"/>) or the engine can't run.
    /// </summary>
    public async Task<AnalysisResult?> ReanalyseAsync(Recording take, CancellationToken cancellationToken = default)
    {
        if (!engine.IsAvailable || !Needs(take) || AudioOf(take) is not { } audio) return null;
        var floor = take.Register?.FloorHz ?? AnalysisPostProcessor.DefaultRegisterFloorHz;
        var result = await engine.WithFormantMethod(Current).AnalyzeAsync(audio, floor, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        store.SaveReanalysis(take, result);
        return result;
    }

    private string? AudioOf(Recording take) => store.Paths.Resolve(take.Audio) is { } path && File.Exists(path) ? path : null;
}
