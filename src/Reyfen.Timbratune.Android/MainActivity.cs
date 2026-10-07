using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
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
        var paths = DataPaths.Default();
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
            RequestMicrophone: RequestMicrophoneAsync,
            LowerThreadPriority: () => global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Background));
#if PROFILING
        // "noprobes": keep the fake mic and switches but log nothing (the probes' own cost out of the measurement).
        if (!Diagnostics.Perf.Flags.Contains("noprobes")) Diagnostics.Perf.Sink = line => global::Android.Util.Log.Info("Timbratune", line);
#endif
        base.OnCreate(savedInstanceState);
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
    /// Vulkan first, then OpenGL ES, then software. Measured scrolling: on a Pixel 9 (Android 17,
    /// where OpenGL ES runs through a translation layer on Vulkan) OpenGL ES held it to ~33 fps
    /// with the GPU mostly idle, Vulkan gave ~56 fps; on a Pixel 4a 52 → 56 fps, and live
    /// recording ran smoother on both. If Vulkan can't start, Avalonia falls back to the next.
    /// </summary>
    private static IReadOnlyList<AndroidRenderingMode> RenderingModes()
    {
#if PROFILING
        // A/B switch: "egl" renders through OpenGL ES as before.
        if (Diagnostics.Perf.Flags.Contains("egl")) return [AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
#endif
        return [AndroidRenderingMode.Vulkan, AndroidRenderingMode.Egl, AndroidRenderingMode.Software];
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
