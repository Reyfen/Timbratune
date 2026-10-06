using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Reyfen.Timbratune.Services;

/// <summary><see cref="IFileDialogs"/> over Avalonia's cross-platform StorageProvider.</summary>
public sealed class StorageProviderDialogs : IFileDialogs
{
    private TopLevel? _topLevel;
    private Control? _pending;

    /// <summary>Binds to the window / view; its TopLevel is resolved lazily (views attach after load).</summary>
    public void Attach(Control control) => _pending = control;

    public async Task<string?> SaveCopyAsync(string sourcePath, string suggestedName)
    {
        _topLevel ??= _pending as TopLevel ?? TopLevel.GetTopLevel(_pending);
        if (_topLevel is null) return null;

        var ext = Path.GetExtension(sourcePath).TrimStart('.');
        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a copy of this take",
            SuggestedFileName = suggestedName,
            DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType(ext.ToUpperInvariant() + " audio") { Patterns = ["*." + ext] }],
        });
        if (file is null) return null;

        await using (var source = File.OpenRead(sourcePath))
        await using (var target = await file.OpenWriteAsync())
            await source.CopyToAsync(target);
        return file.TryGetLocalPath() ?? file.Name;
    }
}
