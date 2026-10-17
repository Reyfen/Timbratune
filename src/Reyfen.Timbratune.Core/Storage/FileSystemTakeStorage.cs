namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// Takes as folders under one directory (<see cref="DataPaths.TakesDir"/>): today's layout,
/// byte for byte. A file is written to "name.tmp" and then renamed over the old one.
/// </summary>
public sealed class FileSystemTakeStorage(string takesDir) : ITakeStorage
{
    public string TakesDir { get; } = Path.GetFullPath(takesDir);

    public IReadOnlyList<string> ListTakes() =>
        Directory.Exists(TakesDir) ? Directory.GetDirectories(TakesDir).Select(d => Path.GetFileName(d)).ToList() : [];

    public string CreateTake(string name)
    {
        CheckName(name);
        Directory.CreateDirectory(TakesDir);
        var unique = name;
        for (var n = 2; Directory.Exists(Path.Combine(TakesDir, unique)); n++) unique = $"{name} ({n})";
        Directory.CreateDirectory(Path.Combine(TakesDir, unique));
        return unique;
    }

    public bool TryRenameTake(string take, string newName)
    {
        var target = Folder(newName);
        if (Directory.Exists(target)) return false;
        try
        {
            Directory.Move(Folder(take), target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void DeleteTake(string take) => Directory.Delete(Folder(take), recursive: true);

    public bool Exists(string take, string file) => File.Exists(FilePath(take, file));

    public Stream OpenRead(string take, string file) => File.OpenRead(FilePath(take, file));

    public void Write(string take, string file, Action<Stream> write) => WriteAtomically(FilePath(take, file), write);

    public void CopyIn(string take, string file, string sourcePath) => File.Copy(sourcePath, FilePath(take, file));

    public void MoveIn(string take, string file, string sourcePath) => File.Move(sourcePath, FilePath(take, file));

    public void DeleteFile(string take, string file) => File.Delete(FilePath(take, file));

    public DateTime? LastWriteTime(string take, string file)
    {
        var path = FilePath(take, file);
        return File.Exists(path) ? File.GetLastWriteTime(path) : null;
    }

    public string? LocalPath(string take, string file) => FilePath(take, file);

    /// <summary>The take's folder.</summary>
    public string Folder(string take)
    {
        CheckName(take);
        return Path.Combine(TakesDir, take);
    }

    private string FilePath(string take, string file)
    {
        CheckName(file);
        return Path.Combine(Folder(take), file);
    }

    internal static void WriteAtomically(string path, Action<Stream> write)
    {
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp)) write(stream);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>One path segment: never a way out of the takes folder.</summary>
    internal static void CheckName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.IndexOfAny(['/', '\\']) >= 0 || Path.IsPathRooted(name))
            throw new ArgumentException($"Not a plain name: \"{name}\".", nameof(name));
    }
}

/// <summary><see cref="IDataStore"/> over a folder; names with '/' are sub-folders.</summary>
public sealed class FileSystemDataStore(string root) : IDataStore
{
    public string Root { get; } = Path.GetFullPath(root);

    public bool Exists(string name) => File.Exists(PathOf(name));

    public Stream OpenRead(string name) => File.OpenRead(PathOf(name));

    public void Write(string name, Action<Stream> write)
    {
        var path = PathOf(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        FileSystemTakeStorage.WriteAtomically(path, write);
    }

    public void Delete(string name) => File.Delete(PathOf(name));

    public void Move(string name, string newName)
    {
        var target = PathOf(newName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(PathOf(name), target, overwrite: true);
    }

    private string PathOf(string name)
    {
        var segments = name.Split('/');
        foreach (var s in segments) FileSystemTakeStorage.CheckName(s);
        return Path.Combine([Root, .. segments]);
    }
}
