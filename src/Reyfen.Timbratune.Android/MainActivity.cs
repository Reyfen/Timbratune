using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Avalonia.Android;

namespace Reyfen.Timbratune.Android;

[Activity(
    Label = "Timbratune",
    Theme = "@style/TimbratuneTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode
                           | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public sealed class MainActivity : AvaloniaMainActivity
{
    private const int MicrophoneRequest = 1001;

    private TaskCompletionSource<bool>? _microphone;

    /// <summary>
    /// The live Activity. The app and its services are built once per process (<see cref="AndroidApp"/>),
    /// but the Activity can be re-created (when the surface is replaced, see WatchSurface), so
    /// anything needing it (permission requests, the share sheet) goes through this rather than
    /// holding on to the first one.
    /// </summary>
    internal static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
        global::Android.Util.Log.Info("Timbratune", $"started: vulkan wanted: {AndroidApp.VulkanWanted}, multi-window: {IsInMultiWindowMode}");
        WatchSurface();
#if PROFILING
        if (Avalonia.Application.Current is { } app)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Diagnostics.Perf.ApplyExperiments(app,
                // This Activity's own view: asking MainViewFactory again would take the view out of this window.
                Content is Avalonia.Controls.Control v ? Avalonia.Controls.TopLevel.GetTopLevel(v) : null));
        Diagnostics.Perf.StartStallWatch();
        if (Diagnostics.Perf.Flags.Contains("fftbench")) System.Threading.Tasks.Task.Run(FftBench.Run);
        Diagnostics.Perf.Note($"started on {Build.Model}, Android {Build.VERSION.Release}, {System.Environment.ProcessorCount} cores");
#endif
    }

    // Vulkan and a replaced surface. Entering (or leaving) split screen replaces the SurfaceView's
    // Surface: a new native window, and the old one's buffer queue is abandoned. Avalonia's
    // Vulkan display (11.3.22, and the same code in 12.1.4) keeps its VkSurfaceKHR from the old
    // window and only re-creates the swapchain on it, every frame, against a dead window: logcat
    // fills with "BufferQueue has been abandoned" and the pane stays black, however it is resized
    // or repainted (a forced full repaint and a layout refresh were tried). Seen on a Pixel 9
    // (Android 17); the API 35 emulator never shows it, not even on 11.3 without this watcher, so
    // whether 12 still needs it can only be checked on such a phone. A new view builds a new Vulkan
    // surface from the current window, so when the surface is replaced the Activity is replaced by
    // a new one (about half a second, with a flash). The process, recorder and view models keep
    // running; the main view is the same instance (App hands every Activity the same one),
    // attached to the new window.
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
        if (IsFinishing || IsDestroyed || !AndroidApp.VulkanWanted) return;
        // Never in a loop: at most one re-creation a second.
        var now = System.Environment.TickCount64;
        if (now - s_lastSurfaceRecreate < 1000) return;
        s_lastSurfaceRecreate = now;
        global::Android.Util.Log.Info("Timbratune", "surface replaced under Vulkan: re-creating the window");
        // Not Recreate(): Android relaunches into the old window (the same DecorView), and that
        // preserved window lays the new content out inside the system bars while Avalonia still
        // pads for them (edge-to-edge is enforced from Android 15): white bars and a page pushed
        // down by the status bar's height (seen on Avalonia 12, Android 15). A new instance gets
        // a new window. The view is handed over first: it can only have one parent at a time.
        Content = null;
        StartActivity(new Intent(this, typeof(MainActivity)).AddFlags(ActivityFlags.NoAnimation));
        Finish();
        if (OperatingSystem.IsAndroidVersionAtLeast(34)) OverrideActivityTransition(OverrideTransition.Close, 0, 0);
        else OverridePendingTransition(0, 0);
    }

    protected override void OnDestroy()
    {
        if (Current == this) Current = null;
        base.OnDestroy();
    }

    /// <summary>Asks for the microphone the first time; true when recording is allowed.</summary>
    internal Task<bool> RequestMicrophoneAsync()
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
