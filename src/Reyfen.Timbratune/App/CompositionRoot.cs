using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Diagnostics;
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
    /// <see cref="IThreadPriority"/>, <see cref="ReferenceVoiceFolder"/> and <see cref="IDataStore"/>
    /// (the app's own files, such as settings; default: the data folder's root).
    /// </summary>
    public static IServiceCollection AddTimbratune(this IServiceCollection services)
    {
        // Core
        services.TryAddSingleton<IAnalysisScheduler>(sp =>
            new NativeAnalysisScheduler(LiveCores(), sp.GetRequiredService<IThreadPriority>().LowerCurrentThread));
        services.TryAddSingleton<IAnalysisEngine>(sp => new AcousticsAnalysisEngine(sp.GetRequiredService<IAnalysisScheduler>()));
        services.TryAddSingleton(sp => new RecordingStore(sp.GetRequiredService<DataPaths>()));
        services.TryAddSingleton(sp => new TakeImporter(sp.GetRequiredService<RecordingStore>(),
            sp.GetRequiredService<IAnalysisEngine>(), sp.GetService<IAudioDecoder>()));
        services.TryAddSingleton<IDataStore>(sp => new FileSystemDataStore(sp.GetRequiredService<DataPaths>().Root));
        services.TryAddSingleton<ISettingsStore>(sp => new DataStoreSettingsStore(sp.GetRequiredService<IDataStore>()));
        services.TryAddSingleton<ISettings>(sp => new SettingsService(sp.GetRequiredService<ISettingsStore>(), sp.GetServices<ISettingKey>()));

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

        // UI (each feature's settings are registered with it; these move with the features in P3)
        services.AddSetting(ShellSettings.Theme);
        services.AddSetting(RecordingSettings.LiveWindowSeconds);
        services.TryAddSingleton<ViewLocator>();
        services.TryAddSingleton<MainViewModel>();
        return services;
    }

    /// <summary>Registers a setting, so the settings service knows it (and checks it doesn't clash with another).</summary>
    public static IServiceCollection AddSetting(this IServiceCollection services, ISettingKey key)
    {
        services.AddSingleton(key);
        return services;
    }

    /// <summary>
    /// Cores for the live analysis. Profiling builds' switches (HANDOFF §8d): "fullcpu" runs it
    /// on every core at normal priority, "lowprioN" on N cores.
    /// </summary>
    private static int LiveCores()
    {
        if (Perf.Flags.Contains("fullcpu")) return 0;
        foreach (var n in new[] { 1, 2, 3, 4 })
            if (Perf.Flags.Contains("lowprio" + n)) return n;
        return NativeAnalysisScheduler.DefaultLiveCores;
    }
}
