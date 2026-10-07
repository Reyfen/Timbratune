namespace Reyfen.Timbratune.Services;

/// <summary>Platform file dialogs, provided by the view layer (Avalonia StorageProvider).</summary>
public interface IFileDialogs
{
    /// <summary>Asks where to save a copy of <paramref name="sourcePath"/>; copies it and returns the target, or null if cancelled.</summary>
    Task<string?> SaveCopyAsync(string sourcePath, string suggestedName);
}

/// <summary>
/// Everything the view models need from the outside world.
/// <paramref name="ReferenceDir"/> is the folder holding reference.json and reference-audio/
/// (only used when <see cref="Features.ReferenceVoices"/> is on).
/// <paramref name="RequestMicrophone"/> asks for microphone access on platforms that need it
/// at run time (Android); null elsewhere.
/// </summary>
public sealed record AppServices(
    Core.Storage.RecordingStore Store,
    Core.Analysis.IAnalysisEngine Engine,
    Core.Audio.IAudioRecorder Recorder,
    PlaybackService Playback,
    IFileDialogs Dialogs,
    string? ReferenceDir = null,
    Func<Task<bool>>? RequestMicrophone = null,
    Action? LowerThreadPriority = null);
