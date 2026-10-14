using Android;
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
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

    // Renderer switching (see RendererSwitch): the backend the next OnCreate has to build, set
    // just before Recreate(); and whether Vulkan failed to start, so we stop trying it.
    private static bool? s_switchTo;
    private static bool s_vulkanUnavailable;

    // The live Activity. The app's services are built once per process, but the Activity can be
    // re-created (the renderer switch), so anything needing it (permission requests, the share
    // sheet) goes through this rather than holding on to the first one.
    private static MainActivity? s_current;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // A renderer switch: the old Activity and its view are gone by now (Recreate destroys
        // before it creates), so Avalonia's graphics can be rebuilt before the new view exists.
        if (s_switchTo is { } toVulkan)
        {
            s_switchTo = null;
            if (!RendererSwitch.TryRebuild(toVulkan, out var error))
            {
                global::Android.Util.Log.Warn("Timbratune", $"couldn't switch to {(toVulkan ? "Vulkan" : "OpenGL ES")}: {error}");
                if (toVulkan) s_vulkanUnavailable = true;
            }
        }
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
        global::Android.Util.Log.Info("Timbratune", $"renderer: {RendererSwitch.Describe()}, multi-window: {IsInMultiWindowMode}");
        // Vulkan was wanted but Avalonia fell back: don't keep trying to switch to it.
        if (ForcedRenderer() is null && !IsInMultiWindowMode && !RendererSwitch.IsVulkan) s_vulkanUnavailable = true;
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
    /// OpenGL ES, then software. Vulkan is faster (measured scrolling: on a Pixel 9, Android 17,
    /// where OpenGL ES runs through a translation layer on Vulkan, ~33 fps with OpenGL ES against
    /// ~56 fps; on a Pixel 4a 52 → 56 fps), but with Vulkan the picture goes black or partly black
    /// when the window is resized in split screen (Pixel 9, next to Discord; gone with OpenGL ES).
    /// The renderer can't change while the app runs, so Vulkan is opt-in: "vulkan" in renderer.txt.
    /// </summary>
    private IReadOnlyList<AndroidRenderingMode> RenderingModes()
    {
#if PROFILING
        // A/B switch: "egl" renders through OpenGL ES as before.
        if (Diagnostics.Perf.Flags.Contains("egl")) return [AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
#endif
        switch (ForcedRenderer())
        {
            case "vulkan": return [AndroidRenderingMode.Vulkan, AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
            case "egl": return [AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
            case "software": return [AndroidRenderingMode.Software];
        }
        // Vulkan, except in split screen / multi-window, where it goes black or partly black
        // when the window is resized (Pixel 9, Android 17): OpenGL ES there. Entering or leaving
        // multi-window while running switches (OnMultiWindowModeChanged).
        return IsInMultiWindowMode
            ? [AndroidRenderingMode.Egl, AndroidRenderingMode.Software]
            : [AndroidRenderingMode.Vulkan, AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
    }

    /// <summary>
    /// renderer.txt in the app's folder ("vulkan", "egl" or "software"; adb push, then restart
    /// the app) fixes the renderer, with no switching. Null: automatic.
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

    /// <summary>
    /// Makes the renderer match the window: Vulkan full-screen, OpenGL ES in multi-window.
    /// While a take is being recorded it waits (the switch re-creates the Activity).
    /// </summary>
    private void ApplyRendererPolicy()
    {
        if (IsFinishing || IsDestroyed || ForcedRenderer() is not null || !RendererSwitch.Supported) return;
        var wantVulkan = !IsInMultiWindowMode;
        if (RendererSwitch.IsVulkan == wantVulkan || (wantVulkan && s_vulkanUnavailable)) return;
        if (s_recorder?.IsRecording == true)
        {
            new Handler(Looper.MainLooper!).PostDelayed(ApplyRendererPolicy, 1000);
            return;
        }
        s_switchTo = wantVulkan;
        Recreate();
    }

    // Split screen: the activity handles its own configuration changes (no re-creation), so
    // when the window is resized, or moved into or out of multi-window, nothing makes the
    // render surface follow and it can stay black or partly drawn. Kick layout and a repaint
    // right away and once more after the system has settled the new bounds.
    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        RefreshSurface();
    }

    public override void OnMultiWindowModeChanged(bool isInMultiWindowMode, Configuration? newConfig)
    {
        base.OnMultiWindowModeChanged(isInMultiWindowMode, newConfig);
        RefreshSurface();
        // Let the system settle the new bounds first.
        new Handler(Looper.MainLooper!).PostDelayed(ApplyRendererPolicy, 250);
    }

    private void RefreshSurface()
    {
        Refresh();
        var handler = new Handler(Looper.MainLooper!);
        handler.PostDelayed(Refresh, 100);
        handler.PostDelayed(Refresh, 400);
    }

    private void Refresh()
    {
        try
        {
            if (IsFinishing || IsDestroyed) return;
            Invalidate(Window?.DecorView);
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime { MainView: { } view })
            {
                view.InvalidateMeasure();
                view.InvalidateVisual();
            }
        }
        catch (Exception e)
        {
            global::Android.Util.Log.Warn("Timbratune", $"surface refresh failed: {e.Message}");
        }
    }

    private static void Invalidate(View? view)
    {
        if (view is null) return;
        view.RequestLayout();
        view.Invalidate();
        if (view is not ViewGroup group) return;
        for (var i = 0; i < group.ChildCount; i++) Invalidate(group.GetChildAt(i));
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
