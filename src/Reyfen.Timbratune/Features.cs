namespace Reyfen.Timbratune;

/// <summary>
/// Build-time feature switches. Turn one on with an MSBuild property, e.g.
/// <c>dotnet build -p:TimbratuneReferenceVoices=true</c>.
/// </summary>
public static class Features
{
    /// <summary>
    /// The real reference voices (VCTK clips) in the metric comparison. Off by default: the
    /// comparison then shows your own takes only, and the clips are not shipped.
    /// </summary>
#if REFERENCE_VOICES
    public const bool ReferenceVoices = true;
#else
    public const bool ReferenceVoices = false;
#endif

    /// <summary>"v0.1.0": the build's version (Directory.Build.props), without any "+commit" suffix.</summary>
    public static string VersionText { get; } = "v" + (System.Reflection.CustomAttributeExtensions
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Features).Assembly)
        ?.InformationalVersion.Split('+')[0] ?? "0.0.0");

    /// <summary>Footer credit: "Timbratune v0.1.0 · by Reyfen".</summary>
    public static string CreditsText => $"Timbratune {VersionText} · by Reyfen";

    /// <summary>The original app Timbratune is forked from.</summary>
    public static Uri EuphoniaUri { get; } = new("https://github.com/Yuuzulight/Euphonia");

    /// <summary>Praat, whose methods the analysis algorithms follow (no Praat code is used).</summary>
    public static Uri PraatUri { get; } = new("https://github.com/praat/praat.github.io");

    /// <summary>What tapping a metric card offers.</summary>
    public const string CompareHint = ReferenceVoices
        ? "tap to see how you compare to real voices 🔍"
        : "tap to compare with your other takes 🔍";
}
