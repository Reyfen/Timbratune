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
