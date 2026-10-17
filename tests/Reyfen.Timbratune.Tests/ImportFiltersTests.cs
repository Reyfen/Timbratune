using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Features.Import;

namespace Reyfen.Timbratune.Tests;

/// <summary>The Import picker offers what this device decodes, and nothing else.</summary>
public sealed class ImportFiltersTests
{
    [Fact]
    public void EverythingFirstThenEachFormatThenTakes()
    {
        var types = ImportFilters.For(new FormatSet(["mp3", "wav", "aiff", "flac"]));
        Assert.Equal(["Audio and Timbratune takes", "WAV", "AIFF", "MP3", "FLAC", "Timbratune takes"], types.Select(t => t.Name));
        Assert.Equal(["wav", "wave", "aiff", "aif", "aifc", "mp3", "flac", "tmbr"], types[0].Extensions);
        Assert.Contains("audio/mpeg", types[0].MimeTypes);
        Assert.Contains("application/octet-stream", types[0].MimeTypes); // .tmbr, for Android's pickers
        Assert.Null(types[0].AppleUtis); // .tmbr has no type identifier: the extensions are used on Apple platforms
        Assert.Equal(["public.mp3"], types[3].AppleUtis);
        Assert.DoesNotContain(types, t => t.Extensions.Contains("*") || t.Name.Contains("All files"));
    }

    [Fact]
    public void FormatsWithoutATypeIdentifierUseExtensions()
    {
        var types = ImportFilters.For(new FormatSet(["ogg", "opus"]));
        Assert.All(types, t => Assert.Null(t.AppleUtis));
        Assert.Equal(["ogg", "oga", "opus", "tmbr"], types[0].Extensions);
        Assert.Equal(1, types[0].MimeTypes.Count(m => m == "audio/ogg")); // shared by both formats, listed once
    }

    [Fact]
    public void WithoutAnyDecoderOnlyTakesAreOffered()
    {
        Assert.Equal([ImportFilters.Takes], ImportFilters.For(FormatSet.Empty));
    }
}
