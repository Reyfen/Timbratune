namespace Reyfen.Timbratune.Shared.Platform;

/// <summary>
/// What the platform the app runs on can do. Each head registers its own, so shared code asks
/// this instead of checking <c>OperatingSystem.Is…</c>.
/// </summary>
public interface IPlatformInfo
{
    /// <summary>A phone or tablet (Android, iOS): the cheaper, touch-first look.</summary>
    bool IsMobile { get; }

    /// <summary>Running in a browser (WebAssembly).</summary>
    bool IsBrowser { get; }

    /// <summary>Background threads are available (the analysis may run off the UI thread).</summary>
    bool SupportsThreads { get; }

    /// <summary>The takes folder can be opened in the system's file manager.</summary>
    bool CanOpenDataFolder { get; }

    /// <summary>
    /// Where <see cref="CanOpenDataFolder"/> is false and the folder can still be reached some
    /// other way (Android: over USB), how to find it; null otherwise.
    /// </summary>
    string? DataFolderHint { get; }

    /// <summary>There is a share sheet (<see cref="IFileSharer"/> is registered).</summary>
    bool CanShare { get; }

    /// <summary>Other programs can be started (e.g. an ffmpeg decoder).</summary>
    bool CanRunProcesses { get; }
}

/// <summary>A head's <see cref="IPlatformInfo"/>; the defaults describe a desktop.</summary>
public sealed record PlatformInfo : IPlatformInfo
{
    public bool IsMobile { get; init; }
    public bool IsBrowser { get; init; }
    public bool SupportsThreads { get; init; } = true;
    public bool CanOpenDataFolder { get; init; } = true;
    public string? DataFolderHint { get; init; }
    public bool CanShare { get; init; }
    public bool CanRunProcesses { get; init; } = true;
}
