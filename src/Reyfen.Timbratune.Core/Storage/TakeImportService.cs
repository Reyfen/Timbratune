using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// Brings files in as takes: audio is decoded (<see cref="DecoderRegistry"/>) and analyzed; a
/// .tmbr file is unpacked as it is, without analysis. Used by the Import feature (picked files,
/// files dropped into the takes folder) and the desktop's <c>--import</c>.
/// <list type="bullet">
/// <item>A WAV the analysis reads directly is stored as it is.</item>
/// <item>Anything else becomes a 44.1 kHz mono WAV, and the original is kept beside it as
/// <c>source.&lt;ext&gt;</c>.</item>
/// </list>
/// </summary>
public sealed class TakeImportService(RecordingStore store, IAnalysisEngine engine, DecoderRegistry decoders)
{
    /// <summary>The original of a decoded import, in its take's folder: <c>source.m4a</c> and so on.</summary>
    public const string SourceName = "source";

    public DecoderRegistry Decoders => decoders;

    public static bool IsArchive(string fileName) =>
        Path.GetExtension(fileName).Equals("." + TakeArchive.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Extensions (without the dot) of every format this device decodes, then tmbr.</summary>
    public async Task<IReadOnlyList<string>> GetExtensionsAsync()
    {
        var formats = await decoders.GetFormatsAsync().ConfigureAwait(false);
        return [.. formats.Formats.SelectMany(f => f.Extensions), TakeArchive.Extension];
    }

    /// <summary>Whether a file of this name looks importable here (by its extension: for listings; importing sniffs the content).</summary>
    public async Task<bool> CanImportAsync(string fileName) =>
        IsArchive(fileName) || AudioFormatCatalog.ByExtension(fileName) is { } format
            && (await decoders.GetFormatsAsync().ConfigureAwait(false)).Contains(format.Id);

    /// <summary>Files in the takes folder itself (not in a take's folder) that look importable.</summary>
    public async Task<IReadOnlyList<string>> FindDroppedAsync()
    {
        if (!Directory.Exists(store.Paths.TakesDir)) return [];
        var found = new List<string>();
        foreach (var file in Directory.GetFiles(store.Paths.TakesDir).Order(StringComparer.Ordinal))
            if (await CanImportAsync(file).ConfigureAwait(false)) found.Add(file);
        return found;
    }

    /// <summary>
    /// Imports one file and returns the new take. <paramref name="name"/> is the file's name
    /// when <paramref name="path"/> is a temporary copy (e.g. from a file picker).
    /// </summary>
    /// <param name="progress">Analysis progress (0–1); not reported for .tmbr files.</param>
    /// <param name="label">The take's label for audio files (default: the file name).</param>
    /// <param name="moveOriginal">Move a decoded file's original into the take instead of copying it.</param>
    public async Task<Recording> ImportAsync(string path, string? name = null, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default, string? label = null, bool moveOriginal = false) =>
        (await ImportCoreAsync(path, name, progress, cancellationToken, label, moveOriginal)).Take;

    private async Task<(Recording Take, bool Decoded)> ImportCoreAsync(string path, string? name, IProgress<double>? progress,
        CancellationToken cancellationToken, string? label, bool moveOriginal)
    {
        name ??= Path.GetFileName(path);
        var work = WorkFolder();
        try
        {
            if (IsArchive(name))
            {
                var audio = Path.Combine(work, RecordingStore.AudioName);
                return (await Task.Run(() =>
                {
                    TakeExport export;
                    using (var source = File.OpenRead(path)) export = TakeArchive.Extract(source, audio);
                    return store.AddImported(export, audio);
                }, cancellationToken), false);
            }

            var wav = await Task.Run(() => IsReadableWav(path), cancellationToken) ? path : null;
            DecodedAudio? decoded = null;
            if (wav is null)
            {
                wav = Path.Combine(work, RecordingStore.AudioName);
                await using var target = new FileStream(wav, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
                decoded = await decoders.DecodeAsync(AudioSource.FromFile(path, name), target, cancellationToken: cancellationToken);
            }
            var result = await engine.AnalyzeAsync(wav, cancellationToken: cancellationToken, progress: progress);
            var entry = result.Metrics;
            entry.Label = label ?? Path.GetFileNameWithoutExtension(name);
            entry.Note = "";
            entry.SourceFile = name;
            var when = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;
            entry.Date = when.ToString("yyyy-MM-dd");
            entry.RecordedAt = Recording.Timestamp(when);
            return (await Task.Run(() =>
            {
                var take = store.Add(entry, result.Detail, wav, result.Series);
                if (decoded is not null) KeepOriginal(take, path, name, decoded.Format, moveOriginal);
                return take;
            }, cancellationToken), decoded is not null);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Imports a file dropped into the takes folder, then removes it from there: a .tmbr or
    /// WAV is fully inside the new take's folder; any other original is moved into it.
    /// </summary>
    public async Task<Recording> ImportDroppedAsync(string path, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var (take, decoded) = await ImportCoreAsync(path, null, progress, cancellationToken, null, moveOriginal: true);
        if (decoded) return take; // the original was moved into the take (or, if it was locked, is still there)
        try
        {
            File.Delete(path);
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

    private void KeepOriginal(Recording take, string path, string name, AudioFormat format, bool move)
    {
        var ext = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        var file = SourceName + "." + (format.Extensions.Contains(ext) ? ext : format.Extension);
        try
        {
            if (move) store.MoveIntoTake(take, file, path);
            else store.CopyIntoTake(take, file, path);
        }
        catch (IOException)
        {
            // the take is complete without it (rare: a locked file)
        }
    }

    private string WorkRoot => Path.Combine(store.Paths.Root, ".import");

    private string WorkFolder()
    {
        var dir = Path.Combine(WorkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A plain RIFF WAVE the analysis reads as it is (PCM or float), kept unconverted.</summary>
    private static bool IsReadableWav(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> riff = stackalloc byte[4];
            if (stream.ReadAtLeast(riff, 4, throwOnEndOfStream: false) < 4 || !riff.SequenceEqual("RIFF"u8)) return false;
            stream.Position = 0;
            WavDecoder.Decode(stream);
            return true;
        }
        catch (Exception e) when (e is FormatException or EndOfStreamException or NotSupportedException)
        {
            return false;
        }
    }
}
