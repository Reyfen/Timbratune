using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.App;
using Reyfen.Timbratune.Audio.Android;
using Reyfen.Timbratune.Audio.SoundFlow;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Shared.Platform;

namespace Reyfen.Timbratune.Android;

/// <summary>
/// The process: Avalonia 12 builds the app here, before any Activity exists, so the services
/// (one container for the process: one audio engine and recorder), fonts and renderer are set up
/// here too. Anything that needs the Activity (permission requests, the share sheet) goes through
/// <see cref="MainActivity.Current"/>, as the Activity can be re-created.
/// </summary>
[Application] // label, icon and backup: Properties/AndroidManifest*.xml
public sealed class AndroidApp : AvaloniaAndroidApplication<TimbratuneApp>
{
    // Lives as long as the process (Android ends it without a call back).
    private ServiceProvider? _services;

    /// <summary>Vulkan is the first renderer asked for: its surface needs MainActivity's watcher.</summary>
    internal static bool VulkanWanted { get; private set; }

    public AndroidApp(IntPtr javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
#if PROFILING
        // Experiment switches are read first: some (e.g. "vulkan") apply while the app is built.
        var flagsFile = System.IO.Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "perf-flags.txt");
        if (System.IO.File.Exists(flagsFile))
            Diagnostics.Perf.Flags = System.IO.File.ReadAllText(flagsFile).Split((char[])[' ', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
#endif
        var fakeMic = FakeMic();
        var paths = DataFolder();
#if PROFILING
        // Test takes recorded from the fake mic go to their own folder, not the user's takes.
        if (fakeMic is not null) paths = new DataPaths(System.IO.Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "profiling-data"));
#endif
        var hint = FolderHint(paths);
        var services = new ServiceCollection().AddTimbratune();
        services.AddSingleton(paths);
        services.AddSingleton<IPlatformInfo>(new PlatformInfo
        {
            IsMobile = true,
            CanOpenDataFolder = hint is null,
            DataFolderHint = hint,
            CanShare = true,
            CanRunProcesses = false,
        });
        services.AddSingleton(_ => new SoundFlowAudio());
        services.AddSingleton<IAudioDecoderProvider>(sp => new MiniaudioDecoderProvider(sp.GetRequiredService<SoundFlowAudio>));
        services.AddSingleton<IAudioDecoderProvider, MediaCodecDecoderProvider>();
        services.AddSingleton<IAudioRecorder>(sp => fakeMic is not null ? new FileReplayRecorder(fakeMic) : sp.GetRequiredService<SoundFlowAudio>().CreateRecorder());
        services.AddSingleton<IAudioPlayer>(sp => sp.GetRequiredService<SoundFlowAudio>().CreatePlayer());
        services.AddSingleton<IPermissions, AndroidPermissions>();
        services.AddSingleton<IThreadPriority, AndroidThreadPriority>();
        services.AddSingleton<IFileSharer>(new AndroidFileSharer(() => MainActivity.Current));
        // Settings in the app's private folder, not with the takes in shared storage (a PC sees
        // that one over USB, and settings would be lost with a moved data folder).
        services.AddSingleton<IDataStore>(new FileSystemDataStore(FilesDir!.AbsolutePath));
        _services = services.BuildServiceProvider();
#if PROFILING
        // "noprobes": keep the fake mic and switches but log nothing (the probes' own cost out of the measurement).
        if (!Diagnostics.Perf.Flags.Contains("noprobes")) Diagnostics.Perf.Sink = line => global::Android.Util.Log.Info("Timbratune", line);
#endif
        base.OnCreate();
    }

    /// <summary>
    /// The app's folder in shared storage (Android/data/com.reyfen.timbratune/files): a PC sees
    /// it over USB, and so do some file managers. Like private storage it's removed with the
    /// app. Takes kept in private storage by earlier versions are moved there once. Without
    /// shared storage (not mounted) private storage is used as before.
    /// </summary>
    private DataPaths DataFolder()
    {
        var old = DataPaths.Default();
        var external = GetExternalFilesDir(null)?.AbsolutePath;
        if (external is null) return old;
        var paths = new DataPaths(external);
        try
        {
            DataPaths.MoveContents(old.Root, paths.Root);
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("Timbratune", $"couldn't move the takes to {paths.Root}: {e.Message}");
            return old;
        }
        return paths;
    }

    /// <summary>Android can't open the app's folder in a file manager: say where it is instead.</summary>
    private static string? FolderHint(DataPaths paths) =>
        paths.TakesDir.Contains("/Android/data/", StringComparison.Ordinal)
            ? "Android/data/com.reyfen.timbratune/files/takes on this phone · open it from a PC over USB to add or copy takes"
            : null;

    /// <summary>
    /// Debug builds only: files/fake-mic.wav in the app's private folder replays as if it were
    /// being spoken (the desktop's TIMBRATUNE_FAKE_MIC). Put it there with
    /// <c>adb push take.wav /data/local/tmp/ &amp;&amp; adb shell run-as com.reyfen.timbratune cp /data/local/tmp/take.wav files/fake-mic.wav</c>.
    /// (debug.mono.env can't carry it: .NET for Android 36.1 aborts on any value there.)
    /// </summary>
    private string? FakeMic()
    {
#if DEBUG
        var path = System.IO.Path.Combine(FilesDir!.AbsolutePath, "fake-mic.wav");
        return System.IO.File.Exists(path) ? path : null;
#elif PROFILING
        // Release builds can't be written into over USB; the external app folder can:
        // adb push take.wav /sdcard/Android/data/com.reyfen.timbratune/files/fake-mic.wav
        var path = System.IO.Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "fake-mic.wav");
        return System.IO.File.Exists(path) ? path : null;
#else
        return null;
#endif
    }

    /// <summary>The app with this process' services (instead of the base class' parameterless one).</summary>
    protected override AppBuilder CreateAppBuilder() =>
        AppBuilder.Configure(() => new TimbratuneApp(_services ?? throw new InvalidOperationException("services are built in OnCreate")))
            .UseAndroid();

    // Inter has no emoji, and Android's own emoji font is COLRv1, which this Skia can't draw
    // (boxes). So the app carries the emoji it uses as a tiny bitmap (CBDT) Noto Color Emoji
    // subset, built by scripts/make-emoji-font.py.
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont()
            .ConfigureFonts(fonts => fonts.AddFontCollection(
                new EmbeddedFontCollection(new Uri("fonts:TimbratuneEmoji"), new Uri("avares://Reyfen.Timbratune.Android/Assets/Fonts"))))
            .With(new FontManagerOptions
            {
                FontFallbacks = [new FontFallback { FontFamily = new FontFamily("fonts:TimbratuneEmoji#Noto Color Emoji") }],
            })
            .With(new AndroidPlatformOptions { RenderingMode = RenderingModes() });

    /// <summary>
    /// Vulkan first, then OpenGL ES, then software. Measured scrolling on a Pixel 9 (Android 17,
    /// where OpenGL ES runs through a translation layer on Vulkan): ~15 % janky frames with
    /// Vulkan against ~62 % with OpenGL ES; live recording ~2 % against ~10 %. If Vulkan can't
    /// start, Avalonia falls back to the next. Its surface needs one workaround: MainActivity.WatchSurface.
    /// </summary>
    private IReadOnlyList<AndroidRenderingMode> RenderingModes()
    {
#if PROFILING
        // A/B switch: "egl" renders through OpenGL ES.
        if (Diagnostics.Perf.Flags.Contains("egl")) return [AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
#endif
        switch (ForcedRenderer())
        {
            case "egl":
                VulkanWanted = false;
                return [AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
            case "software":
                VulkanWanted = false;
                return [AndroidRenderingMode.Software];
        }
        VulkanWanted = true;
        return [AndroidRenderingMode.Vulkan, AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
    }

    /// <summary>
    /// renderer.txt in the app's folder ("vulkan", "egl" or "software"; adb push, then restart
    /// the app) picks the renderer, for tests. Null: the default.
    /// </summary>
    private string? ForcedRenderer()
    {
        try
        {
            var file = System.IO.Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "renderer.txt");
            if (System.IO.File.Exists(file))
            {
                var text = System.IO.File.ReadAllText(file).Trim().ToLowerInvariant();
                if (text is "vulkan" or "egl" or "software") return text;
            }
        }
        catch (Exception)
        {
            // Unreadable override: automatic.
        }
        return null;
    }
}
