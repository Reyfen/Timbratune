using Reyfen.Timbratune.Core.Analysis;

namespace Reyfen.Timbratune.Shared.State;

/// <summary>
/// The take being recorded, as the live analysis sees it. Owned by the Recording feature; the
/// live graphs and the live take's cards follow it. Live from <see cref="Started"/> until
/// <see cref="Ended"/>; after Stop it stays on the final snapshot while the saved take is analyzed.
/// </summary>
public interface ILiveSession
{
    /// <summary>Recording, or frozen on the final snapshot while the saved take is analyzed.</summary>
    bool IsLive { get; }

    /// <summary>Recording right now (audio is still arriving).</summary>
    bool IsRecording { get; }

    /// <summary>The newest snapshot; read it only on the analysis thread while <see cref="IsRecording"/> (its lists are refilled there).</summary>
    LiveSnapshot? Latest { get; }

    /// <summary>
    /// A new snapshot, raised <b>on the analysis thread</b> (the live worker; for the final one, a
    /// pool thread). Handlers do their own work there (e.g. smoothing) and marshal only the
    /// finished result to the UI thread. They must not block for long: the next update waits.
    /// </summary>
    event Action<LiveSnapshot>? SnapshotReady;

    /// <summary>A recording started (UI thread).</summary>
    event EventHandler? Started;

    /// <summary>The live views can go: the take was saved or the recording failed (UI thread).</summary>
    event EventHandler? Ended;
}
