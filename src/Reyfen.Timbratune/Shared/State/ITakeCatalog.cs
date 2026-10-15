using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;

namespace Reyfen.Timbratune.Shared.State;

/// <summary>
/// The takes as the app shows them: read from the store, refreshed by whichever feature changed
/// them (a recording saved, an import, a rename or delete). Features read <see cref="Takes"/> and
/// follow <see cref="Changed"/> instead of each loading the store.
/// </summary>
public interface ITakeCatalog
{
    /// <summary>All takes, oldest first (by id).</summary>
    IReadOnlyList<Recording> Takes { get; }

    /// <summary>The newest take, or null when there is none.</summary>
    Recording? Latest { get; }

    /// <summary>What went wrong reading the takes folder on the last load, if anything.</summary>
    string? LoadError { get; }

    /// <summary>Reads the takes again; <see cref="Changed"/> follows on the caller's thread.</summary>
    Task ReloadAsync();

    event EventHandler? Changed;
}

public sealed class TakeCatalog(RecordingStore store) : ITakeCatalog
{
    public IReadOnlyList<Recording> Takes { get; private set; } = [];
    public Recording? Latest => Takes.Count > 0 ? Takes[^1] : null;
    public string? LoadError { get; private set; }

    public event EventHandler? Changed;

    public async Task ReloadAsync()
    {
        try
        {
            Takes = await Task.Run(store.Load);
            LoadError = store.Unreadable.Count == 0
                ? null
                : $"couldn't read {string.Join(", ", store.Unreadable)} in the takes folder 🌧️";
        }
        catch (Exception ex)
        {
            LoadError = $"couldn't load recordings 🌧️ {ex.Message}";
            Takes = [];
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
