using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.Core.Audio;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Services;
using Reyfen.Timbratune.Shared.Messaging;
using Reyfen.Timbratune.Shared.Navigation;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Features.TakeLibrary;

/// <summary>The takes on disk, as the cards need them: the audio file to play and its waveform.</summary>
public sealed class TakeLibraryManager(DataPaths paths)
{
    /// <summary>The take's audio file, when it is there.</summary>
    public string? AudioPathOf(Recording take) => paths.Resolve(take.Audio) is { } path && File.Exists(path) ? path : null;

    /// <summary>The waveform's peaks (400 bins), read off the UI thread.</summary>
    public Task<float[]> PeaksAsync(string audioPath) => Task.Run(() => WaveformPeaks.FromWav(audioPath, 400));
}

/// <summary>
/// The takes: the switcher's chips ("viewing: #3 · …") and the cards under "All recordings",
/// newest first. Picking one shows it above (<see cref="IActiveTakeService"/>).
/// </summary>
public sealed partial class TakeLibraryViewModel : ObservableObject
{
    private readonly ITakeCatalog _catalog;
    private readonly IActiveTakeService _active;
    private readonly PlaybackService _playback;
    private readonly ITakeActionsLauncher _actions;
    private readonly IPlatformInfo _platform;
    private readonly TakeLibraryManager _manager;

    public TakeLibraryViewModel(ITakeCatalog catalog, IActiveTakeService active, PlaybackService playback,
        ITakeActionsLauncher actions, IPlatformInfo platform, TakeLibraryManager manager, IMessenger messenger)
    {
        _catalog = catalog;
        _active = active;
        _playback = playback;
        _actions = actions;
        _platform = platform;
        _manager = manager;
        catalog.Changed += (_, _) => Rebuild();
        active.Changed += (_, _) => MarkActive();
        messenger.Register<TakeLibraryViewModel, TakeActionStatus>(this, (vm, status) =>
        {
            foreach (var card in vm.Items)
                if (card.Id == status.TakeId) card.Apply(status);
        });
    }

    /// <summary>Newest first, for the switcher and "All recordings".</summary>
    public ObservableCollection<TakeCardViewModel> Items { get; } = [];

    public bool ShowSwitcher => Items.Count > 1;
    public bool NoRecordings => Items.Count == 0;

    /// <summary>What went wrong reading the takes folder, if anything.</summary>
    public string? LoadError => _catalog.LoadError;

    public bool HasLoadError => LoadError is not null;

    /// <summary>Anything to show above the take (an error or the switcher): otherwise the area takes no space.</summary>
    public bool ShowsTop => ShowSwitcher || HasLoadError;

    private void Rebuild()
    {
        foreach (var card in Items) card.Dispose();
        Items.Clear();
        var takes = _catalog.Takes;
        var latestId = _catalog.Latest?.Id;
        for (var i = takes.Count - 1; i >= 0; i--)
        {
            var r = takes[i];
            Items.Add(new TakeCardViewModel(r, _manager.AudioPathOf(r), r.Id == latestId, _playback, _actions, _platform.CanShare,
                card => _active.Select(card.Id), _manager.PeaksAsync));
        }
        OnPropertyChanged(nameof(ShowSwitcher));
        OnPropertyChanged(nameof(NoRecordings));
        OnPropertyChanged(nameof(LoadError));
        OnPropertyChanged(nameof(HasLoadError));
        OnPropertyChanged(nameof(ShowsTop));
        MarkActive();
    }

    private void MarkActive()
    {
        var active = _active.Active?.Id;
        foreach (var card in Items) card.IsActive = card.Id == active;
    }
}

public static class TakeLibraryFeature
{
    public static IServiceCollection AddTakeLibraryFeature(this IServiceCollection services)
    {
        services.AddSingleton<TakeLibraryManager>();
        services.AddSingleton<TakeLibraryViewModel>();
        return services;
    }
}
