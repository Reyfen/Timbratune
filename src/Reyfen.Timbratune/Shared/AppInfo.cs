namespace Reyfen.Timbratune.Shared;

/// <summary>What the app says about itself: version, credits and links.</summary>
public static class AppInfo
{
    /// <summary>"v0.1.0": the build's version (Directory.Build.props), without any "+commit" suffix.</summary>
    public static string VersionText => "v" + Version;

    /// <summary>"0.1.0": the build's version (Directory.Build.props), without any "+commit" suffix.</summary>
    public static string Version { get; } = System.Reflection.CustomAttributeExtensions
        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(AppInfo).Assembly)
        ?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    /// <summary>Footer credit: "Timbratune v0.1.0 · by Reyfen".</summary>
    public static string CreditsText => $"Timbratune {VersionText} · by Reyfen";

    /// <summary>The original app Timbratune is forked from.</summary>
    public static Uri EuphoniaUri { get; } = new("https://github.com/Yuuzulight/Euphonia");

    /// <summary>Praat, whose methods the analysis algorithms follow (no Praat code is used).</summary>
    public static Uri PraatUri { get; } = new("https://github.com/praat/praat.github.io");

    /// <summary>What tapping a metric card offers.</summary>
    public static string CompareHint(bool referenceVoices) => referenceVoices
        ? "tap to see how you compare to real voices 🔍"
        : "tap to compare with your other takes 🔍";
}
