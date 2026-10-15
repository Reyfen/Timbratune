using System.Text;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// The takes: one take per folder under <see cref="DataPaths.TakesDir"/> (see
/// <see cref="DataPaths"/> for the files), found by listing them; there is no index.
/// The repository over <see cref="ITakeStorage"/>, which does the actual reading and writing.
/// The older layout (recordings.json + audio/ + analysis/) is converted on first use.
/// All writes are serialized through one lock; files are written then renamed.
/// </summary>
public sealed class RecordingStore
{
    public const string AudioName = "take.wav", TakeJson = "take.json", DetailJson = "detail.json", SeriesJson = "series.json";

    /// <summary>The folder of the data folder that holds the takes; <see cref="Recording.Audio"/> paths start with it.</summary>
    public const string TakesFolder = "takes";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();
    private readonly ITakeStorage _storage;

    /// <summary>Takes in folders under <paramref name="paths"/> (the native heads).</summary>
    public RecordingStore(DataPaths paths) : this(new FileSystemTakeStorage(paths.TakesDir), paths)
    {
    }

    /// <param name="paths">The data folder: the old layout is looked for there, and local paths resolve against it.</param>
    public RecordingStore(ITakeStorage storage, DataPaths paths)
    {
        _storage = storage;
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
        if (Locate(recording.Detail) is not var (take, file) || !_storage.Exists(take, file)) return null;
        try
        {
            return TimbratuneJson.ReadDetail(ReadText(take, file));
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
            var take = NewTakeUnlocked(entry.Id, entry.Label);
            try
            {
                _storage.CopyIn(take, AudioName, audioSourcePath);
                WriteTakeUnlocked(take, entry, detail, series);
            }
            catch
            {
                TryDeleteTake(take);
                throw;
            }
            SetPaths(entry, take);
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
            var take = NewTakeUnlocked(entry.Id, entry.Label);
            try
            {
                _storage.MoveIn(take, AudioName, extractedAudio);
                WriteTakeUnlocked(take, entry, export.Detail ?? new RecordingDetail(), export.Series);
            }
            catch
            {
                TryDeleteTake(take);
                throw;
            }
            SetPaths(entry, take);
            return entry;
        }
    }

    /// <summary>Moves a local file into a take's folder as <paramref name="fileName"/> (e.g. the original of an MP3 import).</summary>
    public void MoveIntoTake(Recording recording, string fileName, string sourcePath)
    {
        lock (_gate) _storage.MoveIn(TakeOf(recording), fileName, sourcePath);
    }

    /// <summary>Rewrites a take's detail.json (e.g. after re-analyzing its audio).</summary>
    public void SaveDetail(Recording recording, RecordingDetail detail)
    {
        lock (_gate)
        {
            var take = TakeOf(recording);
            WriteText(take, DetailJson, TimbratuneJson.WriteDetail(detail));
            // take.json carries the analysis settings that come from the detail (e.g. the trend step).
            WriteTakeJsonUnlocked(take, recording, detail);
        }
    }

    /// <summary>Whether the take's per-frame lists are saved (takes analyzed before they were kept have none).</summary>
    public bool HasSeries(Recording recording) => SeriesTakeOf(recording) is { } take && _storage.Exists(take, SeriesJson);

    /// <summary>
    /// The take's per-frame lists, read from its series.json; null if absent or unreadable.
    /// They can be large: callers use them and let them go (nothing caches them).
    /// </summary>
    public TakeSeries? LoadSeries(Recording recording)
    {
        if (SeriesTakeOf(recording) is not { } take || !_storage.Exists(take, SeriesJson)) return null;
        try
        {
            using var stream = _storage.OpenRead(take, SeriesJson);
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
        lock (_gate) WriteSeries(TakeOf(recording), series);
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
            var take = TakeOf(recording);
            TakeExport export;
            using (var stream = _storage.OpenRead(take, TakeJson))
                export = TimbratuneJson.ReadExport(stream) ?? throw new InvalidDataException($"Can't read {take}/{TakeJson}.");
            export.Take.Label = label;
            export.Metrics.Label = label;
            WriteExportJson(take, export);

            // e.g. its audio is open in a player: then the name inside take.json is what counts
            var renamed = $"{export.Take.Id:000} {SafeName(label)}".TrimEnd();
            if (renameFolder && _storage.TryRenameTake(take, renamed)) take = renamed;
            return ToRecording(take, export);
        }
    }

    /// <summary>Deletes one take's folder. Unknown id → no-op.</summary>
    public void Delete(int id)
    {
        lock (_gate)
        {
            var entry = ScanUnlocked().FirstOrDefault(r => r.Id == id);
            if (entry is not null) TryDeleteTake(TakeOf(entry));
        }
    }

    /// <summary>The folder holding <paramref name="recording"/> (from its paths), on storage with a file system.</summary>
    public string FolderOf(Recording recording) =>
        Path.GetDirectoryName(_storage.LocalPath(TakeOf(recording), TakeJson)
                              ?? throw new InvalidOperationException("This storage has no folders."))!;

    /// <summary>The take's name in the storage (its folder's name), from its paths.</summary>
    private static string TakeOf(Recording recording) =>
        (Locate(recording.Detail) ?? Locate(recording.Audio))?.Take
        ?? throw new InvalidOperationException($"Take #{recording.Id} has no folder.");

    private static string? SeriesTakeOf(Recording recording) => Locate(recording.Detail)?.Take;

    /// <summary>"takes/&lt;take&gt;/&lt;file&gt;" → (take, file); null for anything else (also anything leaving the takes folder).</summary>
    private static (string Take, string File)? Locate(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length != 3 || parts[0] != TakesFolder) return null;
        if (parts[1] is "" or "." or ".." || parts[2] is "" or "." or "..") return null;
        return (parts[1], parts[2]);
    }

    // ---- reading ----

    private List<Recording> ScanUnlocked()
    {
        var takes = new List<Recording>();
        var unreadable = new List<string>();
        var seen = new HashSet<int>();
        var renumber = new List<(string Take, TakeExport Export)>();
        // Name order: the zero-padded id prefix keeps the original of a copied duplicate first.
        foreach (var take in _storage.ListTakes().Order(StringComparer.Ordinal))
        {
            if (!_storage.Exists(take, TakeJson)) continue; // not a take (yet)
            TakeExport? export;
            try
            {
                using var stream = _storage.OpenRead(take, TakeJson);
                export = TimbratuneJson.ReadExport(stream);
            }
            catch (Exception)
            {
                export = null;
            }
            if (export is null)
            {
                unreadable.Add(take);
                continue;
            }
            if (export.Take.Id <= 0 || !seen.Add(export.Take.Id))
            {
                renumber.Add((take, export)); // e.g. a take folder copied in from another device
                continue;
            }
            takes.Add(ToRecording(take, export));
        }
        foreach (var (take, export) in renumber)
        {
            export.Take.Id = Math.Max(seen.DefaultIfEmpty(0).Max(), takes.Select(t => t.Id).DefaultIfEmpty(0).Max()) + 1;
            seen.Add(export.Take.Id);
            try
            {
                WriteExportJson(take, export);
            }
            catch (IOException)
            {
                // read-only: it still shows, under the new id, until the next start
            }
            takes.Add(ToRecording(take, export));
        }
        Unreadable = unreadable;
        takes.Sort((a, b) => a.Id.CompareTo(b.Id));
        return takes;
    }

    private Recording ToRecording(string take, TakeExport export)
    {
        var r = export.Metrics;
        r.Id = export.Take.Id;
        r.Label = export.Take.Label ?? "";
        r.Note = export.Take.Note ?? "";
        r.Date = export.Take.Date ?? "";
        r.RecordedAt = export.Take.RecordedAt ?? AudioTime(take, r.Date);
        r.DurationS = export.Take.DurationS ?? r.DurationS;
        r.SourceFile = export.Take.SourceFile ?? "";
        SetPaths(r, take);
        return r;
    }

    /// <summary>
    /// For takes saved before the time was kept: the audio file's time, when it's on the take's
    /// date (the WAV is written as the take is recorded, and moving it keeps its time).
    /// </summary>
    private string? AudioTime(string take, string date) =>
        _storage.LastWriteTime(take, AudioName) is { } written && written.ToString("yyyy-MM-dd") == date
            ? Recording.Timestamp(written)
            : null;

    private void SetPaths(Recording r, string take)
    {
        r.Audio = _storage.Exists(take, AudioName) ? $"{TakesFolder}/{take}/{AudioName}" : null;
        r.Detail = $"{TakesFolder}/{take}/{DetailJson}";
    }

    private int NextIdUnlocked() => ScanUnlocked().Select(r => r.Id).DefaultIfEmpty(0).Max() + 1;

    private string ReadText(string take, string file)
    {
        using var reader = new StreamReader(_storage.OpenRead(take, file), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // ---- writing ----

    /// <summary>A new, empty take "NNN label" (made unique if needed).</summary>
    private string NewTakeUnlocked(int id, string? label) => _storage.CreateTake($"{id:000} {SafeName(label)}".TrimEnd());

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
    private void WriteTakeUnlocked(string take, Recording entry, RecordingDetail detail, TakeSeries? series)
    {
        WriteText(take, DetailJson, TimbratuneJson.WriteDetail(detail));
        if (series is not null) WriteSeries(take, series);
        WriteTakeJsonUnlocked(take, entry, detail);
    }

    private void WriteTakeJsonUnlocked(string take, Recording entry, RecordingDetail detail)
    {
        Func<Stream>? audio = _storage.Exists(take, AudioName) ? () => _storage.OpenRead(take, AudioName) : null;
        WriteExportJson(take, TakeArchive.Describe(entry, detail, audio, keepSource: true));
    }

    private void WriteExportJson(string take, TakeExport export) =>
        _storage.Write(take, TakeJson, stream => TimbratuneJson.WriteExport(stream, export));

    // Streamed to the file (the lists can be a few MB as text) and written then renamed.
    private void WriteSeries(string take, TakeSeries series) =>
        _storage.Write(take, SeriesJson, stream => TimbratuneJson.WriteSeries(stream, series));

    private void WriteText(string take, string file, string text) =>
        _storage.Write(take, file, stream => stream.Write(Utf8NoBom.GetBytes(text)));

    private void TryDeleteTake(string take)
    {
        try
        {
            _storage.DeleteTake(take);
        }
        catch (IOException)
        {
            // locked by a player: take.json goes at least, so it no longer shows
            try { _storage.DeleteFile(take, TakeJson); } catch (IOException) { }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ---- the layout before 0.2.0 (only ever in a data folder on disk) ----

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
            var take = NewTakeUnlocked(r.Id, r.Label);
            if (audio is not null && File.Exists(audio)) _storage.MoveIn(take, AudioName, audio);
            WriteTakeUnlocked(take, r, detail ?? new RecordingDetail(), series);
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
