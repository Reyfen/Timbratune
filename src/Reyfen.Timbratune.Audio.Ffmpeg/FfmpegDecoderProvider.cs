using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Platform;

namespace Reyfen.Timbratune.Audio.Ffmpeg;

/// <summary>
/// An ffmpeg program decodes: the last resort, after the managed, miniaudio and OS decoders, and
/// the one that reads nearly everything. The probe asks that ffmpeg which demuxers and decoders
/// it has (<c>-demuxers</c>, <c>-decoders</c>); a format counts when both its container and one
/// of its usual codecs are there.
/// </summary>
/// <param name="installed">Where a copy downloaded for Timbratune may be (checked before the PATH).</param>
public sealed class FfmpegDecoderProvider(IProcessRunner runner, Func<IEnumerable<string>>? installed = null) : IAudioDecoderProvider
{
    public const string ProviderId = "ffmpeg";

    /// <summary>Format → the demuxer it needs, and the decoders of which one must be there.</summary>
    private static readonly (string Format, string Demuxer, string[] Decoders)[] Needs =
    [
        (AudioFormatCatalog.Wav, "wav", ["pcm_s16le", "pcm_f32le", "adpcm_ms", "pcm_mulaw"]),
        (AudioFormatCatalog.Aiff, "aiff", ["pcm_s16be", "pcm_s24be"]),
        (AudioFormatCatalog.Mp3, "mp3", ["mp3", "mp3float"]),
        (AudioFormatCatalog.Flac, "flac", ["flac"]),
        (AudioFormatCatalog.Ogg, "ogg", ["vorbis", "libvorbis"]),
        (AudioFormatCatalog.Opus, "ogg", ["opus", "libopus"]),
        (AudioFormatCatalog.Aac, "aac", ["aac", "aac_fixed"]),
        (AudioFormatCatalog.M4a, "mov", ["aac", "alac"]),
        (AudioFormatCatalog.Mp4, "mov", ["aac"]),
        (AudioFormatCatalog.ThreeGp, "mov", ["aac", "amrnb", "libopencore_amrnb"]),
        (AudioFormatCatalog.Amr, "amr", ["amrnb", "amrwb", "libopencore_amrnb", "libopencore_amrwb"]),
        (AudioFormatCatalog.Caf, "caf", ["alac", "aac", "pcm_s16be"]),
        (AudioFormatCatalog.WebM, "webm", ["opus", "libopus", "vorbis", "libvorbis"]),
        (AudioFormatCatalog.Mkv, "matroska", ["aac", "opus", "libopus", "vorbis", "flac", "ac3"]),
        (AudioFormatCatalog.Wma, "asf", ["wmav1", "wmav2", "wmapro"]),
    ];

    public string Id => ProviderId;

    public int Priority => 6;

    /// <summary>The ffmpeg that would be used now: a downloaded copy, else the one on the PATH; null if there is none.</summary>
    public string? Locate() =>
        installed?.Invoke().FirstOrDefault(File.Exists) ?? runner.FindOnPath("ffmpeg");

    public async ValueTask<FormatSet> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (Locate() is not { } ffmpeg) return FormatSet.Empty;
        var demuxers = await ListAsync(ffmpeg, "-demuxers", cancellationToken);
        var decoders = await ListAsync(ffmpeg, "-decoders", cancellationToken);
        return new FormatSet(Needs.Where(n => demuxers.Contains(n.Demuxer) && n.Decoders.Any(decoders.Contains)).Select(n => n.Format));
    }

    public IAudioDecoder Create() => new FfmpegDecoder(runner, Locate() ?? throw new FileNotFoundException("ffmpeg isn't there any more."));

    /// <summary>The names in <c>ffmpeg -demuxers</c> / <c>-decoders</c> (after the "--" line: flags, then names, then a description).</summary>
    private async Task<HashSet<string>> ListAsync(string ffmpeg, string what, CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(ffmpeg, ["-hide_banner", what], cancellationToken);
        return Parse(result.Output);
    }

    internal static HashSet<string> Parse(string listing)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var started = false;
        foreach (var line in listing.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!started)
            {
                started = trimmed.StartsWith("--", StringComparison.Ordinal);
                continue;
            }
            var parts = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                foreach (var name in parts[1].Split(',')) names.Add(name);
        }
        return names;
    }
}

/// <summary>
/// Runs <c>ffmpeg -i file -map 0:a:0 -c:a pcm_f32le -f wav -</c> and streams the WAV it prints:
/// the first audio track, at its own rate and channels, as float.
/// </summary>
internal sealed class FfmpegDecoder(IProcessRunner runner, string ffmpeg) : IAudioDecoder
{
    public async Task DecodeAsync(AudioSource source, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var path = source.Path;
        string? copy = null;
        if (path is null)
        {
            copy = Path.Combine(Path.GetTempPath(), $"timbratune-ffmpeg-{Guid.NewGuid():N}");
            await using (var input = source.Open())
            await using (var output = File.Create(copy))
                await input.CopyToAsync(output, cancellationToken);
            path = copy;
        }
        try
        {
            long frames = 0;
            var result = await runner.RunAsync(ffmpeg,
                ["-nostdin", "-hide_banner", "-loglevel", "error", "-i", path, "-map", "0:a:0", "-vn", "-sn", "-dn",
                 "-c:a", "pcm_f32le", "-f", "wav", "-"],
                async (stdout, ct) => frames = await ReadWavAsync(stdout, sink, ct), cancellationToken);
            if (result.ExitCode != 0)
            {
                var message = result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                throw new InvalidDataException($"ffmpeg: {message ?? "exit code " + result.ExitCode}");
            }
            if (frames == 0) throw new InvalidDataException($"No audio could be decoded from {source.Name}.");
        }
        finally
        {
            if (copy is not null) try { File.Delete(copy); } catch (IOException) { }
        }
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Reads a float WAV from a pipe: the header's sizes are placeholders there, so the data
    /// chunk runs to the end of the stream. Returns the frames passed on.
    /// </summary>
    internal static async Task<long> ReadWavAsync(Stream stream, IPcmSink sink, CancellationToken cancellationToken)
    {
        var header = new byte[12];
        if (!await ReadExactlyOrEndAsync(stream, header, cancellationToken)) return 0;
        if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw new InvalidDataException("ffmpeg didn't write a WAV.");
        int channels = 0, rate = 0;
        var chunk = new byte[8];
        while (await ReadExactlyOrEndAsync(stream, chunk, cancellationToken))
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk.AsSpan(4));
            if (chunk.AsSpan(0, 4).SequenceEqual("fmt "u8))
            {
                var fmt = new byte[size + (size & 1)];
                await stream.ReadExactlyAsync(fmt, cancellationToken);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(4));
                if (BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14)) != 32) throw new InvalidDataException("ffmpeg didn't write float samples.");
            }
            else if (chunk.AsSpan(0, 4).SequenceEqual("data"u8))
            {
                if (channels == 0 || rate == 0) throw new InvalidDataException("WAV data before its format.");
                sink.SetFormat(rate, channels);
                return await ReadSamplesAsync(stream, sink, channels, cancellationToken);
            }
            else
            {
                var skip = new byte[size + (size & 1)];
                await stream.ReadExactlyAsync(skip, cancellationToken);
            }
        }
        return 0;
    }

    private static async Task<long> ReadSamplesAsync(Stream stream, IPcmSink sink, int channels, CancellationToken cancellationToken)
    {
        var frameBytes = 4 * channels;
        var buffer = new byte[64 * 1024 / frameBytes * frameBytes + frameBytes];
        var filled = 0;
        long frames = 0;
        while (true)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken);
            if (n == 0) return frames;
            filled += n;
            var whole = filled / frameBytes * frameBytes;
            if (whole == 0) continue;
            sink.Write(MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, whole)));
            frames += whole / frameBytes;
            // Keep the partial frame for the next read.
            buffer.AsSpan(whole, filled - whole).CopyTo(buffer);
            filled -= whole;
        }
    }

    private static async Task<bool> ReadExactlyOrEndAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        if (read == 0) return false;
        if (read < buffer.Length) throw new EndOfStreamException();
        return true;
    }
}
