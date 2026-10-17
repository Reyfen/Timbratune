using Avalonia;
using Avalonia.iOS;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.App;
using Reyfen.Timbratune.Audio.SoundFlow;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Shared.Platform;

namespace Reyfen.Timbratune.iOS;

/// <summary>
/// The process: Avalonia builds the app here, so the services (one container for the process:
/// one audio engine and recorder) are set up here too, as on Android.
/// </summary>
[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<TimbratuneApp>
{
    // Lives as long as the process.
    private ServiceProvider? _services;

    protected override AppBuilder CreateAppBuilder()
    {
        _services = BuildServices();
        return AppBuilder.Configure(() => new TimbratuneApp(_services)).UseiOS(this);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        // Apple Color Emoji covers the UI's emoji, so unlike Android no font of our own is needed.
        base.CustomizeAppBuilder(builder).WithInterFont();

    private static ServiceProvider BuildServices()
    {
        MiniaudioFramework.Prepare();
        IosAudioSession.Configure();

        var paths = new DataPaths(IosFolders.Documents);
        var services = new ServiceCollection().AddTimbratune();
        services.AddSingleton(paths);
        services.AddSingleton<IPlatformInfo>(new PlatformInfo
        {
            IsMobile = true,
            CanOpenDataFolder = false,
            DataFolderHint = "Files app › On My iPhone (or iPad) › Timbratune › takes · add or copy takes there",
            CanShare = true,
            CanRunProcesses = false,
        });
        services.AddSingleton(_ => new SoundFlowAudio());
        services.AddSingleton<IAudioDecoderProvider>(sp => new MiniaudioDecoderProvider(sp.GetRequiredService<SoundFlowAudio>));
        services.AddSingleton<IAudioRecorder>(sp => sp.GetRequiredService<SoundFlowAudio>().CreateRecorder());
        services.AddSingleton<IAudioPlayer>(sp => sp.GetRequiredService<SoundFlowAudio>().CreatePlayer());
        services.AddSingleton<IPermissions, IosPermissions>();
        services.AddSingleton<IFileSharer, IosFileSharer>();
        // Settings in Library/Application Support, not with the takes in Documents (which the Files app shows).
        services.AddSingleton<IDataStore>(new FileSystemDataStore(IosFolders.ApplicationSupport));
        return services.BuildServiceProvider();
    }
}
