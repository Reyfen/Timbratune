namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// Per-user data folder. Every take is one folder under <c>takes/</c>, so takes can be
/// added, copied or removed by hand:
/// <code>
///   takes/003 untitled take/
///     take.wav        the take's audio
///     take.json       label, note, date, metrics, audio format (small: read at startup)
///     detail.json     10 ms pitch contour, phrases, register summary, trends
///     series.json     per-frame lists (pitch, loudness, HNR, F1-F3, weight, jitter), for export
/// </code>
/// The folder name is cosmetic (id and label when it was made); the id is in take.json.
/// Audio or .tmbr files dropped into <c>takes/</c> are imported on the next start.
/// Before 0.2.0 takes were listed in <c>recordings.json</c> with <c>audio/NNN.wav</c> and
/// <c>analysis/&lt;id&gt;.json</c> (the Electron app's layout); that is converted on first use.
/// </summary>
public sealed class DataPaths
{
    public const string DataDirEnvVar = "TIMBRATUNE_DATA_DIR";

    public DataPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }
    /// <summary>One folder per take.</summary>
    public string TakesDir => Path.Combine(Root, "takes");

    // The layout before 0.2.0, converted by RecordingStore on first use.
    public string RecordingsJson => Path.Combine(Root, "recordings.json");
    public string AudioDir => Path.Combine(Root, "audio");
    public string AnalysisDir => Path.Combine(Root, "analysis");

    /// <summary>Where builds from before the rename to Timbratune kept their takes.</summary>
    public const string LegacyFolderName = "Euphonia-CSharp";

    /// <summary>
    /// $TIMBRATUNE_DATA_DIR if set, otherwise &lt;ApplicationData&gt;/Timbratune —
    /// deliberately separate from the original Electron app's %APPDATA%\Euphonia so the
    /// two never write the same index. Takes recorded before the rename (in
    /// &lt;ApplicationData&gt;/Euphonia-CSharp) are moved over on first use.
    /// </summary>
    public static DataPaths Default()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirEnvVar);
        if (!string.IsNullOrWhiteSpace(overridden)) return new DataPaths(overridden);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        var root = Path.Combine(appData, "Timbratune");
        var legacy = Path.Combine(appData, LegacyFolderName);
        if (!Directory.Exists(root) && Directory.Exists(legacy))
        {
            try
            {
                Directory.Move(legacy, root);
            }
            catch (IOException)
            {
                return new DataPaths(legacy); // in use or on another volume: keep using it as is
            }
            catch (UnauthorizedAccessException)
            {
                return new DataPaths(legacy);
            }
        }
        return new DataPaths(root);
    }

    /// <summary>
    /// Resolves a relative path such as <see cref="Models.Recording.Audio"/> ("takes/001 x/take.wav")
    /// against the root. Returns null if it would escape the root — the same
    /// guard as electron/src/protocol.ts resolveWithinBase.
    /// </summary>
    public string? Resolve(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var full = Path.GetFullPath(Path.Combine(Root, relative));
        var rootWithSep = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>
    /// Moves a data folder's takes (both layouts) from <paramref name="from"/> into
    /// <paramref name="to"/>, file by file (they may be on different volumes), when
    /// <paramref name="from"/> has any and <paramref name="to"/> has none yet. Safe to call
    /// on every start; an interrupted move continues next time.
    /// </summary>
    public static void MoveContents(string from, string to)
    {
        from = Path.GetFullPath(from);
        to = Path.GetFullPath(to);
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(from)) return;
        string[] entries = ["recordings.json", "audio", "analysis", "takes"];
        bool Has(string root) => entries.Any(e => File.Exists(Path.Combine(root, e)) || Directory.Exists(Path.Combine(root, e)));
        if (!Has(from)) return;
        // Only into an empty folder, unless an earlier move was interrupted (its marker is still there):
        // two folders that both have takes are never merged.
        var marker = Path.Combine(to, ".moving");
        if (Has(to) && !File.Exists(marker)) return;
        Directory.CreateDirectory(to);
        File.WriteAllText(marker, from);
        foreach (var name in entries)
        {
            var source = Path.Combine(from, name);
            var target = Path.Combine(to, name);
            if (File.Exists(source)) MoveFile(source, target);
            else if (Directory.Exists(source))
            {
                foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                    MoveFile(file, Path.Combine(target, Path.GetRelativePath(source, file)));
                Directory.Delete(source, recursive: true);
            }
        }
        File.Delete(marker);
    }

    private static void MoveFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            File.Delete(source); // copied on an earlier, interrupted run
            return;
        }
        // Copy to a temporary name, then rename: a half-copied file never looks complete.
        var tmp = target + ".moving";
        File.Copy(source, tmp, overwrite: true);
        File.Move(tmp, target);
        File.Delete(source);
    }
}
