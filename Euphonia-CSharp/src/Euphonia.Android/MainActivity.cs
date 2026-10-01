using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Euphonia.Audio.SoundFlow;
using Euphonia.Core.Analysis;
using Euphonia.Core.Audio;
using Euphonia.Core.Storage;
using Euphonia.Services;

namespace Euphonia.Android;

[Activity(
    Label = "Euphonia",
    Theme = "@style/EuphoniaTheme",
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
        s_audio ??= new SoundFlowAudio();
        s_recorder ??= FakeMic() is { } fakeMic ? new FileReplayRecorder(fakeMic) : s_audio.CreateRecorder();
        var audio = s_audio;
        var recorder = s_recorder;
        App.ServicesFactory = dialogs => new AppServices(
            Store: new RecordingStore(DataPaths.Default()),
            Engine: new AcousticsAnalysisEngine(),
            Recorder: recorder,
            Playback: new PlaybackService(audio.CreatePlayer()),
            Dialogs: dialogs,
            RequestMicrophone: RequestMicrophoneAsync);
        base.OnCreate(savedInstanceState);
    }

    /// <summary>
    /// Debug builds only: files/fake-mic.wav in the app's private folder replays as if it were
    /// being spoken (the desktop's EUPHONIA_FAKE_MIC). Put it there with
    /// <c>adb push take.wav /data/local/tmp/ &amp;&amp; adb shell run-as app.euphonia cp /data/local/tmp/take.wav files/fake-mic.wav</c>.
    /// (debug.mono.env can't carry it: .NET for Android 36.1 aborts on any value there.)
    /// </summary>
    private string? FakeMic()
    {
#if DEBUG
        var path = System.IO.Path.Combine(FilesDir!.AbsolutePath, "fake-mic.wav");
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
                new EmbeddedFontCollection(new Uri("fonts:EuphoniaEmoji"), new Uri("avares://Euphonia.Android/Assets/Fonts"))))
            .With(new FontManagerOptions
            {
                FontFallbacks = [new FontFallback { FontFamily = new FontFamily("fonts:EuphoniaEmoji#Noto Color Emoji") }],
            });

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
