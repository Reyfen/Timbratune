using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// Brings files in as takes: audio (WAV, and MP3 / FLAC through <see cref="IAudioDecoder"/>)
/// is analyzed; a .tmbr file is unpacked as it is, without analysis. Used by the Import
/// button and for files dropped into the takes folder (<see cref="FindDropped"/>).
/// </summary>
public sealed class TakeImporter(RecordingStore store, IAnalysisEngine engine, IAudioDecoder? decoder)
{
    public static readonly string[] AudioExtensions = [".wav", ".mp3", ".flac"];

    /// <summary>Extensions the importer takes, e.g. for a file picker: wav, mp3, flac (with a decoder), tmbr.</summary>
    public IReadOnlyList<string> Extensions =>
        [.. AudioExtensions.Where(e => e == ".wav" || decoder is not null).Select(e => e[1..]), TakeArchive.Extension];

    public bool CanImport(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext == "." + TakeArchive.Extension || ext == ".wav" || decoder is not null && AudioExtensions.Contains(ext);
    }

    /// <summary>Files in the takes folder itself (not in a take's folder) that can be imported.</summary>
    public IReadOnlyList<string> FindDropped() =>
        Directory.Exists(store.Paths.TakesDir)
            ? Directory.GetFiles(store.Paths.TakesDir).Where(CanImport).Order(StringComparer.Ordinal).ToList()
            : [];

    /// <summary>
    /// Imports one file and returns the new take. <paramref name="name"/> is the file's name
    /// when <paramref name="path"/> is a temporary copy (e.g. from a file picker).
    /// </summary>
    /// <param name="progress">Analysis progress (0–1); not reported for .tmbr files.</param>
    /// <param name="label">The take's label for audio files (default: the file name).</param>
    public async Task<Recording> ImportAsync(string path, string? name = null, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default, string? label = null)
    {
        name ??= Path.GetFileName(path);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var work = WorkFolder();
        try
        {
            if (ext == "." + TakeArchive.Extension)
            {
                var audio = Path.Combine(work, RecordingStore.AudioName);
                return await Task.Run(() =>
                {
                    TakeExport export;
                    using (var source = File.OpenRead(path)) export = TakeArchive.Extract(source, audio);
                    return store.AddImported(export, audio);
                }, cancellationToken);
            }

            var wav = ext == ".wav" && await Task.Run(() => IsReadableWav(path), cancellationToken) ? path : null;
            if (wav is null)
            {
                if (decoder is null) throw new NotSupportedException($"Can't read {name}.");
                wav = Path.Combine(work, RecordingStore.AudioName);
                await Task.Run(() => decoder.DecodeToWav(path, wav), cancellationToken);
            }
            var result = await engine.AnalyzeAsync(wav, cancellationToken: cancellationToken, progress: progress);
            var entry = result.Metrics;
            entry.Label = label ?? Path.GetFileNameWithoutExtension(name);
            entry.Note = "";
            entry.SourceFile = name;
            var when = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;
            entry.Date = when.ToString("yyyy-MM-dd");
            entry.RecordedAt = Recording.Timestamp(when);
            return await Task.Run(() => store.Add(entry, result.Detail, wav, result.Series), cancellationToken);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Imports a file dropped into the takes folder, then removes it from there: a .tmbr or
    /// WAV is fully inside the new take's folder; an MP3 / FLAC original is moved into it too.
    /// </summary>
    public async Task<Recording> ImportDroppedAsync(string path, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var take = await ImportAsync(path, progress: progress, cancellationToken: cancellationToken);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            if (ext is ".mp3" or ".flac") File.Move(path, Path.Combine(store.FolderOf(take), "source" + ext));
            else File.Delete(path);
        }
        catch (IOException)
        {
            // still there: it would be imported again next time, so say so by leaving it — rare (locked file)
        }
        return take;
    }

    /// <summary>Removes leftovers of imports that were interrupted.</summary>
    public void CleanUp()
    {
        try
        {
            if (Directory.Exists(WorkRoot)) Directory.Delete(WorkRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string WorkRoot => Path.Combine(store.Paths.Root, ".import");

    private string WorkFolder()
    {
        var dir = Path.Combine(WorkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static bool IsReadableWav(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            WavDecoder.Decode(stream);
            return true;
        }
        catch (Exception e) when (e is FormatException or EndOfStreamException or NotSupportedException)
        {
            return false;
        }
    }
}
