namespace Euphonia.Core.Tests.PraatReference;

/// <summary>Finds the Praat executable (dev/test oracle only) without any platform-specific API.</summary>
public static class PraatLocator
{
    public const string PraatEnvVar = "EUPHONIA_PRAAT";

    /// <summary>
    /// Lookup order: $EUPHONIA_PRAAT → &lt;app&gt;/praat/ (bundled) →
    /// tools/praat/ in any parent folder (dev checkout, filled by
    /// scripts/fetch-praat.ps1) → PATH.
    /// </summary>
    public static string? Find(string? appBaseDirectory = null)
    {
        var fromEnv = Environment.GetEnvironmentVariable(PraatEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return Path.GetFullPath(fromEnv);

        var baseDir = appBaseDirectory ?? AppContext.BaseDirectory;
        var bundled = FindIn(Path.Combine(baseDir, "praat"));
        if (bundled is not null) return bundled;

        for (var dir = new DirectoryInfo(baseDir); dir is not null; dir = dir.Parent)
        {
            var dev = FindIn(Path.Combine(dir.FullName, "tools", "praat"));
            if (dev is not null) return dev;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var hit = FindIn(entry);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static string? FindIn(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        foreach (var name in CandidateNames)
        {
            var candidate = Path.Combine(dir, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    // Windows zip ships Praat.exe; Linux tarballs ship "praat" (or
    // "praat_barren", the GUI-less build); the macOS app bundle keeps it here.
    private static IEnumerable<string> CandidateNames =>
        OperatingSystem.IsWindows()
            ? ["Praat.exe", "praat.exe"]
            : OperatingSystem.IsMacOS()
                ? ["Praat.app/Contents/MacOS/Praat", "praat"]
                : ["praat_barren", "praat"];
}
