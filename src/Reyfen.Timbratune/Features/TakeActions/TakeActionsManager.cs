using CommunityToolkit.Mvvm.Messaging;
using Reyfen.Timbratune.Core.Analysis;
using Reyfen.Timbratune.Core.Models;
using Reyfen.Timbratune.Core.Platform;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Services;
using Reyfen.Timbratune.Shared;
using Reyfen.Timbratune.Shared.Messaging;
using Reyfen.Timbratune.Shared.Platform;
using Reyfen.Timbratune.Shared.State;

namespace Reyfen.Timbratune.Features.TakeActions;

/// <summary>
/// What can be done to one take: save a copy of its audio or share it, export (or share) its
/// .tmbr file, rename it, delete it. Changes reload the takes and are announced.
/// </summary>
/// <param name="sharer">The share sheet, where the platform has one (Android): takes are then shared instead of saved.</param>
public sealed class TakeActionsManager(RecordingStore store, IAnalysisEngine engine, PlaybackService playback, IFileDialogs dialogs,
    ITakeCatalog catalog, IMessenger messenger, IFileSharer? sharer = null, IDeviceInfo? device = null)
{
    private readonly HashSet<int> _exporting = [];

    /// <summary>The platform has a share sheet: actions share instead of saving to a folder.</summary>
    public bool CanShare => sharer is not null;

    public Recording? Find(int takeId) => catalog.Takes.FirstOrDefault(r => r.Id == takeId);

    /// <summary>The take's audio file, if it is there.</summary>
    public string? AudioPathOf(Recording take) => store.Paths.Resolve(take.Audio) is { } path && File.Exists(path) ? path : null;

    /// <summary>Tells the take's card what an action is doing or how it went (null = nothing to say).</summary>
    public void ReportStatus(int takeId, string? text, bool busy = false) => messenger.Send(new TakeActionStatus(takeId, text, busy));

    /// <summary>The audio: a copy saved through the file picker, or shared.</summary>
    public async Task SaveAudioAsync(Recording take)
    {
        if (AudioPathOf(take) is not { } path) return;
        var name = $"voice-take-{take.Id}{Path.GetExtension(path)}";
        if (sharer is null)
        {
            await dialogs.SaveCopyAsync(path, name);
            return;
        }
        var mime = Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav" : "application/octet-stream";
        await sharer.ShareAsync("Share this take's audio", name, mime, async target =>
        {
            await using var source = File.OpenRead(path);
            await source.CopyToAsync(target);
        });
    }

    /// <summary>
    /// Exports a take as a .tmbr file (WAV + take.json, see <see cref="TakeArchive"/>), saved through
    /// the file picker or shared. Takes analyzed before the per-frame lists were kept get them now:
    /// the stored WAV is analyzed once and the lists saved for next time. The lists are only held
    /// while the file is written. Returns where it was saved, or null if cancelled, shared, or an
    /// export of this take is already running.
    /// </summary>
    public async Task<string?> ExportDataAsync(Recording take)
    {
        if (AudioPathOf(take) is not { } audioPath || !_exporting.Add(take.Id)) return null;
        try
        {
            var (detail, series) = await Task.Run(() => (store.LoadDetail(take), store.LoadSeries(take)));
            if (detail is null) throw new InvalidOperationException("this take's analysis file is missing");
            if (series is null)
            {
                var floor = take.Register?.FloorHz ?? AnalysisPostProcessor.DefaultRegisterFloorHz;
                var result = await engine.AnalyzeAsync(audioPath, floor);
                series = result.Series ?? throw new InvalidOperationException("the analysis gave no per-frame lists");
                detail = result.Detail;
                var analyzed = series;
                await Task.Run(() =>
                {
                    store.SaveDetail(take, detail);
                    store.SaveSeries(take, analyzed);
                });
                messenger.Send(new TakeAnalysisUpdated(take.Id));
            }

            var (d, s) = (detail, series);
            var name = $"voice-take-{take.Id}.{TakeArchive.Extension}";
            Func<Stream, Task> write = target => Task.Run(() => TakeArchive.Write(target, take, d, s, audioPath, AppInfo.Version,
                device?.Describe()));
            if (sharer is not null)
            {
                await sharer.ShareAsync("Share this take's data", name, "application/octet-stream", write);
                return null;
            }
            return await dialogs.SaveAsync("Export this take's data", name, "Timbratune take", TakeArchive.Extension,
                "application/octet-stream", write);
        }
        finally
        {
            _exporting.Remove(take.Id);
        }
    }

    /// <summary>Renames a take (label and folder) and shows the new name everywhere.</summary>
    public async Task RenameAsync(Recording take, string label)
    {
        // A take that is playing keeps its folder name: the player holds its audio open.
        var playing = store.Paths.Resolve(take.Audio) is { } audio && playback.CurrentPath == audio;
        await Task.Run(() => store.Rename(take, label, renameFolder: !playing));
        await catalog.ReloadAsync();
        messenger.Send(new TakeRenamed(take.Id, label));
    }

    public async Task DeleteAsync(Recording take)
    {
        // Release the file before deleting it.
        if (store.Paths.Resolve(take.Audio) is { } audio && playback.CurrentPath == audio) playback.Stop();
        await Task.Run(() => store.Delete(take.Id));
        await catalog.ReloadAsync();
        messenger.Send(new TakeDeleted(take.Id));
    }
}
