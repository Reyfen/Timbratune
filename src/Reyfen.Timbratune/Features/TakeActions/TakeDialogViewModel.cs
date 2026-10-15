using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Core.Models;

namespace Reyfen.Timbratune.Features.TakeActions;

public enum TakeDialogKind { SaveMenu, Rename, Delete }

/// <summary>
/// A take's dialog over the page: the save / export (or, on a phone, share) choices, renaming,
/// or the delete confirmation. Closing it is cancelling; actions close it and then run, and
/// report on the take's card (<see cref="TakeActionsManager.ReportStatus"/>).
/// </summary>
public sealed partial class TakeDialogViewModel : ObservableObject
{
    private readonly TakeActionsManager _manager;
    private readonly Action<TakeDialogViewModel> _close;

    public TakeDialogViewModel(Recording take, TakeDialogKind kind, TakeActionsManager manager, Action<TakeDialogViewModel> close)
    {
        Take = take;
        _manager = manager;
        _close = close;
        HasAudio = manager.AudioPathOf(take) is not null;
        Show(kind);
    }

    public Recording Take { get; }
    public int Id => Take.Id;
    public string DialogTitle => $"#{Id} · {Take.Label}";
    public bool HasAudio { get; }

    public bool CanShare => _manager.CanShare;
    public string MenuTitle => CanShare ? "share" : "save or export";
    public string AudioChoice => CanShare ? "🎵 share audio (.wav)" : "🎵 save audio (.wav)";
    public string DataChoice => CanShare ? "📦 share data (.tmbr)" : "📦 export data (.tmbr)";
    public string PdfChoice => CanShare ? "📄 share PDF · coming soon" : "📄 export PDF · coming soon";

    [ObservableProperty] private bool _isSaveMenuOpen;
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private bool _isConfirmingDelete;
    [ObservableProperty] private string _renameText = "";

    public string ConfirmText => $"delete take #{Id}? this can't be undone" +
                                 (HasAudio ? " — you can save a copy of the audio first if you want to keep it." : ".");

    /// <summary>Switches the dialog to another of its kinds (one dialog per take at a time).</summary>
    public void Show(TakeDialogKind kind)
    {
        IsSaveMenuOpen = kind == TakeDialogKind.SaveMenu;
        IsConfirmingDelete = kind == TakeDialogKind.Delete;
        if (kind == TakeDialogKind.Rename) RenameText = Take.Label;
        IsRenaming = kind == TakeDialogKind.Rename;
    }

    /// <summary>Closes the dialog, as cancel.</summary>
    [RelayCommand]
    private void CloseDialog() => _close(this);

    [RelayCommand] private void CancelRename() => _close(this);

    [RelayCommand] private void CancelDelete() => _close(this);

    /// <summary>The audio: a copy saved through the file picker, or shared.</summary>
    [RelayCommand]
    private async Task SaveCopyAsync()
    {
        _close(this);
        try
        {
            await _manager.SaveAudioAsync(Take);
        }
        catch (Exception ex)
        {
            _manager.ReportStatus(Id, $"couldn't {(CanShare ? "share" : "save")} the audio 🌧️ {ex.Message}");
        }
    }

    /// <summary>The .tmbr file: saved through the file picker, or shared.</summary>
    [RelayCommand]
    private async Task ExportDataAsync()
    {
        _close(this);
        if (!HasAudio) return;
        _manager.ReportStatus(Id, CanShare ? "preparing the file to share…" : "preparing the export…", busy: true);
        try
        {
            var where = await _manager.ExportDataAsync(Take);
            _manager.ReportStatus(Id, where is null ? null : $"exported to {Path.GetFileName(where)} ✨");
        }
        catch (Exception ex)
        {
            _manager.ReportStatus(Id, $"couldn't {(CanShare ? "share" : "export")} this take 🌧️ {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ConfirmRenameAsync()
    {
        var label = RenameText.Trim();
        _close(this);
        if (label.Length == 0 || label == Take.Label) return;
        try
        {
            await _manager.RenameAsync(Take, label);
        }
        catch (Exception ex)
        {
            _manager.ReportStatus(Id, $"couldn't rename this take 🌧️ {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        _close(this);
        await _manager.DeleteAsync(Take);
    }
}
