namespace Reyfen.Timbratune.Shared.Platform;

/// <summary>Run-time permissions the platform may ask the person for (Android).</summary>
public interface IPermissions
{
    /// <summary>Asks for the microphone where that is needed; true when recording may start.</summary>
    Task<bool> RequestMicrophoneAsync();
}

/// <summary>Platforms that don't ask at run time (desktops): everything is granted.</summary>
public sealed class NoRuntimePermissions : IPermissions
{
    public Task<bool> RequestMicrophoneAsync() => Task.FromResult(true);
}
