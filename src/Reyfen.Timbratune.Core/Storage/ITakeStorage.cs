namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// Where takes are kept: named takes, each holding named files (take.wav, take.json, …).
/// <see cref="RecordingStore"/> is the repository on top; this port is what changes per
/// platform (<see cref="FileSystemTakeStorage"/> on the native heads: one folder per take;
/// later a browser's private file system). Take and file names are single path segments.
/// The file-system behaviour (which exceptions mean what) is the contract: a missing or locked
/// take throws <see cref="IOException"/>, a forbidden one <see cref="UnauthorizedAccessException"/>.
/// </summary>
public interface ITakeStorage
{
    /// <summary>The names of all takes (any order; also ones still being written).</summary>
    IReadOnlyList<string> ListTakes();

    /// <summary>Makes a new, empty take called <paramref name="name"/>, or "name (2)", "name (3)"… if taken; returns its name.</summary>
    string CreateTake(string name);

    /// <summary>Renames a take; false when <paramref name="newName"/> exists or the take can't be moved (e.g. a player holds it open).</summary>
    bool TryRenameTake(string take, string newName);

    /// <summary>Deletes a take and everything in it.</summary>
    void DeleteTake(string take);

    bool Exists(string take, string file);

    Stream OpenRead(string take, string file);

    /// <summary>Writes a file atomically: <paramref name="write"/> fills a temporary file, which then replaces the old one.</summary>
    void Write(string take, string file, Action<Stream> write);

    /// <summary>Copies a local file in (fails if the take already has that file).</summary>
    void CopyIn(string take, string file, string sourcePath);

    /// <summary>Moves a local file in (fails if the take already has that file).</summary>
    void MoveIn(string take, string file, string sourcePath);

    void DeleteFile(string take, string file);

    /// <summary>When the file was last written (local time), or null if it isn't there.</summary>
    DateTime? LastWriteTime(string take, string file);

    /// <summary>
    /// A path that native code (players, share sheets, decoders) can open, or null where the
    /// storage has no file system behind it.
    /// </summary>
    string? LocalPath(string take, string file);
}

/// <summary>
/// The app's own small files that aren't takes: settings documents, downloaded tools. Names may
/// contain '/' for sub-folders. Writes are atomic (a temporary file, then a rename), so a crash
/// mid-write leaves the previous version.
/// </summary>
public interface IDataStore
{
    bool Exists(string name);

    Stream OpenRead(string name);

    void Write(string name, Action<Stream> write);

    void Delete(string name);

    /// <summary>Renames <paramref name="name"/> to <paramref name="newName"/>, replacing it if it exists.</summary>
    void Move(string name, string newName);
}
