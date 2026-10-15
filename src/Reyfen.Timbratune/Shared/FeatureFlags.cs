namespace Reyfen.Timbratune.Shared;

/// <summary>Product switches: parts of the app that are on in some builds only.</summary>
public interface IFeatureFlags
{
    /// <summary>
    /// The real reference voices (VCTK clips) in the metric comparison. Off by default: the
    /// comparison then shows your own takes only, and the clips are not shipped.
    /// </summary>
    bool ReferenceVoices { get; }
}

/// <summary>
/// The switches as built. Turn one on with an MSBuild property, e.g.
/// <c>dotnet build -p:TimbratuneReferenceVoices=true</c>.
/// </summary>
public sealed class BuildFeatureFlags : IFeatureFlags
{
#if REFERENCE_VOICES
    public const bool ReferenceVoicesBuilt = true;
#else
    public const bool ReferenceVoicesBuilt = false;
#endif

    public bool ReferenceVoices => ReferenceVoicesBuilt;
}
