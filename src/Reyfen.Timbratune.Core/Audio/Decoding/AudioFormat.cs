namespace Reyfen.Timbratune.Core.Audio.Decoding;

/// <summary>
/// A kind of audio file, as the importer and the file pickers know it. Formats are found by
/// their content (<see cref="FormatSniffer"/>), never by the extension; the extensions, MIME
/// types and Apple type identifiers are only for pickers and for naming a kept original.
/// </summary>
public sealed record AudioFormat(string Id, string DisplayName, string[] Extensions, string[] MimeTypes, string[] AppleUtis)
{
    /// <summary>The usual extension, without the dot.</summary>
    public string Extension => Extensions[0];

    public override string ToString() => Id;
}

/// <summary>The one list of formats the app knows. Ids are stable: settings and decoders refer to them.</summary>
public static class AudioFormatCatalog
{
    public const string Wav = "wav", Aiff = "aiff", Mp3 = "mp3", Flac = "flac", Ogg = "ogg", Opus = "opus", Aac = "aac",
        M4a = "m4a", Mp4 = "mp4", ThreeGp = "3gp", Amr = "amr", Caf = "caf", WebM = "webm", Mkv = "mkv", Wma = "wma";

    public static IReadOnlyList<AudioFormat> All { get; } =
    [
        new(Wav, "WAV", ["wav", "wave"], ["audio/wav", "audio/x-wav", "audio/wave", "audio/vnd.wave"], ["com.microsoft.waveform-audio"]),
        new(Aiff, "AIFF", ["aiff", "aif", "aifc"], ["audio/aiff", "audio/x-aiff"], ["public.aiff-audio", "public.aifc-audio"]),
        new(Mp3, "MP3", ["mp3"], ["audio/mpeg", "audio/mp3"], ["public.mp3"]),
        new(Flac, "FLAC", ["flac"], ["audio/flac", "audio/x-flac"], ["org.xiph.flac"]),
        new(Ogg, "Ogg Vorbis", ["ogg", "oga"], ["audio/ogg", "audio/vorbis", "application/ogg"], []),
        new(Opus, "Opus", ["opus"], ["audio/opus", "audio/ogg"], []),
        new(Aac, "AAC", ["aac", "adts"], ["audio/aac", "audio/aacp", "audio/x-aac"], ["public.aac-audio"]),
        new(M4a, "M4A (AAC, ALAC)", ["m4a", "m4b"], ["audio/mp4", "audio/x-m4a", "audio/m4a"], ["com.apple.m4a-audio", "public.mpeg-4-audio"]),
        new(Mp4, "MP4 / MOV video", ["mp4", "m4v", "mov"], ["video/mp4", "video/quicktime"], ["public.mpeg-4", "com.apple.quicktime-movie"]),
        new(ThreeGp, "3GP", ["3gp", "3gpp", "3g2"], ["audio/3gpp", "video/3gpp", "audio/3gpp2", "video/3gpp2"], ["public.3gpp", "public.3gpp2"]),
        new(Amr, "AMR", ["amr", "awb"], ["audio/amr", "audio/amr-wb"], ["org.3gpp.adaptive-multi-rate-audio"]),
        new(Caf, "CAF", ["caf"], ["audio/x-caf"], ["com.apple.coreaudio-format"]),
        new(WebM, "WebM", ["webm", "weba"], ["audio/webm", "video/webm"], []),
        new(Mkv, "Matroska", ["mkv", "mka"], ["audio/x-matroska", "video/x-matroska"], []),
        new(Wma, "WMA / WMV", ["wma", "wmv", "asf"], ["audio/x-ms-wma", "video/x-ms-wmv", "video/x-ms-asf"], ["com.microsoft.windows-media-wma"]),
    ];

    private static readonly Dictionary<string, AudioFormat> ById = All.ToDictionary(f => f.Id);

    public static AudioFormat Get(string id) => ById[id];

    public static AudioFormat? Find(string id) => ById.GetValueOrDefault(id);

    /// <summary>The formats a file name's extension suggests (for pickers and listings only; decoding sniffs).</summary>
    public static AudioFormat? ByExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext.Length == 0 ? null : All.FirstOrDefault(f => f.Extensions.Contains(ext));
    }
}

/// <summary>A set of <see cref="AudioFormat"/> ids, e.g. what a decoder reads on this device.</summary>
public sealed class FormatSet : IReadOnlyCollection<string>
{
    private readonly HashSet<string> _ids;

    public FormatSet(IEnumerable<string> ids) => _ids = new HashSet<string>(ids, StringComparer.Ordinal);

    public static FormatSet Empty { get; } = new([]);

    public int Count => _ids.Count;

    public bool Contains(string id) => _ids.Contains(id);

    public FormatSet Union(FormatSet other) => new(_ids.Concat(other._ids));

    /// <summary>The formats in catalog order.</summary>
    public IReadOnlyList<AudioFormat> Formats => AudioFormatCatalog.All.Where(f => _ids.Contains(f.Id)).ToList();

    public IEnumerator<string> GetEnumerator() => _ids.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => string.Join(", ", Formats.Select(f => f.Id));
}
