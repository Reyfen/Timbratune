using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Acoustics.Formants;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Audio.Decoding;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>Import decoding: telling formats apart, the managed WAV/AIFF decoder, the analysis WAV, the registry.</summary>
public sealed class DecodingTests
{
    private static string Format(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "formats", name);

    [Theory]
    [InlineData("voice.wav", "wav")]
    [InlineData("voice-rf64.wav", "wav")]
    [InlineData("voice-adpcm.wav", "wav")]
    [InlineData("voice.aiff", "aiff")]
    [InlineData("voice-22k-stereo.aiff", "aiff")]
    [InlineData("voice.mp3", "mp3")]
    [InlineData("voice-id3.mp3", "mp3")]
    [InlineData("voice.flac", "flac")]
    [InlineData("voice.ogg", "ogg")]
    [InlineData("voice.opus", "opus")]
    [InlineData("voice.aac", "aac")]
    [InlineData("voice.m4a", "m4a")]
    [InlineData("voice.mp4", "mp4")]
    [InlineData("voice.3gp", "3gp")]
    [InlineData("voice.amr", "amr")]
    [InlineData("voice.caf", "caf")]
    [InlineData("voice.webm", "webm")]
    [InlineData("voice.mka", "mkv")]
    [InlineData("voice.wma", "wma")]
    public void TheContentSaysWhatAFileIs(string file, string format)
    {
        Assert.Equal(format, FormatSniffer.Sniff(AudioSource.FromFile(Format(file)))?.Id);
        // Named wrongly or not at all, it is still the same format.
        Assert.Equal(format, FormatSniffer.Sniff(AudioSource.FromFile(Format(file), "renamed.txt"))?.Id);
        Assert.Equal(format, FormatSniffer.Sniff(new NonSeekable(File.ReadAllBytes(Format(file))))?.Id);
    }

    [Fact]
    public void TheExtensionOnlyChoosesWithinTheMp4Family()
    {
        // An MP4 container named .m4a is an M4A (and its original is kept as source.m4a); named .mp3 it stays MP4.
        Assert.Equal("m4a", FormatSniffer.Sniff(AudioSource.FromFile(Format("voice.mp4"), "song.m4a"))?.Id);
        Assert.Equal("mp4", FormatSniffer.Sniff(AudioSource.FromFile(Format("voice.mp4"), "song.mp3"))?.Id);
        Assert.Equal("3gp", FormatSniffer.Sniff(AudioSource.FromFile(Format("voice.m4a"), "clip.3gp"))?.Id);
    }

    [Fact]
    public void ALargeTagBeforeTheAudioIsSkipped()
    {
        // ID3v2 with 200 KB of "cover art": the frames start well past the first 64 KB.
        const int size = 200_000;
        var tag = new byte[10 + size];
        "ID3"u8.CopyTo(tag);
        tag[3] = 4;
        tag[6] = (byte)((size >> 21) & 0x7F);
        tag[7] = (byte)((size >> 14) & 0x7F);
        tag[8] = (byte)((size >> 7) & 0x7F);
        tag[9] = (byte)(size & 0x7F);
        foreach (var file in new[] { "voice.mp3", "voice.flac", "voice.aac" })
        {
            var bytes = tag.Concat(File.ReadAllBytes(Format(file))).ToArray();
            Assert.Equal(Path.GetExtension(file)[1..], FormatSniffer.Sniff(new MemoryStream(bytes))?.Id);
            Assert.Equal(Path.GetExtension(file)[1..], FormatSniffer.Sniff(new NonSeekable(bytes))?.Id);
        }
    }

    [Fact]
    public void WhatIsNotAudioIsNotGuessed()
    {
        Assert.Null(FormatSniffer.Sniff(new MemoryStream("not audio at all"u8.ToArray())));
        Assert.Null(FormatSniffer.Sniff(new MemoryStream([])));
        Assert.Null(FormatSniffer.Sniff(new MemoryStream([0x50, 0x4B, 3, 4, 20, 0, 0, 0]))); // a zip (.tmbr is handled by the importer)
        Assert.Null(FormatSniffer.Sniff(new MemoryStream([0xFF, 0xFB, 0x50, 0xC4, 1, 2, 3, 4, 5, 6, 7, 8]))); // one stray frame header
        var random = new byte[100_000];
        new Random(7).NextBytes(random);
        random[0] = 0xFF;
        random[1] = 0xFB;
        Assert.Null(FormatSniffer.Sniff(new MemoryStream(random)));
    }

    [Fact]
    public async Task AiffDecodesToTheSameSamplesAsTheWavOfThem()
    {
        // 16-bit mono: the fixture's samples exactly. 24-bit stereo 22 kHz: the same as the WAV of the same data.
        Assert.Equal(Samples(Format("voice.wav")), await DecodeManaged("voice.aiff"));
        Assert.Equal(Samples(Format("voice-22k-stereo.wav")), await DecodeManaged("voice-22k-stereo.aiff"));
        Assert.Equal(Samples(Format("voice.wav")), await DecodeManaged("voice-rf64.wav"));
        Assert.Equal(22050, AiffDecoder.Extended([0x40, 0x0D, 0xAC, 0x44, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void CompressedWavIsLeftToTheGeneralDecoders()
    {
        using var adpcm = File.OpenRead(Format("voice-adpcm.wav"));
        Assert.Throws<FormatException>(() => WavDecoder.Decode(adpcm));
    }

    [Fact]
    public void MonoAt44kPassesThroughUnchanged()
    {
        var samples = Samples(Format("voice.wav")).Select(s => (float)s).ToArray();
        var expected = new MemoryStream();
        using (var writer = new WavWriter(expected, 44100, 1)) writer.Write(samples);

        var actual = new MemoryStream();
        using (var sink = new AnalysisWavSink(actual, leaveOpen: true))
        {
            sink.SetFormat(44100, 1);
            sink.Write(samples.AsSpan(0, 1000));
            sink.Write(samples.AsSpan(1000));
            sink.Complete();
            Assert.Equal(samples.Length, sink.Frames);
        }
        Assert.Equal(expected.ToArray(), actual.ToArray());
    }

    [Fact]
    public void StereoIsAveragedAndOtherRatesAreResampled()
    {
        var stereo = new float[] { 0.5f, -0.5f, 0.25f, 0.75f, -1f, 1f };
        var actual = new MemoryStream();
        using (var sink = new AnalysisWavSink(actual, leaveOpen: true))
        {
            sink.SetFormat(44100, 2);
            sink.Write(stereo);
            sink.Complete();
        }
        actual.Position = 0;
        Assert.Equal([0, 0.5, 0], WavDecoder.Decode(actual).Channel(0).ToArray().Select(v => Math.Round(v, 3)));

        // 22.05 kHz stereo: downmixed, then resampled with the Acoustics resampler in one pass.
        using var source = File.OpenRead(Format("voice-22k-stereo.wav"));
        var sound = WavDecoder.Decode(source);
        var mono = sound.ToMono().Select(v => (double)(float)v).ToArray();
        var expected = Resampler.Resample(new Sound(mono, 22050), 44100).Channel(0).ToArray();
        var wav = new MemoryStream();
        using (var sink = new AnalysisWavSink(wav, leaveOpen: true))
        {
            sink.SetFormat(22050, 2);
            var interleaved = new float[sound.SampleCount * 2];
            for (var i = 0; i < sound.SampleCount; i++)
            {
                interleaved[2 * i] = (float)sound.Channel(0)[i];
                interleaved[2 * i + 1] = (float)sound.Channel(1)[i];
            }
            for (var at = 0; at < interleaved.Length; at += 2 * 3001) // odd block sizes on purpose
                sink.Write(interleaved.AsSpan(at, Math.Min(2 * 3001, interleaved.Length - at)));
            sink.Complete();
            Assert.Equal(expected.Length, sink.Frames);
        }
        wav.Position = 0;
        var written = WavDecoder.Decode(wav);
        Assert.Equal(44100, written.SamplingFrequency);
        Assert.Equal(expected.Select(v => (double)WavWriter.ToPcm16((float)v) / 32768), written.Channel(0).ToArray());
    }

    [Fact]
    public void NothingDecodedIsAnError()
    {
        using var sink = new AnalysisWavSink(new MemoryStream());
        sink.SetFormat(48000, 2);
        Assert.Throws<InvalidDataException>(sink.Complete);
    }

    [Fact]
    public async Task TheRegistryTriesDecodersByPriorityUntilOneReadsTheFile()
    {
        var broken = new FakeProvider("broken", 1, ["wav"], fail: true);
        var managed = new ManagedDecoderProvider();
        var unused = new FakeProvider("unused", 9, ["wav"]);
        var registry = new DecoderRegistry([unused, managed, broken]);
        Assert.Equal(["broken", "managed", "unused"], registry.Providers.Select(p => p.Id));

        var target = new MemoryStream();
        var decoded = await registry.DecodeAsync(AudioSource.FromFile(Format("voice-22k-stereo.wav")), target);
        Assert.Equal(("wav", "managed"), (decoded.Format.Id, decoded.ProviderId));
        Assert.Equal(1, broken.Created);
        Assert.Equal(0, unused.Created);
        target.Position = 0;
        Assert.Equal(decoded.Frames, WavDecoder.Decode(target).SampleCount); // nothing of the failed attempt is left

        // Every decoder fails: one error saying so, with the last one's inside.
        var e = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new DecoderRegistry([broken]).DecodeAsync(AudioSource.FromFile(Format("voice.wav")), new MemoryStream()));
        Assert.Equal("its WAV audio couldn't be decoded (broken can't)", e.Message);
        Assert.Equal("broken can't", e.InnerException?.Message);
    }

    [Fact]
    public async Task TheRegistryKnowsWhatThisDeviceReads()
    {
        var failing = new FakeProvider("failing", 3, ["ogg"], probeFails: true);
        var registry = new DecoderRegistry([new ManagedDecoderProvider(), new FakeProvider("mp3", 2, ["mp3", "flac"]), failing]);
        Assert.Equal("wav, aiff, mp3, flac", (await registry.GetFormatsAsync()).ToString());
        Assert.Empty(await registry.GetFormatsAsync("failing"));
        Assert.Equal(1, failing.Probes);

        await Assert.ThrowsAsync<UnsupportedAudioException>(() => registry.DecodeAsync(AudioSource.FromFile(Format("voice.ogg")), new MemoryStream()));
        Assert.Equal(1, failing.Probes); // probed once, kept

        // Something got installed: a refresh probes again and says so.
        failing.ProbeFails = false;
        var changed = 0;
        registry.CapabilitiesChanged += (_, _) => changed++;
        Assert.Contains("ogg", await registry.RefreshAsync());
        Assert.Equal((2, 1), (failing.Probes, changed));
    }

    private static async Task<double[]> DecodeManaged(string file)
    {
        var sink = new CollectingSink();
        using var decoder = new ManagedDecoderProvider().Create();
        await decoder.DecodeAsync(AudioSource.FromFile(Format(file)), sink, null, CancellationToken.None);
        return sink.Samples.Select(s => (double)s).ToArray();
    }

    /// <summary>A WAV's samples, interleaved, as floats would carry them.</summary>
    private static double[] Samples(string wav)
    {
        using var stream = File.OpenRead(wav);
        var sound = WavDecoder.Decode(stream);
        var all = new double[sound.SampleCount * sound.ChannelCount];
        for (var c = 0; c < sound.ChannelCount; c++)
            for (var i = 0; i < sound.SampleCount; i++)
                all[i * sound.ChannelCount + c] = (float)sound.Channel(c)[i];
        return all;
    }

    private sealed class CollectingSink : IPcmSink
    {
        public List<float> Samples { get; } = [];
        public void SetFormat(int sampleRate, int channels) { }
        public void Write(ReadOnlySpan<float> interleaved) => Samples.AddRange(interleaved);
    }

    private sealed class FakeProvider(string id, int priority, string[] formats, bool fail = false, bool probeFails = false) : IAudioDecoderProvider
    {
        public int Created, Probes;
        public bool ProbeFails = probeFails;
        public string Id => id;
        public int Priority => priority;

        public ValueTask<FormatSet> ProbeAsync(CancellationToken cancellationToken = default)
        {
            Probes++;
            return ProbeFails ? throw new IOException("can't tell") : new(new FormatSet(formats));
        }

        public IAudioDecoder Create()
        {
            Created++;
            return new Decoder(id, fail);
        }

        private sealed class Decoder(string id, bool fail) : IAudioDecoder
        {
            public Task DecodeAsync(AudioSource source, IPcmSink sink, IProgress<double>? progress, CancellationToken cancellationToken)
            {
                sink.SetFormat(8000, 1);
                sink.Write([0.1f, 0.2f, 0.3f]); // a partial result, then the failure
                return fail ? throw new InvalidDataException($"{id} can't") : Task.CompletedTask;
            }

            public void Dispose() { }
        }
    }

    /// <summary>A stream that can't seek (Android's content streams).</summary>
    private sealed class NonSeekable(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 1000)); // short reads too
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
