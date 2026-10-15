using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Audio.Ffmpeg;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Platform;
using Xunit.Abstractions;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>
/// The FFmpeg provider: its probe and its reading of ffmpeg's output with stand-ins, then the
/// real thing when ffmpeg is on the PATH (passes with a note otherwise).
/// </summary>
public sealed class FfmpegTests(ITestOutputHelper output)
{
    private static string Format(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "formats", name);

    private const string Demuxers = """
        File formats:
         D. = Demuxing supported
         --
         D  aac             raw ADTS AAC (Advanced Audio Coding)
         D  matroska,webm   Matroska / WebM
         D  mov,mp4,m4a,3gp,3g2,mj2 QuickTime / MOV
         D  ogg             Ogg
        """;

    private const string Decoders = """
        Decoders:
         A..... = Audio
         ------
         A....D aac                  AAC (Advanced Audio Coding)
         A....D libopus              libopus Opus (codec opus)
         V....D h264                 H.264
        """;

    [Fact]
    public async Task AFormatNeedsItsContainerAndOneOfItsCodecs()
    {
        var runner = new FakeRunner("/usr/bin/ffmpeg");
        var formats = await new FfmpegDecoderProvider(runner).ProbeAsync();
        // Ogg is there but Vorbis isn't; Opus is. Matroska and WebM with AAC or Opus.
        Assert.Equal("opus, aac, m4a, mp4, 3gp, webm, mkv", formats.ToString());
        Assert.Equal(["-hide_banner -demuxers", "-hide_banner -decoders"], runner.Calls);
    }

    [Fact]
    public async Task WithoutFfmpegNothingIsClaimed()
    {
        Assert.Empty(await new FfmpegDecoderProvider(new FakeRunner(null)).ProbeAsync());
        // A downloaded copy is used before the one on the PATH.
        var copy = Path.GetTempFileName();
        try
        {
            Assert.Equal(copy, new FfmpegDecoderProvider(new FakeRunner("/usr/bin/ffmpeg"), () => ["/nowhere/ffmpeg", copy]).Locate());
            Assert.Equal("/usr/bin/ffmpeg", new FfmpegDecoderProvider(new FakeRunner("/usr/bin/ffmpeg"), () => ["/nowhere/ffmpeg"]).Locate());
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public async Task ThePipedWavIsReadToItsEndInAnyPieces()
    {
        // As ffmpeg writes to a pipe: placeholder sizes, a LIST chunk, and a stream that returns odd-sized pieces.
        var samples = Enumerable.Range(0, 3001 * 2).Select(i => (float)Math.Sin(i * 0.01)).ToArray();
        var wav = new MemoryStream();
        var w = new BinaryWriter(wav);
        w.Write("RIFF"u8); w.Write(uint.MaxValue); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)3); w.Write((short)2); w.Write(22050); w.Write(22050 * 8); w.Write((short)8); w.Write((short)32);
        w.Write("LIST"u8); w.Write(5); w.Write("INFO!"u8); w.Write((byte)0);
        w.Write("data"u8); w.Write(uint.MaxValue);
        foreach (var s in samples) w.Write(s);
        w.Write((byte)7); // a partial frame at the end is dropped
        var sink = new CollectingSink();
        var frames = await FfmpegDecoder.ReadWavAsync(new Trickle(wav.ToArray()), sink, CancellationToken.None);
        Assert.Equal(3001, frames);
        Assert.Equal((22050, 2), (sink.Rate, sink.Channels));
        Assert.Equal(samples, sink.Samples);
    }

    [Fact]
    public async Task TheRealFfmpegDecodesWhatTheOthersDont()
    {
        var provider = new FfmpegDecoderProvider(new SystemProcessRunner());
        if (provider.Locate() is not { } ffmpeg)
        {
            output.WriteLine("ffmpeg isn't on the PATH: skipped.");
            return;
        }
        var formats = await provider.ProbeAsync();
        output.WriteLine($"{ffmpeg}: {formats}");
        var registry = new DecoderRegistry([provider]);
        foreach (var file in new[] { "voice.opus", "voice.ogg", "voice.caf", "voice.mka", "voice.wma", "voice.amr" })
        {
            var wav = new MemoryStream();
            // No path (as from Android's picker): a temporary copy is made.
            var decoded = await registry.DecodeAsync(new AudioSource(file, () => File.OpenRead(Format(file))), wav);
            Assert.Equal(FfmpegDecoderProvider.ProviderId, decoded.ProviderId);
            wav.Position = 0;
            Assert.InRange(WavDecoder.Decode(wav).Duration, 0.45, 0.7);
        }

        // Lossless stays exact: the same samples as the WAV.
        var alac = new MemoryStream();
        await registry.DecodeAsync(AudioSource.FromFile(Format("voice-alac.m4a")), alac);
        alac.Position = 0;
        using var original = File.OpenRead(Format("voice.wav"));
        Assert.Equal(WavDecoder.Decode(original).Channel(0).ToArray(), WavDecoder.Decode(alac).Channel(0).ToArray());

        // A damaged file: ffmpeg's own reason comes through.
        var broken = File.ReadAllBytes(Format("voice.ogg"))[..300];
        var e = await Assert.ThrowsAsync<InvalidDataException>(() => registry.DecodeAsync(new AudioSource("x.ogg", () => new MemoryStream(broken)), new MemoryStream()));
        output.WriteLine(e.Message);
        Assert.Contains("ffmpeg", e.Message);
    }

    private sealed class FakeRunner(string? path) : IProcessRunner
    {
        public List<string> Calls { get; } = [];
        public string? FindOnPath(string program) => path;

        public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
        {
            Calls.Add(string.Join(' ', arguments));
            return Task.FromResult(new ProcessResult(0, arguments[^1] == "-demuxers" ? Demuxers : Decoders, ""));
        }

        public Task<ProcessResult> RunAsync(string program, IReadOnlyList<string> arguments, Func<Stream, CancellationToken, Task> readOutput,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CollectingSink : IPcmSink
    {
        public int Rate, Channels;
        public List<float> Samples { get; } = [];
        public void SetFormat(int sampleRate, int channels) => (Rate, Channels) = (sampleRate, channels);
        public void Write(ReadOnlySpan<float> interleaved) => Samples.AddRange(interleaved);
    }

    /// <summary>Returns at most 777 bytes per read, like a pipe.</summary>
    private sealed class Trickle(byte[] data) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 777));
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 777)]);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 777)], cancellationToken);
    }
}
