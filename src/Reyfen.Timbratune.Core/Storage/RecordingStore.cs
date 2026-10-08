using System.Text;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// The takes on disk: one folder per take under <see cref="DataPaths.TakesDir"/> (see
/// <see cref="DataPaths"/> for the files), found by listing that folder; there is no index.
/// The older layout (recordings.json + audio/ + analysis/) is converted on first use.
/// All writes are serialized through one lock; files are written then renamed.
/// </summary>
public sealed class RecordingStore
{
    public const string AudioName = "take.wav", TakeJson = "take.json", DetailJson = "detail.json", SeriesJson = "series.json";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();

    public RecordingStore(DataPaths paths)
    {
        Paths = paths;
    }

    public DataPaths Paths { get; }

    /// <summary>Takes whose folder couldn't be read on the last <see cref="Load"/> (folder names).</summary>
    public IReadOnlyList<string> Unreadable { get; private set; } = [];

    /// <summary>All takes sorted by id, read from their folders. Converts the old layout first if it's there.</summary>
    public List<Recording> Load()
    {
        lock (_gate)
        {
            MigrateLegacyUnlocked();
            return ScanUnlocked();
        }
    }

    public RecordingDetail? LoadDetail(Recording recording)
    {
        var path = Paths.Resolve(recording.Detail);
        if (path is null || !File.Exists(path)) return null;
        try
        {
            return TimbratuneJson.ReadDetail(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Adds an analyzed take: the next id (max + 1), a new folder with the audio copied in as
    /// take.wav, detail.json, series.json (when given) and take.json. <paramref name="entry"/>
    /// carries the metrics, label, note and date; its id and paths are filled in here.
    /// </summary>
    public Recording Add(Recording entry, RecordingDetail detail, string audioSourcePath, TakeSeries? series = null)
    {
        lock (_gate)
        {
            if (string.IsNullOrEmpty(entry.Date)) entry.Date = DateTime.Now.ToString("yyyy-MM-dd");
            entry.RecordedAt ??= entry.Date == DateTime.Now.ToString("yyyy-MM-dd") ? Recording.Timestamp(DateTime.Now) : null;
            entry.Id = NextIdUnlocked();
            var folder = NewFolderUnlocked(entry.Id, entry.Label);
            try
            {
                var audio = Path.Combine(folder, AudioName);
                File.Copy(audioSourcePath, audio);
                WriteTakeUnlocked(folder, entry, detail, series);
            }
            catch
            {
                TryDeleteFolder(folder);
                throw;
            }
            SetPaths(entry, folder);
            return entry;
        }
    }

    /// <summary>
    /// Adds a take read from a .tmbr file (<see cref="TakeArchive.Extract"/>): a new id, the
    /// file's label, note, date, metrics, detail and series, and its audio (moved in from
    /// <paramref name="extractedAudio"/>). Nothing is analyzed again.
    /// </summary>
    public Recording AddImported(TakeExport export, string extractedAudio)
    {
        var entry = export.Metrics;
        entry.Label = export.Take.Label ?? entry.Label;
        entry.Note = export.Take.Note ?? entry.Note;
        entry.Date = export.Take.Date ?? entry.Date;
        entry.RecordedAt = export.Take.RecordedAt;
        entry.DurationS = export.Take.DurationS ?? entry.DurationS;
        entry.SourceFile = export.Take.SourceFile ?? "";
        lock (_gate)
        {
            entry.Id = NextIdUnlocked();
            var folder = NewFolderUnlocked(entry.Id, entry.Label);
            try
            {
                File.Move(extractedAudio, Path.Combine(folder, AudioName));
                WriteTakeUnlocked(folder, entry, export.Detail ?? new RecordingDetail(), export.Series);
            }
            catch
            {
                TryDeleteFolder(folder);
                throw;
            }
            SetPaths(entry, folder);
            return entry;
        }
    }

    /// <summary>Rewrites a take's detail.json (e.g. after re-analyzing its audio).</summary>
    public void SaveDetail(Recording recording, RecordingDetail detail)
    {
        lock (_gate)
        {
            var folder = FolderOf(recording);
            WriteText(Path.Combine(folder, DetailJson), TimbratuneJson.WriteDetail(detail));
            // take.json carries the analysis settings that come from the detail (e.g. the trend step).
            WriteTakeJsonUnlocked(folder, recording, detail);
        }
    }

    /// <summary>Whether the take's per-frame lists are saved (takes analyzed before they were kept have none).</summary>
    public bool HasSeries(Recording recording) => SeriesFileOf(recording) is { } path && File.Exists(path);

    /// <summary>
    /// The take's per-frame lists, read from its series.json; null if absent or unreadable.
    /// They can be large: callers use them and let them go (nothing caches them).
    /// </summary>
    public TakeSeries? LoadSeries(Recording recording)
    {
        var path = SeriesFileOf(recording);
        if (path is null || !File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return TimbratuneJson.ReadSeries(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Writes (or replaces) the take's series.json.</summary>
    public void SaveSeries(Recording recording, TakeSeries series)
    {
        lock (_gate) WriteSeries(Path.Combine(FolderOf(recording), SeriesJson), series);
    }

    /// <summary>
    /// Renames a take: its label in take.json, and its folder ("NNN new label") when
    /// <paramref name="renameFolder"/> and nothing holds the folder open (otherwise the folder
    /// keeps its old, cosmetic name). Returns the take as now stored.
    /// </summary>
    public Recording Rename(Recording recording, string label, bool renameFolder = true)
    {
        label = label.Trim();
        lock (_gate)
        {
            var folder = FolderOf(recording);
            var json = Path.Combine(folder, TakeJson);
            TakeExport take;
            using (var stream = File.OpenRead(json))
                take = TimbratuneJson.ReadExport(stream) ?? throw new InvalidDataException($"Can't read {json}.");
            take.Take.Label = label;
            take.Metrics.Label = label;
            WriteExportJson(json, take);

            var renamed = Path.Combine(Paths.TakesDir, $"{take.Take.Id:000} {SafeName(label)}".TrimEnd());
            if (renameFolder && !string.Equals(renamed, folder, StringComparison.Ordinal) && !Directory.Exists(renamed))
            {
                try
                {
                    Directory.Move(folder, renamed);
                    folder = renamed;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // e.g. its audio is open in a player: the name inside take.json is what counts
                }
            }
            return ToRecording(folder, take);
        }
    }

    /// <summary>Deletes one take's folder. Unknown id → no-op.</summary>
    public void Delete(int id)
    {
        lock (_gate)
        {
            var entry = ScanUnlocked().FirstOrDefault(r => r.Id == id);
            if (entry is not null) TryDeleteFolder(FolderOf(entry));
        }
    }

    /// <summary>The folder holding <paramref name="recording"/> (from its paths).</summary>
    public string FolderOf(Recording recording) =>
        Path.GetDirectoryName(Paths.Resolve(recording.Detail) ?? Paths.Resolve(recording.Audio)
                              ?? throw new InvalidOperationException($"Take #{recording.Id} has no folder."))!;

    private string? SeriesFileOf(Recording recording) =>
        Paths.Resolve(recording.Detail) is { } detail ? Path.Combine(Path.GetDirectoryName(detail)!, SeriesJson) : null;

    // ---- reading ----

    private List<Recording> ScanUnlocked()
    {
        var takes = new List<Recording>();
        var unreadable = new List<string>();
        if (Directory.Exists(Paths.TakesDir))
        {
            var seen = new HashSet<int>();
            var renumber = new List<(string Folder, TakeExport Take)>();
            // Name order: the zero-padded id prefix keeps the original of a copied duplicate first.
            foreach (var folder in Directory.GetDirectories(Paths.TakesDir).Order(StringComparer.Ordinal))
            {
                var json = Path.Combine(folder, TakeJson);
                if (!File.Exists(json)) continue; // not a take (yet)
                TakeExport? take;
                try
                {
                    using var stream = File.OpenRead(json);
                    take = TimbratuneJson.ReadExport(stream);
                }
                catch (Exception)
                {
                    take = null;
                }
                if (take is null)
                {
                    unreadable.Add(Path.GetFileName(folder));
                    continue;
                }
                if (take.Take.Id <= 0 || !seen.Add(take.Take.Id))
                {
                    renumber.Add((folder, take)); // e.g. a take folder copied in from another device
                    continue;
                }
                takes.Add(ToRecording(folder, take));
            }
            foreach (var (folder, take) in renumber)
            {
                take.Take.Id = Math.Max(seen.DefaultIfEmpty(0).Max(), takes.Select(t => t.Id).DefaultIfEmpty(0).Max()) + 1;
                seen.Add(take.Take.Id);
                try
                {
                    WriteExportJson(Path.Combine(folder, TakeJson), take);
                }
                catch (IOException)
                {
                    // read-only: it still shows, under the new id, until the next start
                }
                takes.Add(ToRecording(folder, take));
            }
        }
        Unreadable = unreadable;
        takes.Sort((a, b) => a.Id.CompareTo(b.Id));
        return takes;
    }

    private Recording ToRecording(string folder, TakeExport take)
    {
        var r = take.Metrics;
        r.Id = take.Take.Id;
        r.Label = take.Take.Label ?? "";
        r.Note = take.Take.Note ?? "";
        r.Date = take.Take.Date ?? "";
        r.RecordedAt = take.Take.RecordedAt ?? AudioTime(folder, r.Date);
        r.DurationS = take.Take.DurationS ?? r.DurationS;
        r.SourceFile = take.Take.SourceFile ?? "";
        SetPaths(r, folder);
        return r;
    }

    /// <summary>
    /// For takes saved before the time was kept: the audio file's time, when it's on the take's
    /// date (the WAV is written as the take is recorded, and moving it keeps its time).
    /// </summary>
    private static string? AudioTime(string folder, string date)
    {
        var audio = Path.Combine(folder, AudioName);
        if (!File.Exists(audio)) return null;
        var written = File.GetLastWriteTime(audio);
        return written.ToString("yyyy-MM-dd") == date ? Recording.Timestamp(written) : null;
    }

    private void SetPaths(Recording r, string folder)
    {
        var relative = Path.GetRelativePath(Paths.Root, folder).Replace('\\', '/');
        r.Audio = File.Exists(Path.Combine(folder, AudioName)) ? $"{relative}/{AudioName}" : null;
        r.Detail = $"{relative}/{DetailJson}";
    }

    private int NextIdUnlocked() => ScanUnlocked().Select(r => r.Id).DefaultIfEmpty(0).Max() + 1;

    // ---- writing ----

    /// <summary>A new, empty folder "NNN label" (made unique if needed).</summary>
    private string NewFolderUnlocked(int id, string? label)
    {
        Directory.CreateDirectory(Paths.TakesDir);
        var name = $"{id:000} {SafeName(label)}".TrimEnd();
        var folder = Path.Combine(Paths.TakesDir, name);
        for (var n = 2; Directory.Exists(folder); n++) folder = Path.Combine(Paths.TakesDir, $"{name} ({n})");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>A label as part of a folder name: no path or reserved characters, at most 40 characters.</summary>
    public static string SafeName(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "";
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '/', '\\', '|', '?', '*']).ToHashSet();
        var chars = label.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray();
        var name = new string(chars).Trim('.', ' ');
        return name.Length > 40 ? name[..40].TrimEnd('.', ' ') : name;
    }

    // take.json last: a folder without it isn't listed, so a half-written take never shows.
    private void WriteTakeUnlocked(string folder, Recording entry, RecordingDetail detail, TakeSeries? series)
    {
        WriteText(Path.Combine(folder, DetailJson), TimbratuneJson.WriteDetail(detail));
        if (series is not null) WriteSeries(Path.Combine(folder, SeriesJson), series);
        WriteTakeJsonUnlocked(folder, entry, detail);
    }

    private static void WriteTakeJsonUnlocked(string folder, Recording entry, RecordingDetail detail)
    {
        var audio = Path.Combine(folder, AudioName);
        WriteExportJson(Path.Combine(folder, TakeJson), TakeArchive.Describe(entry, detail, File.Exists(audio) ? audio : null, keepSource: true));
    }

    private static void WriteExportJson(string path, TakeExport take)
    {
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp)) TimbratuneJson.WriteExport(stream, take);
        File.Move(tmp, path, overwrite: true);
    }

    // Streamed to the file (the lists can be a few MB as text) and written then renamed.
    private static void WriteSeries(string path, TakeSeries series)
    {
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp)) TimbratuneJson.WriteSeries(stream, series);
        File.Move(tmp, path, overwrite: true);
    }

    private static void WriteText(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, Utf8NoBom);
        File.Move(tmp, path, overwrite: true);
    }

    private void TryDeleteFolder(string folder)
    {
        // Only ever a folder directly inside takes/.
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(folder)), Path.GetFullPath(Paths.TakesDir), StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
            // locked by a player: take.json goes at least, so it no longer shows
            try { File.Delete(Path.Combine(folder, TakeJson)); } catch (IOException) { }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ---- the layout before 0.2.0 ----

    /// <summary>
    /// recordings.json + audio/NNN.wav + analysis/&lt;id&gt;.json(+ .series.json) → one folder per
    /// take. Files are moved, take by take, so it can stop half-way and continue on the next
    /// start. recordings.json is then renamed to recordings.json.migrated (kept as a record).
    /// Ids are kept unless a take folder already uses one.
    /// </summary>
    private void MigrateLegacyUnlocked()
    {
        if (!File.Exists(Paths.RecordingsJson)) return;
        List<Recording> legacy;
        try
        {
            legacy = TimbratuneJson.ReadRecordings(File.ReadAllText(Paths.RecordingsJson, Encoding.UTF8));
        }
        catch (Exception)
        {
            return; // unreadable: leave it alone
        }

        var used = ScanUnlocked().Select(r => r.Id).ToHashSet();
        foreach (var r in legacy.OrderBy(r => r.Id))
        {
            var audio = Paths.Resolve(r.Audio);
            var detailPath = Paths.Resolve(r.Detail ?? $"analysis/{r.Id}.json");
            var seriesPath = Path.Combine(Paths.AnalysisDir, $"{r.Id}.series.json");
            var detail = detailPath is not null && File.Exists(detailPath)
                ? TryRead(() => TimbratuneJson.ReadDetail(File.ReadAllText(detailPath, Encoding.UTF8)))
                : null;
            TakeSeries? series = null;
            if (File.Exists(seriesPath))
                series = TryRead(() =>
                {
                    using var s = File.OpenRead(seriesPath);
                    return TimbratuneJson.ReadSeries(s);
                });

            var oldId = r.Id;
            if (r.Id <= 0 || used.Contains(r.Id)) r.Id = used.DefaultIfEmpty(0).Max() + 1;
            used.Add(r.Id);
            var folder = NewFolderUnlocked(r.Id, r.Label);
            if (audio is not null && File.Exists(audio)) File.Move(audio, Path.Combine(folder, AudioName));
            WriteTakeUnlocked(folder, r, detail ?? new RecordingDetail(), series);
            if (detailPath is not null) TryDeleteFile(detailPath);
            TryDeleteFile(seriesPath);
            TryDeleteFile(Path.Combine(Paths.AnalysisDir, $"{oldId}-insight.json"));
        }

        File.Move(Paths.RecordingsJson, Paths.RecordingsJson + ".migrated", overwrite: true);
        TryDeleteEmpty(Paths.AudioDir);
        TryDeleteEmpty(Paths.AnalysisDir);
    }

    private static T? TryRead<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void TryDeleteEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (IOException)
        {
        }
    }
}
