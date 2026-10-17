using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Threading;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Platform;

namespace Reyfen.Timbratune.Features.About;

/// <summary>
/// The page's footer: where the takes live (a link that opens the folder, or on Android where
/// to find it), the version and the credits, and FFmpeg's licence when an ffmpeg decodes imports.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private readonly DataPaths _paths;
    private readonly IPlatformInfo _platform;
    private readonly IFileDialogs _dialogs;
    private readonly DecoderRegistry _decoders;

    public AboutViewModel(DataPaths paths, IPlatformInfo platform, IFileDialogs dialogs, DecoderRegistry decoders)
    {
        _paths = paths;
        _platform = platform;
        _dialogs = dialogs;
        _decoders = decoders;
        decoders.CapabilitiesChanged += (_, _) => Dispatcher.UIThread.Post(() => _ = CheckFfmpegAsync());
        _ = CheckFfmpegAsync();
    }

    /// <summary>An ffmpeg (on the PATH or downloaded) decodes some imports: its licence is named.</summary>
    [ObservableProperty] private bool _usesFfmpeg;

    public Uri FfmpegUri { get; } = new("https://ffmpeg.org/legal.html");

    private async Task CheckFfmpegAsync()
    {
        try
        {
            UsesFfmpeg = (await _decoders.GetFormatsAsync("ffmpeg")).Count > 0;
        }
        catch (Exception)
        {
            UsesFfmpeg = false;
        }
    }

    /// <summary>The takes folder (one folder per take; files dropped in are imported on the next start).</summary>
    public string DataFolder => _paths.TakesDir;

    public bool CanOpenDataFolder => _platform.CanOpenDataFolder;

    /// <summary>Where the folder can't be opened from the app (Android): where to find it instead.</summary>
    public string? DataFolderHint => _platform.DataFolderHint;

    public string CreditsText => AppInfo.CreditsText;
    public Uri EuphoniaUri => AppInfo.EuphoniaUri;
    public Uri PraatUri => AppInfo.PraatUri;

    /// <summary>Why the folder couldn't be opened; null = nothing to say.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string? _status;

    public bool HasStatus => Status is not null;

    [RelayCommand]
    private async Task OpenDataFolderAsync()
    {
        Status = await _dialogs.OpenFolderAsync(DataFolder) ? null : $"couldn't open the folder here · it's {DataFolder}";
    }
}

public static class AboutFeature
{
    public static IServiceCollection AddAboutFeature(this IServiceCollection services)
    {
        services.AddSingleton<AboutViewModel>();
        return services;
    }
}
