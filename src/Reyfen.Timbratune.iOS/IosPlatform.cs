using AVFoundation;
using Foundation;
using Reyfen.Timbratune.Shared.Platform;
using UIKit;

namespace Reyfen.Timbratune.iOS;

/// <summary>The app's folders in its sandbox.</summary>
internal static class IosFolders
{
    /// <summary>Documents: the takes. The Files app shows it (UIFileSharingEnabled in Info.plist).</summary>
    public static string Documents => Folder(NSSearchPathDirectory.DocumentDirectory);

    /// <summary>Library/Application Support: the settings. Backed up, but not shown to the person.</summary>
    public static string ApplicationSupport => Folder(NSSearchPathDirectory.ApplicationSupportDirectory);

    /// <summary>Library/Caches: may be emptied by iOS when space is short.</summary>
    public static string Caches => Folder(NSSearchPathDirectory.CachesDirectory);

    private static string Folder(NSSearchPathDirectory which)
    {
        var path = NSFileManager.DefaultManager.GetUrls(which, NSSearchPathDomain.User)[0].Path!;
        Directory.CreateDirectory(path);
        return path;
    }
}

/// <summary>
/// The audio session: record and play back at once (playback through the loudspeaker, not the
/// earpiece), in measurement mode so iOS applies no automatic gain, noise suppression or echo
/// cancellation to the voice being analysed. Bluetooth headsets aren't offered for input: their
/// microphones run at 8-16 kHz (HFP), too little for the formants.
/// </summary>
internal static class IosAudioSession
{
    public static void Configure()
    {
        var session = AVAudioSession.SharedInstance();
        if (session.SetCategory(AVAudioSessionCategory.PlayAndRecord,
                AVAudioSessionCategoryOptions.DefaultToSpeaker | AVAudioSessionCategoryOptions.AllowBluetoothA2DP) is { } error)
            Console.WriteLine($"Timbratune: audio session category: {error.LocalizedDescription}");
        if (!session.SetMode(AVAudioSession.ModeMeasurement, out var modeError))
            Console.WriteLine($"Timbratune: audio session mode: {modeError?.LocalizedDescription}");
        session.SetPreferredSampleRate(44100, out _);
        session.SetActive(true, out _);
    }
}

/// <summary>The microphone permission (asked once; afterwards only Settings can change it).</summary>
internal sealed class IosPermissions : IPermissions
{
    public Task<bool> RequestMicrophoneAsync()
    {
        var session = AVAudioSession.SharedInstance();
        switch (session.RecordPermission)
        {
            case AVAudioSessionRecordPermission.Granted:
                return Task.FromResult(true);
            case AVAudioSessionRecordPermission.Denied:
                return Task.FromResult(false);
        }
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RequestRecordPermission(granted => answer.TrySetResult(granted));
        return answer.Task;
    }
}

/// <summary>
/// The iOS share sheet: the file is written to Caches/share/ and handed to UIActivityViewController
/// (on iPad a popover, anchored to the middle of the window).
/// </summary>
internal sealed class IosFileSharer : IFileSharer
{
    // Shared files stay a while (the receiving app may still be reading one), then go.
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(1);

    public async Task ShareAsync(string title, string fileName, string mimeType, Func<Stream, Task> write)
    {
        var dir = Path.Combine(IosFolders.Caches, "share");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.EnumerateFiles(dir))
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(old) > KeepFor) File.Delete(old);

        var path = Path.Combine(dir, fileName);
        await using (var target = File.Create(path))
            await write(target);

        var presenter = TopViewController() ?? throw new InvalidOperationException("the app isn't in front");
        var sheet = new UIActivityViewController([NSUrl.FromFilename(path)], null);
        if (sheet.PopoverPresentationController is { } popover && presenter.View is { } view)
        {
            popover.SourceView = view;
            popover.SourceRect = new CoreGraphics.CGRect(view.Bounds.GetMidX(), view.Bounds.GetMidY(), 0, 0);
            popover.PermittedArrowDirections = default;
        }
        presenter.PresentViewController(sheet, true, null);
    }

    private static UIViewController? TopViewController()
    {
        var window = UIApplication.SharedApplication.ConnectedScenes.ToArray()
            .OfType<UIWindowScene>()
            .SelectMany(scene => scene.Windows)
            .FirstOrDefault(w => w.IsKeyWindow);
        var controller = window?.RootViewController;
        while (controller?.PresentedViewController is { } presented) controller = presented;
        return controller;
    }
}
