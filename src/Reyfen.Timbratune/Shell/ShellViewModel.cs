using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Features.About;
using Reyfen.Timbratune.Features.Import;
using Reyfen.Timbratune.Features.LiveMonitor;
using Reyfen.Timbratune.Features.Record;
using Reyfen.Timbratune.Features.TakeDetails;
using Reyfen.Timbratune.Features.TakeLibrary;
using Reyfen.Timbratune.Shared.Navigation;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Shell;

/// <summary>
/// App.tsx: the single scrolling page. Places the features (each has its own view model) and
/// fills the record panel's slots; holds the overlay stack (Esc and Android's back close its
/// front), the theme toggle and the start-up.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly ITakeCatalog _catalog;
    private readonly IActiveTakeService _activeTake;
    private readonly ISettings _settings;

    public ShellViewModel(RecordViewModel record, LiveMonitorViewModel liveMonitor, TakeDetailsViewModel details,
        TakeLibraryViewModel library, ImportViewModel import, AboutViewModel about, IOverlayHost overlays,
        ITakeCatalog catalog, IActiveTakeService activeTake, ILiveSession live, ISettings settings)
    {
        Record = record;
        LiveMonitor = liveMonitor;
        Details = details;
        Library = library;
        Import = import;
        About = about;
        Overlays = overlays;
        _catalog = catalog;
        _activeTake = activeTake;
        _settings = settings;
        record.IdleActions = import;
        record.WindowPicker = liveMonitor;
        record.Footer = import.Status;
        live.Started += (_, _) => IsLive = true;
        live.Ended += (_, _) => IsLive = false;
    }

    public RecordViewModel Record { get; }
    public LiveMonitorViewModel LiveMonitor { get; }
    public TakeDetailsViewModel Details { get; }
    public TakeLibraryViewModel Library { get; }
    public ImportViewModel Import { get; }
    public AboutViewModel About { get; }
    public IOverlayHost Overlays { get; }

    /// <summary>A take is being recorded (or analyzed after Stop): the live views replace the take in focus.</summary>
    [ObservableProperty] private bool _isLive;

    /// <summary>First load: the takes, then any audio or .tmbr files dropped into the takes folder.</summary>
    public async Task StartAsync()
    {
        _activeTake.SelectLatest();
        await _catalog.ReloadAsync();
        await Import.StartAsync();
    }

    /// <summary>Esc and Android's back: closes what is open over the page, in front first.</summary>
    [RelayCommand]
    private void CloseOverlay() => Overlays.CloseTop();

    [RelayCommand]
    private void ToggleTheme()
    {
        var app = Avalonia.Application.Current;
        if (app is null) return;
        app.RequestedThemeVariant = app.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;
        _settings.Set(ShellSettings.Theme, ShellSettings.NameOf(app.RequestedThemeVariant));
    }
}
