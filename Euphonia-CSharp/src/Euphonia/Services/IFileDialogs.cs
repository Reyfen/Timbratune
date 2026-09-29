namespace Euphonia.Services;

/// <summary>Platform file dialogs, provided by the view layer (Avalonia StorageProvider).</summary>
public interface IFileDialogs
{
    /// <summary>Asks where to save a copy of <paramref name="sourcePath"/>; copies it and returns the target, or null if cancelled.</summary>
    Task<string?> SaveCopyAsync(string sourcePath, string suggestedName);
}

/// <summary>
/// Everything the view models need from the outside world.
/// <paramref name="ReferenceDir"/> is the folder holding reference.json and reference-audio/.
/// </summary>
public sealed record AppServices(
    Core.Storage.RecordingStore Store,
    Core.Analysis.IAnalysisEngine Engine,
    Core.Audio.IAudioRecorder Recorder,
    PlaybackService Playback,
    IFileDialogs Dialogs,
    string ReferenceDir);
