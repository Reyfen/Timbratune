using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Platform;
using Reyfen.Timbratune.Shared.Platform;

namespace Reyfen.Timbratune.Android;

/// <summary>The microphone permission, asked through the Activity in front (it can be re-created).</summary>
internal sealed class AndroidPermissions : IPermissions
{
    public Task<bool> RequestMicrophoneAsync() => MainActivity.Current?.RequestMicrophoneAsync() ?? Task.FromResult(false);
}

/// <summary>Android's background priority, for the live analysis thread.</summary>
internal sealed class AndroidThreadPriority : IThreadPriority
{
    public void LowerCurrentThread() => global::Android.OS.Process.SetThreadPriority(global::Android.OS.ThreadPriority.Background);
}

/// <summary>The phone: Android's version ("15"), its maker and model as Android names them ("Google", "Pixel 9").</summary>
internal sealed class AndroidDeviceInfo : IDeviceInfo
{
    public DeviceDescription Describe() => new()
    {
        Os = "android",
        OsVersion = SystemDeviceInfo.Clean(global::Android.OS.Build.VERSION.Release),
        Manufacturer = SystemDeviceInfo.Clean(global::Android.OS.Build.Manufacturer),
        Model = SystemDeviceInfo.Clean(global::Android.OS.Build.Model),
    };
}
