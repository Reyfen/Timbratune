using System.Runtime.InteropServices;
using AVFoundation;
using Foundation;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Platform;
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

/// <summary>
/// The iPhone or iPad: iOS's version ("18.1") and the hardware model ("iPhone17,1"); UIDevice
/// only says "iPhone", so the model comes from sysctl hw.machine.
/// </summary>
internal sealed class IosDeviceInfo : IDeviceInfo
{
    private static readonly string? Machine = ReadMachine();

    public DeviceDescription Describe() => new()
    {
        Os = "ios",
        OsVersion = SystemDeviceInfo.Clean(UIDevice.CurrentDevice.SystemVersion),
        Manufacturer = "Apple",
        Model = Machine,
    };

    [DllImport(ObjCRuntime.Constants.SystemLibrary)]
    private static extern int sysctlbyname(string name, IntPtr value, ref nint length, IntPtr newValue, nint newLength);

    private static string? ReadMachine()
    {
        try
        {
            nint length = 0;
            if (sysctlbyname("hw.machine", IntPtr.Zero, ref length, IntPtr.Zero, 0) != 0 || length <= 0) return null;
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                return sysctlbyname("hw.machine", buffer, ref length, IntPtr.Zero, 0) == 0
                    ? SystemDeviceInfo.Clean(Marshal.PtrToStringAnsi(buffer))
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}
