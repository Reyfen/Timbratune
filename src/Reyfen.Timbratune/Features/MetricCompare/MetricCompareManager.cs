using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.Core.Analysis.Metrics;
using Reyfen.Timbratune.Core.Json;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Services;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Navigation;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Features.MetricCompare;

/// <summary>
/// What the comparison needs from outside: the reference voices (only in builds with them, read
/// once) and the audio files to play, takes' and references'.
/// </summary>
public sealed class MetricCompareManager(DataPaths paths, ReferenceVoiceFolder folder, IFeatureFlags flags)
{
    private IReadOnlyList<ReferenceVoice>? _references;

    public bool ShowsReferenceVoices => flags.ReferenceVoices;

    public IReadOnlyList<ReferenceVoice> References => _references ??= flags.ReferenceVoices && folder.Path is { } dir ? Load(dir) : [];

    public string? TakeAudio(Recording take) => Existing(paths.Resolve(take.Audio));

    public string? ReferenceAudio(ReferenceVoice voice) =>
        voice.Audio is { } audio && folder.Path is { } dir ? Existing(Path.Combine(dir, audio)) : null;

    private static string? Existing(string? path) => path is not null && File.Exists(path) ? path : null;

    private static IReadOnlyList<ReferenceVoice> Load(string dir)
    {
        // Missing or broken reference.json just means no reference ticks.
        try
        {
            var path = Path.Combine(dir, "reference.json");
            return File.Exists(path) ? TimbratuneJson.ReadReferences(File.ReadAllText(path, Encoding.UTF8)) : [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}

/// <summary>Opens a metric's comparison over the page (one at a time).</summary>
public sealed class MetricCompareRouter(IOverlayHost overlays, MetricCompareManager manager, ITakeCatalog catalog,
    IActiveTakeService active, PlaybackService playback, MetricRegistry registry) : RouterBase(overlays), IMetricCompareLauncher
{
    public void Open(MetricId metric)
    {
        CloseAll<MetricModalViewModel>();
        MetricModalViewModel? modal = null;
        modal = new MetricModalViewModel(registry[metric], catalog.Takes, manager.References, active.Active?.Id,
            manager.TakeAudio, manager.ReferenceAudio, playback, manager.ShowsReferenceVoices, () => Close(modal!));
        Open(modal);
    }
}

public static class MetricCompareFeature
{
    public static IServiceCollection AddMetricCompareFeature(this IServiceCollection services)
    {
        services.AddSingleton<MetricCompareManager>();
        services.AddSingleton<MetricCompareRouter>();
        services.AddSingleton<IMetricCompareLauncher>(sp => sp.GetRequiredService<MetricCompareRouter>());
        return services;
    }
}
