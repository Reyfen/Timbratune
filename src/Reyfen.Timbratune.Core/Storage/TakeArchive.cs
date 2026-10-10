using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Core.Storage;

/// <summary>
/// The .tmbr export: a zip with the take's WAV (<c>take.wav</c>, byte for byte) and
/// <c>take.json</c> (<see cref="TakeExport"/>: metadata, metrics, the saved detail and
/// the per-frame lists). Both entries are streamed, so no whole copy is held in memory.
/// </summary>
public static class TakeArchive
{
    public const string Extension = "tmbr";
    public const string AudioEntry = "take.wav";
    public const string DataEntry = "take.json";

    /// <summary>This build's version ("0.2.0"), written into every take.json.</summary>
    public static string AppVersion { get; } = typeof(TakeArchive).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? "";

    /// <summary>
    /// Everything about a take except its detail and series: the take.json of a take folder
    /// (<paramref name="keepSource"/> true) or the start of an export's take.json.
    /// </summary>
    public static TakeExport Describe(Recording recording, RecordingDetail detail, string? audioPath, bool keepSource) => new()
    {
        AppVersion = AppVersion,
        ExportedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        Take = new ExportedTake
        {
            Id = recording.Id,
            Label = recording.Label,
            Note = recording.Note,
            Date = recording.Date,
            RecordedAt = recording.RecordedAt,
            DurationS = recording.DurationS,
            SourceFile = keepSource && !string.IsNullOrEmpty(recording.SourceFile) ? recording.SourceFile : null,
        },
        Audio = audioPath is null ? new ExportedAudio() : ReadFormat(audioPath),
        Analysis = Settings(detail),
        Metrics = Metrics(recording),
    };

    public static void Write(Stream target, Recording recording, RecordingDetail detail, TakeSeries series, string wavPath,
        string? appVersion = null)
    {
        var export = Describe(recording, detail, wavPath, keepSource: false);
        if (appVersion is not null) export.AppVersion = appVersion;
        export.Detail = WithTrends(detail);
        export.Series = series;

        using var zip = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true);
        using (var entry = zip.CreateEntry(AudioEntry, CompressionLevel.Fastest).Open())
        using (var wav = File.OpenRead(wavPath))
            wav.CopyTo(entry);
        using (var entry = zip.CreateEntry(DataEntry, CompressionLevel.Optimal).Open())
            TimbratuneJson.WriteExport(entry, export);
    }

    /// <summary>The take.json of a .tmbr file; throws <see cref="InvalidDataException"/> if it isn't one.</summary>
    public static TakeExport Read(Stream source)
    {
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        return ReadData(zip);
    }

    /// <summary>
    /// Reads a .tmbr file: its take.json, with the audio extracted to <paramref name="audioTarget"/>.
    /// Throws <see cref="InvalidDataException"/> if it isn't a take this version can read.
    /// </summary>
    public static TakeExport Extract(Stream source, string audioTarget)
    {
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        var export = ReadData(zip);
        var audio = zip.GetEntry(string.IsNullOrEmpty(export.Audio.File) ? AudioEntry : export.Audio.File)
                    ?? throw new InvalidDataException("The take's audio is missing from the file.");
        using (var from = audio.Open())
        using (var to = File.Create(audioTarget))
            from.CopyTo(to);
        return export;
    }

    private static TakeExport ReadData(ZipArchive zip)
    {
        var entry = zip.GetEntry(DataEntry) ?? throw new InvalidDataException($"No {DataEntry} in the archive.");
        TakeExport? export;
        using (var json = entry.Open()) export = TimbratuneJson.ReadExport(json);
        if (export is null || export.Format != TakeExport.FormatName)
            throw new InvalidDataException("Not a Timbratune take.");
        if (export.ExporterVersion > TakeExport.CurrentExporterVersion)
            throw new InvalidDataException($"This take was saved by a newer Timbratune ({export.AppVersion}); update to open it.");
        return export;
    }

    private static AnalysisSettings Settings(RecordingDetail detail) => new()
    {
        PitchFloorHz = AcousticsAnalysisEngine.PitchFloor,
        PitchCeilingHz = AcousticsAnalysisEngine.PitchCeiling,
        PitchStepS = AcousticsAnalysisEngine.PitchStep,
        IntensityStepS = AcousticsAnalysisEngine.IntensityStep,
        HnrStepS = AcousticsAnalysisEngine.HnrStep,
        FormantCeilingHz = RawAnalysisAssembler.FormantCeiling,
        FormantCount = AcousticsAnalysisEngine.FormantCount,
        FormantWindowS = AcousticsAnalysisEngine.FormantWindow,
        FormantStepS = AcousticsAnalysisEngine.FormantWindow / 4,
        RegisterFloorHz = detail.RegisterFloorHz,
        SemitoneRefHz = detail.SemitoneRefHz,
        TrendStepS = (detail.Trends ?? AnalysisPostProcessor.TimeTrends(detail)).StepS,
    };

    // The trend charts of older takes are rebuilt from the contour (as the take view does).
    private static RecordingDetail WithTrends(RecordingDetail detail) => detail.Trends is not null ? detail : new RecordingDetail
    {
        RegisterFloorHz = detail.RegisterFloorHz,
        SemitoneRefHz = detail.SemitoneRefHz,
        DurationS = detail.DurationS,
        TimeStep = detail.TimeStep,
        Frames = detail.Frames,
        Phrases = detail.Phrases,
        Summary = detail.Summary,
        PhraseMetrics = detail.PhraseMetrics,
        Trends = AnalysisPostProcessor.TimeTrends(detail),
    };

    // The metrics only: no paths of this computer's data folder or of the imported file.
    private static Recording Metrics(Recording r) => new()
    {
        Id = r.Id,
        Label = r.Label,
        Note = r.Note,
        Date = r.Date,
        DurationS = r.DurationS,
        Pitch = r.Pitch,
        Formants = r.Formants,
        VoiceQuality = r.VoiceQuality,
        Intensity = r.Intensity,
        Weight = r.Weight,
        Register = r.Register,
    };

    /// <summary>Sample rate, channels and bits from the WAV's fmt chunk (zeros if unreadable).</summary>
    private static ExportedAudio ReadFormat(string wavPath)
    {
        var audio = new ExportedAudio { File = AudioEntry };
        try
        {
            using var stream = File.OpenRead(wavPath);
            Span<byte> header = stackalloc byte[12];
            if (stream.Read(header) < 12) return audio;
            Span<byte> chunk = stackalloc byte[8];
            while (stream.Read(chunk) == 8)
            {
                var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
                if (chunk[..4].SequenceEqual("fmt "u8))
                {
                    Span<byte> fmt = stackalloc byte[16];
                    if (stream.Read(fmt) < 16) return audio;
                    audio.Channels = BinaryPrimitives.ReadInt16LittleEndian(fmt[2..]);
                    audio.SampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmt[4..]);
                    audio.BitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(fmt[14..]);
                    return audio;
                }
                stream.Seek(size + (size & 1), SeekOrigin.Current);
            }
        }
        catch (IOException)
        {
        }
        return audio;
    }
}
