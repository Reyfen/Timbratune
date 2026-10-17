using Foundation;

namespace Reyfen.Timbratune.iOS;

/// <summary>
/// Lets SoundFlow 1.4.1 find miniaudio on iOS. Its resolver first asks for "miniaudio" (which
/// iOS doesn't resolve to a framework), then loads
/// <c>{AppContext.BaseDirectory}/runtimes/ios-arm64/native/miniaudio.framework/miniaudio</c>.
/// The framework can't ship there: the App Store only takes frameworks in the bundle's
/// Frameworks/, where the build embeds and signs it (NativeReference in the project). So a folder
/// in Caches gets that path, with the framework folder as a link to the real one, and becomes
/// the base directory SoundFlow combines with. Nothing else in the app reads the base directory
/// (assemblies and resources come from the bundle).
/// </summary>
internal static class MiniaudioFramework
{
    private const string Relative = "runtimes/ios-arm64/native/miniaudio.framework";

    public static void Prepare()
    {
        var framework = Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath!, "miniaudio.framework");
        if (!Directory.Exists(framework))
        {
            Console.WriteLine($"Timbratune: {framework} is missing; recording won't work");
            return;
        }
        // Recreated every start: the bundle's path changes with each install or update.
        var root = Path.Combine(IosFolders.Caches, "soundflow");
        var link = Path.Combine(root, Relative);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, framework);
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", root + Path.DirectorySeparatorChar);
    }
}
