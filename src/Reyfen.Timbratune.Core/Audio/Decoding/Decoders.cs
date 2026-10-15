namespace Reyfen.Timbratune.Core.Audio.Decoding;

/// <summary>
/// An audio file to decode: a path where there is one, or only a stream (Android's picker hands
/// out content without a path). <see cref="Name"/> is the file's name as the user knows it.
/// </summary>
public sealed class AudioSource(string name, Func<Stream> open, string? path = null, string? mimeType = null)
{
    public static AudioSource FromFile(string path, string? name = null) =>
        new(name ?? System.IO.Path.GetFileName(path), () => File.OpenRead(path), path);

    public string Name { get; } = name;

    /// <summary>A local file decoders may open themselves (native decoders, a separate process), or null.</summary>
    public string? Path { get; } = path;

    public string? MimeType { get; } = mimeType;

    /// <summary>What the content is, once sniffed: the registry fills it in before a decoder sees the file.</summary>
    public AudioFormat? Format { get; private init; }

    public AudioSource WithFormat(AudioFormat format) => new(Name, open, Path, MimeType) { Format = format };

    /// <summary>A new stream over the file, from its start; the caller disposes it.</summary>
    public Stream Open() => open();
}

/// <summary>Receives decoded audio: the format once, then the samples.</summary>
public interface IPcmSink
{
    /// <summary>Called once, before the first <see cref="Write"/>: the decoded audio's own rate and channel count.</summary>
    void SetFormat(int sampleRate, int channels);

    /// <summary>Interleaved float samples (−1..1), whole frames.</summary>
    void Write(ReadOnlySpan<float> interleaved);
}

/// <summary>Decodes one file. Made by a provider for one import and disposed after it.</summary>
public interface IAudioDecoder : IDisposable
{
    /// <summary>
    /// Decodes the first audio track of <paramref name="source"/> into <paramref name="sink"/>, at the
    /// file's own rate and channels; throws if it can't (the registry then tries the next decoder).
    /// </summary>
    Task DecodeAsync(AudioSource source, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>
/// One way of decoding audio on this device (the managed WAV/AIFF reader, miniaudio, the OS's
/// decoders, FFmpeg…). Registered by the heads; cheap to hold: nothing heavy is made before
/// <see cref="Create"/>, which is only called during an import.
/// </summary>
public interface IAudioDecoderProvider
{
    /// <summary>A stable name (logs, settings).</summary>
    string Id { get; }

    /// <summary>Lower goes first among the providers that read a format.</summary>
    int Priority { get; }

    /// <summary>The formats this provider decodes on this device now (it may look at what is installed).</summary>
    ValueTask<FormatSet> ProbeAsync(CancellationToken cancellationToken = default);

    IAudioDecoder Create();
}

/// <summary>A provider that can fetch what it needs (e.g. FFmpeg on Linux), only when the user asks.</summary>
public interface IDecoderInstaller
{
    /// <summary>The provider it installs for.</summary>
    string ProviderId { get; }

    /// <summary>Whether installing makes sense here: a build for this OS and CPU, and the decoder isn't there yet.</summary>
    bool CanInstall { get; }

    /// <summary>
    /// Whether the app offers it unasked (FFmpeg: on Linux, the one platform without a general
    /// system decoder). Elsewhere it is there for a setting to offer.
    /// </summary>
    bool IsOffered { get; }

    /// <summary>A copy downloaded by this installer is there.</summary>
    bool IsInstalled { get; }

    /// <summary>What would be fetched, for the prompt: name, version, size, licence and source.</summary>
    DecoderDownload Describe();

    /// <summary>The command that installs it with the system's package manager (to show, never run), or null.</summary>
    string? PackageManagerCommand { get; }

    /// <summary>Downloads, verifies and unpacks it; the caller then refreshes the <see cref="DecoderRegistry"/>.</summary>
    Task InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Deletes what <see cref="InstallAsync"/> put there.</summary>
    void Remove();
}

/// <summary>A decoder download as the prompt describes it.</summary>
public sealed record DecoderDownload(string Name, string Version, long Bytes, string Licence, Uri LicenceUri, Uri SourceUri, Uri From);

/// <summary>A file no decoder on this device reads: an unknown format, or one without a decoder here.</summary>
public sealed class UnsupportedAudioException(string fileName, AudioFormat? format)
    : NotSupportedException(format is null
        ? "not an audio format Timbratune knows"
        : $"{format.DisplayName} files can't be decoded on this device")
{
    public string FileName { get; } = fileName;

    /// <summary>What the file is, when it was recognised; null for an unknown format.</summary>
    public AudioFormat? Format { get; } = format;
}
