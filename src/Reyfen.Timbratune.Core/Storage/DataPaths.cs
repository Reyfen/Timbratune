namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// Per-user data folder, same layout the Electron app / analyze.py
/// --output-root use:
/// <code>
///   recordings.json            index (sorted by id, pretty JSON)
///   audio/NNN.wav              the take's audio (zero-padded id)
///   analysis/&lt;id&gt;.json         per-frame register detail
///   analysis/&lt;id&gt;.series.json  per-frame lists (pitch, loudness, HNR, F1-F3, weight, jitter), for export
///   analysis/&lt;id&gt;-insight.json  cached insight (not written by v1)
/// </code>
/// </summary>
public sealed class DataPaths
{
    public const string DataDirEnvVar = "TIMBRATUNE_DATA_DIR";

    public DataPaths(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }
    public string RecordingsJson => Path.Combine(Root, "recordings.json");
    public string AudioDir => Path.Combine(Root, "audio");
    public string AnalysisDir => Path.Combine(Root, "analysis");

    /// <summary>The take's per-frame lists (see <see cref="Models.TakeSeries"/>).</summary>
    public string SeriesFile(int id) => Path.Combine(AnalysisDir, $"{id}.series.json");

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
    /// Resolves a relative path stored in recordings.json ("audio/001.wav")
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
}
