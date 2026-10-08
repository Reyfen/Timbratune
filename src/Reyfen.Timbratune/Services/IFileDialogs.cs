namespace Reyfen.Timbratune.Services;

/// <summary>Platform file dialogs, provided by the view layer (Avalonia StorageProvider).</summary>
public interface IFileDialogs
{
    /// <summary>Asks where to save a copy of <paramref name="sourcePath"/>; copies it and returns the target, or null if cancelled.</summary>
    Task<string?> SaveCopyAsync(string sourcePath, string suggestedName);

    /// <summary>
    /// Asks where to save a new file of one type, then lets <paramref name="write"/> fill it.
    /// Returns where it went (a path, or just the name where there's none, e.g. on Android), or null if cancelled.
    /// </summary>
    /// <param name="mimeType">For pickers that work by type (Android): the extension is kept as given.</param>
    Task<string?> SaveAsync(string title, string suggestedName, string typeName, string extension, string mimeType,
        Func<Stream, Task> write);

    /// <summary>Asks for one or more files of the given extensions ("wav", …); empty if cancelled.</summary>
    Task<IReadOnlyList<PickedFile>> OpenFilesAsync(string title, string typeName, IReadOnlyList<string> extensions);

    /// <summary>Opens a folder in the system's file manager; false where that isn't possible.</summary>
    Task<bool> OpenFolderAsync(string path);
}

/// <summary>A file chosen in a picker: its name, and a way to read it (on Android there's often no path).</summary>
public sealed record PickedFile(string Name, Func<Task<Stream>> OpenRead);

/// <summary>
/// Everything the view models need from the outside world.
/// <paramref name="ReferenceDir"/> is the folder holding reference.json and reference-audio/
/// (only used when <see cref="Features.ReferenceVoices"/> is on).
/// <paramref name="RequestMicrophone"/> asks for microphone access on platforms that need it
/// at run time (Android); null elsewhere.
/// <paramref name="Decoder"/> reads MP3 / FLAC for import (null: WAV and .tmbr only).
/// <paramref name="DataFolderHint"/>, where the takes folder can't be opened from the app
/// (Android), says where to find it instead of offering a link.
/// </summary>
public sealed record AppServices(
    Core.Storage.RecordingStore Store,
    Core.Analysis.IAnalysisEngine Engine,
    Core.Audio.IAudioRecorder Recorder,
    PlaybackService Playback,
    IFileDialogs Dialogs,
    string? ReferenceDir = null,
    Func<Task<bool>>? RequestMicrophone = null,
    Action? LowerThreadPriority = null,
    Core.Audio.IAudioDecoder? Decoder = null,
    string? DataFolderHint = null);
