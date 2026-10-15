using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Reyfen.Timbratune.Shared.Platform;

/// <summary><see cref="IFileDialogs"/> over Avalonia's cross-platform StorageProvider.</summary>
public sealed class StorageProviderDialogs : IFileDialogs
{
    private Control? _pending;

    /// <summary>Binds to the window / view; its TopLevel is resolved lazily (views attach after load).</summary>
    public void Attach(Control control) => _pending = control;

    // Looked up on every call, not cached: on Android the view moves to a new TopLevel when the
    // Activity is re-created (the renderer switch for split screen), and the old one is disposed.
    private TopLevel? CurrentTopLevel => _pending as TopLevel ?? TopLevel.GetTopLevel(_pending);

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
        if (CurrentTopLevel is not { } topLevel) return null;

        // Android's picker adds DefaultExtension to the suggested name itself ("x.tmbr.tmbr"
        // otherwise); the desktop pickers don't, so they keep the full name.
        if (OperatingSystem.IsAndroid() && suggestedName.EndsWith("." + extension, StringComparison.OrdinalIgnoreCase))
            suggestedName = suggestedName[..^(extension.Length + 1)];

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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

    public async Task<IReadOnlyList<PickedFile>> OpenFilesAsync(string title, string typeName, IReadOnlyList<string> extensions)
    {
        if (CurrentTopLevel is not { } topLevel) return [];
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = [.. extensions.Select(e => "*." + e)] }, FilePickerFileTypes.All],
        });
        return [.. files.Select(f => new PickedFile(f.Name, f.OpenReadAsync))];
    }

    public async Task<bool> OpenFolderAsync(string path)
    {
        if (CurrentTopLevel is not { } topLevel) return false;
        Directory.CreateDirectory(path);
        try
        {
            return await topLevel.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
