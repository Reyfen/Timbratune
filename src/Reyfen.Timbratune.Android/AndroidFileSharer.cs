using Android.App;
using Android.Content;
using Reyfen.Timbratune.Shared.Platform;

namespace Reyfen.Timbratune.Android;

/// <summary>
/// The Android share sheet: the file is written to cache/share/ and handed to the chosen app as
/// a content:// URI from <see cref="ShareFileProvider"/>, with read permission for that app only.
/// </summary>
internal sealed class AndroidFileSharer(Func<Activity?> currentActivity) : IFileSharer
{
    // Shared files stay a while (the receiving app may still be uploading one), then go.
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(1);

    public async Task ShareAsync(string title, string fileName, string mimeType, Func<Stream, Task> write)
    {
        var activity = currentActivity() ?? throw new InvalidOperationException("the app isn't in front");
        var dir = Path.Combine(activity.CacheDir!.AbsolutePath, "share");
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.EnumerateFiles(dir))
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(old) > KeepFor) File.Delete(old);

        var path = Path.Combine(dir, fileName);
        await using (var target = File.Create(path))
            await write(target);

        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, ShareFileProvider.AuthorityOf(activity), new Java.IO.File(path))!;
        var send = new Intent(Intent.ActionSend);
        send.SetType(mimeType);
        send.PutExtra(Intent.ExtraStream, uri);
        // ClipData too: that's what carries the read permission to the app picked in the chooser.
        send.ClipData = ClipData.NewRawUri(fileName, uri);
        send.AddFlags(ActivityFlags.GrantReadUriPermission);
        (currentActivity() ?? activity).StartActivity(Intent.CreateChooser(send, title));
    }
}

/// <summary>
/// Serves cache/share/ (Resources/xml/share_paths.xml) to the app a file is shared with.
/// Declared here rather than in the manifests, so the normal and the profiling manifest both get it.
/// The authority follows the app id (the build fills in ${applicationId}), so a build with another
/// id (e.g. the research and profiling builds, tools/phone-bench) installs next to the app.
/// </summary>
[ContentProvider(["${applicationId}.fileprovider"], Exported = false, GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/share_paths")]
internal sealed class ShareFileProvider : AndroidX.Core.Content.FileProvider
{
    public static string AuthorityOf(Context context) => context.PackageName + ".fileprovider";
}
