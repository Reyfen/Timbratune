using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Diagnostics;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Diagnostics;
using Reyfen.Timbratune.Features.About;
using Reyfen.Timbratune.Features.CheatSheet;
using Reyfen.Timbratune.Features.Import;
using Reyfen.Timbratune.Features.LiveMonitor;
using Reyfen.Timbratune.Features.MetricCompare;
using Reyfen.Timbratune.Features.Record;
using Reyfen.Timbratune.Features.TakeActions;
using Reyfen.Timbratune.Features.TakeDetails;
using Reyfen.Timbratune.Features.TakeLibrary;
using Reyfen.Timbratune.Services;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Navigation;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shared.State;
using Reyfen.Timbratune.Shared.Telemetry;
using Reyfen.Timbratune.Shell;

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
    /// It may add <see cref="IAudioDecoderProvider"/>s (miniaudio, the OS's decoders, FFmpeg; the
    /// managed WAV/AIFF one is always there), register <see cref="IFileSharer"/>
    /// (a share sheet), and replace the defaults of <see cref="IPermissions"/>,
    /// <see cref="IThreadPriority"/>, <see cref="ReferenceVoiceFolder"/> and <see cref="IDataStore"/>
    /// (the app's own files, such as settings; default: the data folder's root).
    /// Telemetry is off by default (REFACTOR-PLAN §5): <see cref="IAppLog"/>, <see cref="IAnalytics"/>
    /// and <see cref="ICrashReporter"/> are do-nothing defaults until an adapter replaces them, and
    /// an adapter is only created when <see cref="TelemetryConsent"/> says this install sends.
    /// </summary>
    public static IServiceCollection AddTimbratune(this IServiceCollection services)
    {
        // Core
        services.TryAddSingleton<IAppLog>(NullAppLog.Instance);
        services.TryAddSingleton(MetricRegistry.Default);
        services.TryAddSingleton<IAnalysisScheduler>(sp =>
            new NativeAnalysisScheduler(LiveCores(), sp.GetRequiredService<IThreadPriority>().LowerCurrentThread));
        services.TryAddSingleton<IAnalysisEngine>(sp => new AcousticsAnalysisEngine(sp.GetRequiredService<IAnalysisScheduler>()));
        services.TryAddSingleton(sp => new RecordingStore(sp.GetRequiredService<DataPaths>()));
        services.AddSingleton<IAudioDecoderProvider, ManagedDecoderProvider>();
        services.TryAddSingleton(sp => new DecoderRegistry(sp.GetServices<IAudioDecoderProvider>(), sp.GetRequiredService<IAppLog>()));
        services.TryAddSingleton(sp => new TakeImportService(sp.GetRequiredService<RecordingStore>(),
            sp.GetRequiredService<IAnalysisEngine>(), sp.GetRequiredService<DecoderRegistry>()));
        services.TryAddSingleton<IDataStore>(sp => new FileSystemDataStore(sp.GetRequiredService<DataPaths>().Root));
        services.TryAddSingleton<ISettingsStore>(sp => new DataStoreSettingsStore(sp.GetRequiredService<IDataStore>()));
        services.TryAddSingleton<ISettings>(sp => new SettingsService(sp.GetRequiredService<ISettingsStore>(), sp.GetServices<ISettingKey>(),
            log: sp.GetRequiredService<IAppLog>()));

        // Shared
        services.TryAddSingleton<IMessenger>(_ => new WeakReferenceMessenger());
        services.TryAddSingleton<IOverlayHost, OverlayHost>();
        services.TryAddSingleton<ITakeCatalog>(sp => new TakeCatalog(sp.GetRequiredService<RecordingStore>()));
        services.TryAddSingleton<IActiveTakeService>(sp => new ActiveTakeService(sp.GetRequiredService<ITakeCatalog>()));
        services.TryAddSingleton<IFeatureFlags, BuildFeatureFlags>();
        services.TryAddSingleton<IPermissions, NoRuntimePermissions>();
        services.TryAddSingleton<IThreadPriority, DefaultThreadPriority>();
        services.TryAddSingleton(ReferenceVoiceFolder.None);
        services.TryAddSingleton<StorageProviderDialogs>();
        services.TryAddSingleton<IFileDialogs>(sp => sp.GetRequiredService<StorageProviderDialogs>());
        services.TryAddSingleton(sp => new PlaybackService(sp.GetRequiredService<IAudioPlayer>()));

        // Telemetry (§5): consent and rollout, and the do-nothing defaults
        services.TryAddSingleton<IEntitlements>(NoEntitlements.Instance);
        services.TryAddSingleton(RolloutRates.Default);
        services.TryAddSingleton<TelemetryConsent>();
        services.TryAddSingleton<IAnalytics>(NullAnalytics.Instance);
        services.TryAddSingleton<ICrashReporter>(NullCrashReporter.Instance);
        services.AddSetting(TelemetrySettings.Analytics);
        services.AddSetting(TelemetrySettings.CrashReports);
        services.AddSetting(TelemetrySettings.InstallId);

        // Features (each registers its own view models, managers, routers and settings)
        services.AddCheatSheetFeature();
        services.AddAboutFeature();
        services.AddTakeActionsFeature();
        services.AddMetricCompareFeature();
        services.AddImportFeature();
        services.AddTakeLibraryFeature();
        services.AddTakeDetailsFeature();
        services.AddRecordingFeature();
        services.AddLiveMonitorFeature();

        // Shell
        services.AddSetting(ShellSettings.Theme);
        services.TryAddSingleton<ViewLocator>();
        services.TryAddSingleton<ShellViewModel>();
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
