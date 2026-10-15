using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Services;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Navigation;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.ViewModels;

namespace Reyfen.Timbratune.App;

/// <summary>
/// Composition: what the app is made of. A head calls <see cref="AddTimbratune"/>, adds its
/// platform adapters, builds the container and passes it to <see cref="TimbratuneApp"/>.
/// Constructor injection only, no assembly scanning; everything here is a singleton.
/// </summary>
public static class CompositionRoot
{
    /// <summary>
    /// Registers Core, Shared and the UI. The head must register:
    /// <list type="bullet">
    /// <item><see cref="DataPaths"/>: where the takes live;</item>
    /// <item><see cref="IAudioRecorder"/> and <see cref="IAudioPlayer"/>;</item>
    /// <item><see cref="IPlatformInfo"/>.</item>
    /// </list>
    /// It may register <see cref="IAudioDecoder"/> (MP3/FLAC import), <see cref="IFileSharer"/>
    /// (a share sheet), and replace the defaults of <see cref="IPermissions"/>,
    /// <see cref="IThreadPriority"/> and <see cref="ReferenceVoiceFolder"/> (before or after this call).
    /// </summary>
    public static IServiceCollection AddTimbratune(this IServiceCollection services)
    {
        // Core
        services.TryAddSingleton<IAnalysisEngine, AcousticsAnalysisEngine>();
        services.TryAddSingleton(sp => new RecordingStore(sp.GetRequiredService<DataPaths>()));
        services.TryAddSingleton(sp => new TakeImporter(sp.GetRequiredService<RecordingStore>(),
            sp.GetRequiredService<IAnalysisEngine>(), sp.GetService<IAudioDecoder>()));

        // Shared
        services.TryAddSingleton<IMessenger>(_ => new WeakReferenceMessenger());
        services.TryAddSingleton<IOverlayHost, OverlayHost>();
        services.TryAddSingleton<IFeatureFlags, BuildFeatureFlags>();
        services.TryAddSingleton<IPermissions, NoRuntimePermissions>();
        services.TryAddSingleton<IThreadPriority, DefaultThreadPriority>();
        services.TryAddSingleton(ReferenceVoiceFolder.None);
        services.TryAddSingleton<StorageProviderDialogs>();
        services.TryAddSingleton<IFileDialogs>(sp => sp.GetRequiredService<StorageProviderDialogs>());
        services.TryAddSingleton(sp => new PlaybackService(sp.GetRequiredService<IAudioPlayer>()));

        // UI
        services.TryAddSingleton<ViewLocator>();
        services.TryAddSingleton<MainViewModel>();
        return services;
    }
}
