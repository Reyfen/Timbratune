using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Storage.Settings;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Messaging;
using Reyfen.Timbratune.Shared.Platform;

namespace Reyfen.Timbratune.Features.Import;

/// <summary>The Import button, and (in <see cref="Status"/>) the line saying what an import is doing or how it went.</summary>
public sealed partial class ImportViewModel : ObservableObject
{
    private readonly ImportManager _manager;
    private readonly ImportRouter _router;
    private readonly IFileDialogs _dialogs;

    public ImportViewModel(ImportManager manager, ImportRouter router, IFileDialogs dialogs, IMessenger messenger)
    {
        _manager = manager;
        _router = router;
        _dialogs = dialogs;
        // An import's note is about the takes list; a new recording makes it old news.
        messenger.Register<ImportViewModel, RecordingStarted>(this, (vm, _) =>
        {
            if (!vm.IsImporting) vm.Status.Text = null;
        });
    }

    [ObservableProperty] private bool _isImporting;

    public ImportStatusViewModel Status { get; } = new();

    /// <summary>At start: clears interrupted imports, then imports any audio or .tmbr files dropped into the takes folder.</summary>
    public async Task StartAsync()
    {
        await _manager.CleanUpAsync();
        var dropped = await _manager.FindDroppedAsync();
        await RunAsync(dropped.Importable.Select(path => (Path.GetFileName(path),
            (Func<IProgress<double>, Task<Recording>>)(progress => _manager.ImportDroppedAsync(path, progress)))).ToList(),
            "found in the takes folder");
        if (dropped.Unsupported.Count > 0)
        {
            // Said once, at start; the files stay for a decoder that may come later.
            var left = $"left {string.Join(", ", dropped.Unsupported.Select(Path.GetFileName))} in the takes folder · this device can't decode {(dropped.Unsupported.Count == 1 ? "it" : "them")} yet";
            Status.Text = string.IsNullOrEmpty(Status.Text) ? left : Status.Text + " · " + left;
        }
    }

    /// <summary>
    /// Audio files (analyzed) and .tmbr files (taken as they are). The picker offers only what this
    /// device decodes; a file that slipped through (some Android file managers ignore the types)
    /// is refused by its content, with the reason.
    /// </summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (IsImporting) return;
        // Linux without ffmpeg: offer it first (once, unless "not now"); the picker then shows what it adds.
        if (_manager.ShouldOfferFfmpeg) await _router.OfferFfmpegAsync();
        var files = await _dialogs.OpenFilesAsync("Import takes", await _manager.GetPickerTypesAsync());
        await RunAsync(files.Select(f => (f.Name, (Func<IProgress<double>, Task<Recording>>)(progress => _manager.ImportPickedAsync(f, progress))))
            .ToList(), null);
    }

    private async Task RunAsync(IReadOnlyList<(string Name, Func<IProgress<double>, Task<Recording>> Import)> imports, string? where)
    {
        if (imports.Count == 0) return;
        IsImporting = true;
        var failed = new List<string>();
        var undecodable = false;
        var done = 0;
        try
        {
            for (var i = 0; i < imports.Count; i++)
            {
                var (name, import) = imports[i];
                var prefix = imports.Count > 1 ? $"importing {i + 1} of {imports.Count}" : "importing";
                Status.Text = $"{prefix} · {name}…";
                var progress = new ThrottledProgress(f =>
                {
                    if (IsImporting) Status.Text = $"{prefix} · {name} · {f:P0}"; // late reports after the end are dropped
                });
                try
                {
                    await import(progress);
                    done++;
                }
                catch (Exception ex)
                {
                    failed.Add($"{name} ({ex.Message})");
                    undecodable |= ex is UnsupportedAudioException { Format: not null };
                }
            }
        }
        finally
        {
            IsImporting = false;
        }
        var summary = done == 0 ? "" : $"imported {done} {(done == 1 ? "take" : "takes")}{(where is null ? "" : " " + where)} ✨";
        if (failed.Count > 0) summary += (summary.Length > 0 ? " · " : "") + "couldn't import " + string.Join(", ", failed) + " 🌧️";
        Status.Text = summary;
        _manager.Finished(done, failed.Count);
        // A known format nothing here decodes: FFmpeg would (Linux, unless declined).
        if (undecodable && _manager.ShouldOfferFfmpeg) await _router.OfferFfmpegAsync();
    }
}

/// <summary>What an import is doing, or how it went (shown at the bottom of the record panel).</summary>
public sealed partial class ImportStatusViewModel : ObservableObject
{
    /// <summary>Null = nothing to say.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasText))] private string? _text;

    public bool HasText => Text is not null;
}

public static class ImportFeature
{
    public static IServiceCollection AddImportFeature(this IServiceCollection services)
    {
        services.AddSingleton<ImportManager>();
        services.AddSingleton<ImportRouter>();
        services.AddSingleton<ImportViewModel>();
        services.AddSingleton<ISettingKey>(ImportSettings.FfmpegOfferDeclined);
        return services;
    }
}
