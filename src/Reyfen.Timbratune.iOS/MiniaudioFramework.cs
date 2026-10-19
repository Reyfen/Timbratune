using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Foundation;

namespace Reyfen.Timbratune.iOS;

/// <summary>
/// Lets SoundFlow 1.4.1 find miniaudio on iOS. The build embeds it as Frameworks/miniaudio.framework
/// (NativeReference in the project; the App Store takes frameworks only there), but SoundFlow's
/// resolver asks for "miniaudio" and then for runtimes/ios-arm64/native/… in the bundle, and finds
/// neither (build 12: DllNotFoundException: miniaudio). The runtime swallows the resolver's failure
/// and ends with the load context's ResolvingUnmanagedDll event, which loads the embedded framework.
/// (Pointing the base directory at a link to the framework, the first attempt, didn't work.)
/// </summary>
internal static class MiniaudioFramework
{
    public static void Prepare()
    {
        var path = Path.Combine(NSBundle.MainBundle.PrivateFrameworksPath!, "miniaudio.framework", "miniaudio");
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
        {
            if (name != "miniaudio") return IntPtr.Zero;
            if (NativeLibrary.TryLoad(path, out var handle)) return handle;
            CrashLog.Write("miniaudio not loadable", $"{path} (exists: {File.Exists(path)})");
            return IntPtr.Zero;
        };
    }
}
