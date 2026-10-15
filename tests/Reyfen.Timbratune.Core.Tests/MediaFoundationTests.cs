using Reyfen.Timbratune.Acoustics;
using Reyfen.Timbratune.Audio.MediaFoundation;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Xunit.Abstractions;

namespace Reyfen.Timbratune.Core.Tests;

/// <summary>Windows' decoders through Media Foundation. Elsewhere these pass with a note.</summary>
public sealed class MediaFoundationTests(ITestOutputHelper output)
{
    private static string Format(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "formats", name);

    private bool OnWindows()
    {
        if (OperatingSystem.IsWindows()) return true;
        output.WriteLine("Not Windows: Media Foundation tests skipped.");
        return false;
    }

    [Fact]
    public async Task TheProbeFindsWhatEveryWindowsHas()
    {
        if (!OnWindows()) return;
        var provider = new MediaFoundationDecoderProvider();
        var formats = await provider.ProbeAsync();
        output.WriteLine($"{formats} in {provider.LastProbeTime.TotalMilliseconds:F0} ms");
        // Windows 10/11 (not the N editions without the Media Feature Pack) decode these out of the box.
        foreach (var id in new[] { "m4a", "mp4", "3gp", "aac", "wma", "mp3", "flac" }) Assert.Contains(id, formats);
        Assert.DoesNotContain("caf", formats);
    }

    [Fact]
    public async Task LosslessAudioDecodesToTheSameSamples()
    {
        if (!OnWindows()) return;
        var registry = new DecoderRegistry([new MediaFoundationDecoderProvider()]);
        var wav = new MemoryStream();
        var decoded = await registry.DecodeAsync(AudioSource.FromFile(Format("voice-alac.m4a")), wav);
        Assert.Equal(("m4a", MediaFoundationDecoderProvider.ProviderId), (decoded.Format.Id, decoded.ProviderId));
        wav.Position = 0;
        using var original = File.OpenRead(Format("voice.wav"));
        Assert.Equal(WavDecoder.Decode(original).Channel(0).ToArray(), WavDecoder.Decode(wav).Channel(0).ToArray());
    }

    [Theory]
    [InlineData("voice.m4a", "m4a")]
    [InlineData("voice.aac", "aac")]
    [InlineData("voice.3gp", "3gp")]
    [InlineData("voice.mp4", "mp4")]
    [InlineData("voice.wma", "wma")]
    public async Task CompressedAudioDecodesToTheWholeClip(string file, string format)
    {
        if (!OnWindows()) return;
        var registry = new DecoderRegistry([new MediaFoundationDecoderProvider()]);
        var wav = new MemoryStream();
        // Named wrongly on purpose: Media Foundation gets a copy with the right extension.
        var source = new AudioSource("clip.bin", () => File.OpenRead(Format(file)));
        var decoded = await registry.DecodeAsync(source, wav);
        Assert.Equal(format, decoded.Format.Id);
        wav.Position = 0;
        var sound = WavDecoder.Decode(wav);
        Assert.Equal(44100, sound.SamplingFrequency);
        // Encoder priming and container padding add up to ~0.15 s at these low bitrates.
        Assert.InRange(sound.Duration, 0.45, 0.7);
        // About as loud as the original (the quiet start of a take, through low-bitrate codecs).
        using var original = File.OpenRead(Format("voice.wav"));
        var peak = WavDecoder.Decode(original).Channel(0).ToArray().Max(Math.Abs);
        Assert.InRange(sound.Channel(0).ToArray().Max(Math.Abs), peak * 0.5, peak * 2);
    }
}
