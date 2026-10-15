using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Reyfen.Timbratune.Audio.SoundFlow;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Services;

namespace Reyfen.Timbratune.Android;

[Activity(
    Label = "Timbratune",
    Theme = "@style/TimbratuneTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public sealed class MainActivity : AvaloniaMainActivity<App>
{
    private const int MicrophoneRequest = 1001;

    // One audio engine and recorder for the process: the activity can be re-created.
    private static SoundFlowAudio? s_audio;
    private static IAudioRecorder? s_recorder;
    private TaskCompletionSource<bool>? _microphone;

    // The live Activity. The app's services are built once per process, but the Activity can be
    // re-created (when the surface is replaced, see WatchSurface), so anything needing it
    // (permission requests, the share sheet) goes through this rather than holding on to the
    // first one.
    private static MainActivity? s_current;

    // Vulkan is the first renderer asked for: its surface needs the watcher below.
    private static bool s_vulkanWanted;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if PROFILING
        // Experiment switches are read first: some (e.g. "vulkan") apply while the app is built.
        var flagsFile = System.IO.Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "perf-flags.txt");
        if (System.IO.File.Exists(flagsFile))
            Diagnostics.Perf.Flags = System.IO.File.ReadAllText(flagsFile).Split((char[])[' ', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
#endif
        s_audio ??= new SoundFlowAudio();
        var fakeMic = FakeMic();
        s_recorder ??= fakeMic is not null ? new FileReplayRecorder(fakeMic) : s_audio.CreateRecorder();
        var audio = s_audio;
        var recorder = s_recorder;
        var paths = DataFolder();
#if PROFILING
        // Test takes recorded from the fake mic go to their own folder, not the user's takes.
        if (fakeMic is not null) paths = new DataPaths(System.IO.Path.Combine(GetExternalFilesDir(null)!.AbsolutePath, "profiling-data"));
#endif
        App.ServicesFactory = dialogs => new AppServices(
            Store: new RecordingStore(paths),
            Engine: new AcousticsAnalysisEngine(),
            Recorder: recorder,
            Playback: new PlaybackService(audio.CreatePlayer()),
            Dialogs: dialogs,
            RequestMicrophone: () => s_current?.RequestMicrophoneAsync() ?? Task.FromResult(false),
            LowerThreadPriority: () => global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Background),
            Decoder: audio,
            DataFolderHint: FolderHint(paths),
            Sharer: new AndroidFileSharer(() => s_current));
        s_current = this;
#if PROFILING
        // "noprobes": keep the fake mic and switches but log nothing (the probes' own cost out of the measurement).
        if (!Diagnostics.Perf.Flags.Contains("noprobes")) Diagnostics.Perf.Sink = line => global::Android.Util.Log.Info("Timbratune", line);
#endif
        base.OnCreate(savedInstanceState);
        global::Android.Util.Log.Info("Timbratune", $"started: vulkan wanted: {s_vulkanWanted}, multi-window: {IsInMultiWindowMode}");
        WatchSurface();
#if PROFILING
        if (Avalonia.Application.Current is { } app)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Diagnostics.Perf.ApplyExperiments(app,
                (app.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime)?.MainView is { } v
                    ? Avalonia.Controls.TopLevel.GetTopLevel(v) : null));
        Diagnostics.Perf.StartStallWatch();
        if (Diagnostics.Perf.Flags.Contains("fftbench")) System.Threading.Tasks.Task.Run(FftBench.Run);
        Diagnostics.Perf.Note($"started on {Build.Model}, Android {Build.VERSION.Release}, {System.Environment.ProcessorCount} cores");
#endif
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
    /// start, Avalonia falls back to the next. Its surface needs one workaround: WatchSurface.
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
                s_vulkanWanted = false;
                return [AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
            case "software":
                s_vulkanWanted = false;
                return [AndroidRenderingMode.Software];
        }
        s_vulkanWanted = true;
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

    // Vulkan and a replaced surface. Entering (or leaving) split screen replaces the SurfaceView's
    // Surface: a new native window, and the old one's buffer queue is abandoned. Avalonia's
    // Vulkan display (11.3.22) keeps its VkSurfaceKHR from the old window and only re-creates
    // the swapchain on it, every frame, against a dead window: logcat fills with "BufferQueue has
    // been abandoned" and the pane stays black, however it is resized or repainted (a forced
    // full repaint and a layout refresh were tried). A new view builds a new Vulkan surface from
    // the current window, so when the surface is replaced the Activity is re-created (about
    // half a second, with a flash). The process, recorder and view models keep running; the main
    // view is the same instance (SingleViewLifetime), attached to the new window.
    // This includes coming back to the app (Home, lock screen, Recents): Android destroys the
    // surface when the app stops and gives a new one on return. Skipping the re-creation when
    // the window came back at the same size was tried: it usually worked, but once (unlocking
    // in split screen) the display presented to the dead window for 3 s until a later resize.
    private sealed class SurfaceWatcher(MainActivity owner, bool hadSurface) : Java.Lang.Object, ISurfaceHolderCallback
    {
        private bool _seen = hadSurface;

        public void SurfaceCreated(ISurfaceHolder holder)
        {
            if (_seen) owner.OnSurfaceReplaced();
            _seen = true;
        }

        public void SurfaceChanged(ISurfaceHolder holder, global::Android.Graphics.Format format, int width, int height) { }
        public void SurfaceDestroyed(ISurfaceHolder holder) { }
    }

    private static long s_lastSurfaceRecreate;

    private void WatchSurface(int attempts = 20)
    {
        if (IsFinishing || IsDestroyed) return;
        if (FindSurfaceView(Window?.DecorView) is { } surfaceView)
        {
            var had = surfaceView.Holder?.Surface?.IsValid == true;
            surfaceView.Holder?.AddCallback(new SurfaceWatcher(this, had));
            return;
        }
        if (attempts > 1) new Handler(Looper.MainLooper!).PostDelayed(() => WatchSurface(attempts - 1), 50);
    }

    private static SurfaceView? FindSurfaceView(View? view)
    {
        if (view is SurfaceView surfaceView) return surfaceView;
        if (view is not ViewGroup group) return null;
        for (var i = 0; i < group.ChildCount; i++)
            if (FindSurfaceView(group.GetChildAt(i)) is { } found) return found;
        return null;
    }

    private void OnSurfaceReplaced()
    {
        // OpenGL ES and software take a replaced surface in their stride; only Vulkan needs this.
        if (IsFinishing || IsDestroyed || !s_vulkanWanted) return;
        // Never in a loop: at most one re-creation a second.
        var now = System.Environment.TickCount64;
        if (now - s_lastSurfaceRecreate < 1000) return;
        s_lastSurfaceRecreate = now;
        global::Android.Util.Log.Info("Timbratune", "surface replaced under Vulkan: re-creating the window");
        Recreate();
    }

    protected override void OnDestroy()
    {
        if (s_current == this) s_current = null;
        base.OnDestroy();
    }

    /// <summary>Asks for the microphone the first time; true when recording is allowed.</summary>
    private Task<bool> RequestMicrophoneAsync()
    {
        if (CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted) return Task.FromResult(true);
        _microphone?.TrySetResult(false);
        _microphone = new TaskCompletionSource<bool>();
        RequestPermissions([Manifest.Permission.RecordAudio], MicrophoneRequest);
        return _microphone.Task;
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != MicrophoneRequest) return;
        _microphone?.TrySetResult(grantResults.Length > 0 && grantResults[0] == Permission.Granted);
        _microphone = null;
    }
}
