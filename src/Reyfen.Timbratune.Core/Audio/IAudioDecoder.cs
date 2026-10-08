namespace Reyfen.Timbratune.Core.Audio;

/// <summary>Turns compressed audio (MP3, FLAC, …) into a WAV the analysis can read.</summary>
public interface IAudioDecoder
{
    /// <summary>Decodes <paramref name="sourcePath"/> to a 16-bit mono WAV at <paramref name="wavPath"/>; throws if it can't.</summary>
    void DecodeToWav(string sourcePath, string wavPath);
}
