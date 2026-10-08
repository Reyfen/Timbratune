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

    public Task<string?> SaveCopyAsync(string sourcePath, string suggestedName)
    {
        var ext = Path.GetExtension(sourcePath).TrimStart('.');
        return SaveAsync("Save a copy of this take's audio", suggestedName, ext.ToUpperInvariant() + " audio", ext,
            ext.Equals("wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav" : "application/octet-stream",
            async target =>
            {
                await using var source = File.OpenRead(sourcePath);
                await source.CopyToAsync(target);
            });
    }

    public async Task<string?> SaveAsync(string title, string suggestedName, string typeName, string extension, string mimeType,
        Func<Stream, Task> write)
    {
        _topLevel ??= _pending as TopLevel ?? TopLevel.GetTopLevel(_pending);
        if (_topLevel is null) return null;

        var file = await _topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = ["*." + extension], MimeTypes = [mimeType] }],
        });
        if (file is null) return null;

        await using (var target = await file.OpenWriteAsync())
            await write(target);
        return file.TryGetLocalPath() ?? file.Name;
    }
}
