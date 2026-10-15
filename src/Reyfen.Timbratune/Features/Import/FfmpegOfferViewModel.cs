using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Reyfen.Timbratune.Shared;

namespace Reyfen.Timbratune.Features.Import;

/// <summary>
/// "More formats need FFmpeg": the offer over the page (Linux, when no ffmpeg was found). Nothing
/// is downloaded without a click. Download fetches the pinned build with progress and cancel;
/// "use my package manager" shows the command to copy (the app never runs sudo) and re-probes on
/// Done; "not now" is remembered. Closing it any way (✕, Esc, back) is "not now" without
/// remembering, and cancels a download.
/// </summary>
public sealed partial class FfmpegOfferViewModel : ObservableObject, IDisposable
{
    private readonly ImportManager _manager;
    private readonly Action<FfmpegOfferViewModel> _close;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _cancel = new();

    public FfmpegOfferViewModel(ImportManager manager, Action<FfmpegOfferViewModel> close)
    {
        _manager = manager;
        _close = close;
        var download = manager.FfmpegDownload!;
        Question = $"More formats (M4A, Opus, OGG, WMA, video…) need FFmpeg. Download it for Timbratune (about {download.Bytes / 1_000_000.0:F0} MB)?";
        Licence = $"FFmpeg {download.Version} is free software under the {download.Licence} · it runs as its own program, only to decode imports · ";
        LicenceUri = download.LicenceUri;
        SourceUri = download.SourceUri;
        PackageCommand = manager.FfmpegPackageCommand;
    }

    /// <summary>Completes when the offer is gone (whatever was chosen).</summary>
    public Task Closed => _closed.Task;

    public string Question { get; }
    public string Licence { get; }
    public Uri LicenceUri { get; }
    public Uri SourceUri { get; }

    /// <summary>E.g. <c>sudo apt install ffmpeg</c>; null when no known package manager was found.</summary>
    public string? PackageCommand { get; }

    public bool HasPackageCommand => PackageCommand is not null;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAsking))] private bool _isDownloading;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsAsking))] private bool _isShowingCommand;

    /// <summary>0–100 while downloading.</summary>
    [ObservableProperty] private double _progress;

    /// <summary>What happened (a failure, or that the package manager's ffmpeg wasn't found yet); null = nothing to say.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasMessage))] private string? _message;

    public bool IsAsking => !IsDownloading && !IsShowingCommand;
    public bool HasMessage => Message is not null;

    [RelayCommand]
    private async Task DownloadAsync()
    {
        Message = null;
        IsDownloading = true;
        Progress = 0;
        try
        {
            await _manager.InstallFfmpegAsync(new ThrottledProgress(f => Progress = f * 100), _cancel.Token);
            _close(this);
        }
        catch (OperationCanceledException)
        {
            // closed or cancelled: nothing was installed
        }
        catch (HttpRequestException e)
        {
            Message = e.StatusCode is { } status
                ? $"couldn't download FFmpeg 🌧️ the server answered {(int)status} · try again later"
                : "couldn't download FFmpeg 🌧️ no connection? try again later";
        }
        catch (Exception e)
        {
            Message = $"couldn't get FFmpeg 🌧️ {e.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload() => _cancel.Cancel();

    [RelayCommand]
    private void UsePackageManager()
    {
        Message = null;
        IsShowingCommand = true;
    }

    /// <summary>After installing with the package manager: look again.</summary>
    [RelayCommand]
    private async Task PackageManagerDoneAsync()
    {
        if (await _manager.RefreshDecodersAsync()) _close(this);
        else Message = "ffmpeg isn't there yet · once the command has finished, press done again";
    }

    [RelayCommand]
    private void Back()
    {
        Message = null;
        IsShowingCommand = false;
    }

    /// <summary>Not now, and don't ask again unasked.</summary>
    [RelayCommand]
    private void NotNow()
    {
        _manager.DeclineFfmpeg();
        _close(this);
    }

    [RelayCommand]
    private void Close() => _close(this);

    /// <summary>The overlay host disposes what it closes.</summary>
    public void Dispose()
    {
        _cancel.Cancel();
        _closed.TrySetResult();
    }
}
