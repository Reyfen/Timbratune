using System.Text;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// Reads and writes the recordings index plus each take's audio and detail
/// files. Port of the storage half of analyze.py main() and
/// electron/src/recordings.ts. All writes are serialized through one lock.
/// </summary>
public sealed class RecordingStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _gate = new();

    public RecordingStore(DataPaths paths)
    {
        Paths = paths;
    }

    public DataPaths Paths { get; }

    /// <summary>All recordings sorted by id. Missing or unreadable index → empty.</summary>
    public List<Recording> Load()
    {
        lock (_gate) return LoadUnlocked();
    }

    public RecordingDetail? LoadDetail(Recording recording)
    {
        var path = Paths.Resolve(recording.Detail ?? $"analysis/{recording.Id}.json");
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
    /// Adds an analyzed take: assigns the next id (max + 1), copies the audio
    /// to audio/NNN.ext, writes analysis/&lt;id&gt;.json (and the per-frame
    /// <paramref name="series"/> to analysis/&lt;id&gt;.series.json) and rewrites
    /// the index. <paramref name="entry"/> must carry the metrics; id, audio and
    /// detail paths are filled in here.
    /// </summary>
    public Recording Add(Recording entry, RecordingDetail detail, string audioSourcePath, TakeSeries? series = null)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Paths.AudioDir);
            Directory.CreateDirectory(Paths.AnalysisDir);
            var recordings = LoadUnlocked();
            var id = recordings.Count == 0 ? 1 : recordings.Max(r => r.Id) + 1;

            var ext = Path.GetExtension(audioSourcePath).ToLowerInvariant();
            var playbackName = $"{id:000}{ext}";
            File.Copy(audioSourcePath, Path.Combine(Paths.AudioDir, playbackName), overwrite: true);
            File.WriteAllText(Path.Combine(Paths.AnalysisDir, $"{id}.json"), TimbratuneJson.WriteDetail(detail), Utf8NoBom);
            if (series is not null) WriteSeries(Paths.SeriesFile(id), series);

            entry.Id = id;
            entry.Audio = $"audio/{playbackName}";
            entry.Detail = $"analysis/{id}.json";
            if (string.IsNullOrEmpty(entry.Date)) entry.Date = DateTime.Now.ToString("yyyy-MM-dd");

            recordings.Add(entry);
            SaveUnlocked(recordings);
            return entry;
        }
    }

    /// <summary>Rewrites a take's analysis/&lt;id&gt;.json (e.g. after re-analyzing its audio).</summary>
    public void SaveDetail(Recording recording, RecordingDetail detail)
    {
        lock (_gate)
        {
            var path = Paths.Resolve(recording.Detail ?? $"analysis/{recording.Id}.json")
                       ?? throw new InvalidOperationException($"Detail path of take #{recording.Id} is outside the data folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, TimbratuneJson.WriteDetail(detail), Utf8NoBom);
            File.Move(tmp, path, overwrite: true);
        }
    }

    /// <summary>Whether the take's per-frame lists are saved (takes analyzed before they were kept have none).</summary>
    public bool HasSeries(Recording recording) => File.Exists(Paths.SeriesFile(recording.Id));

    /// <summary>
    /// The take's per-frame lists, read from analysis/&lt;id&gt;.series.json; null if absent
    /// or unreadable. They can be large: callers use them and let them go (nothing caches them).
    /// </summary>
    public TakeSeries? LoadSeries(Recording recording)
    {
        var path = Paths.SeriesFile(recording.Id);
        if (!File.Exists(path)) return null;
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

    /// <summary>Writes (or replaces) the take's analysis/&lt;id&gt;.series.json.</summary>
    public void SaveSeries(Recording recording, TakeSeries series)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Paths.AnalysisDir);
            WriteSeries(Paths.SeriesFile(recording.Id), series);
        }
    }

    // Streamed to the file (the lists can be a few MB as text) and written then renamed.
    private static void WriteSeries(string path, TakeSeries series)
    {
        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp)) TimbratuneJson.WriteSeries(stream, series);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Deletes one take (audio, detail, series, cached insight) and its index entry. Unknown id → no-op.</summary>
    public void Delete(int id)
    {
        lock (_gate)
        {
            var recordings = LoadUnlocked();
            var entry = recordings.FirstOrDefault(r => r.Id == id);
            if (entry is null) return;

            TryDelete(Paths.Resolve(entry.Audio));
            TryDelete(Paths.Resolve(entry.Detail));
            TryDelete(Paths.SeriesFile(id));
            TryDelete(Path.Combine(Paths.AnalysisDir, $"{id}-insight.json"));

            recordings.Remove(entry);
            SaveUnlocked(recordings);
        }
    }

    private List<Recording> LoadUnlocked()
    {
        if (!File.Exists(Paths.RecordingsJson)) return [];
        try
        {
            var list = TimbratuneJson.ReadRecordings(File.ReadAllText(Paths.RecordingsJson, Encoding.UTF8));
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private void SaveUnlocked(List<Recording> recordings)
    {
        recordings.Sort((a, b) => a.Id.CompareTo(b.Id));
        Directory.CreateDirectory(Paths.Root);
        // Write-then-rename so a crash mid-write never truncates the index.
        var tmp = Paths.RecordingsJson + ".tmp";
        File.WriteAllText(tmp, TimbratuneJson.WriteRecordings(recordings), Utf8NoBom);
        File.Move(tmp, Paths.RecordingsJson, overwrite: true);
    }

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // already gone / locked by a player — the index entry is what matters
        }
    }
}
