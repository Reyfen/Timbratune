using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Platform;

namespace Reyfen.Timbratune.Features.About;

/// <summary>
/// The page's footer: where the takes live (a link that opens the folder, or on Android where
/// to find it), the version and the credits.
/// </summary>
public sealed partial class AboutViewModel(DataPaths paths, IPlatformInfo platform, IFileDialogs dialogs) : ObservableObject
{
    /// <summary>The takes folder (one folder per take; files dropped in are imported on the next start).</summary>
    public string DataFolder => paths.TakesDir;

    public bool CanOpenDataFolder => platform.CanOpenDataFolder;

    /// <summary>Where the folder can't be opened from the app (Android): where to find it instead.</summary>
    public string? DataFolderHint => platform.DataFolderHint;

    public string CreditsText => AppInfo.CreditsText;
    public Uri EuphoniaUri => AppInfo.EuphoniaUri;
    public Uri PraatUri => AppInfo.PraatUri;

    /// <summary>Why the folder couldn't be opened; null = nothing to say.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasStatus))] private string? _status;

    public bool HasStatus => Status is not null;

    [RelayCommand]
    private async Task OpenDataFolderAsync()
    {
        Status = await dialogs.OpenFolderAsync(DataFolder) ? null : $"couldn't open the folder here · it's {DataFolder}";
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
