namespace Reyfen.Timbratune.Shared.Platform;

/// <summary>
/// Where the head put the reference voices: the folder holding reference.json and
/// reference-audio/, or null where they aren't shipped. Only read when
/// <see cref="IFeatureFlags.ReferenceVoices"/> is on.
/// </summary>
public sealed record ReferenceVoiceFolder(string? Path)
{
    public static readonly ReferenceVoiceFolder None = new((string?)null);
}
