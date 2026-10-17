using Reyfen.Timbratune.Core.Audio.Decoding;
using Reyfen.Timbratune.Core.Storage;
using Reyfen.Timbratune.Shared.Platform;

namespace Reyfen.Timbratune.Features.Import;

/// <summary>
/// The Import picker's file types, from what this device decodes: everything importable first
/// (preselected), then one choice per format, then .tmbr. Never "All files": what is offered can
/// be read. Some Android file managers ignore the types; the importer still checks each file's
/// content.
/// </summary>
public static class ImportFilters
{
    /// <summary>
    /// .tmbr has no registered type anywhere: Android's pickers see it as a generic binary
    /// (so its group admits any file there, and the content check sorts them out).
    /// </summary>
    public static FileTypeFilter Takes { get; } =
        new("Timbratune takes", [TakeArchive.Extension], ["application/octet-stream", "application/zip"]);

    public static IReadOnlyList<FileTypeFilter> For(FormatSet decodable)
    {
        var formats = decodable.Formats;
        if (formats.Count == 0) return [Takes];
        var types = formats.Select(f => new FileTypeFilter(f.DisplayName, f.Extensions, f.MimeTypes, f.AppleUtis.Length > 0 ? f.AppleUtis : null)).ToList();
        var everything = types.Append(Takes).ToList();
        var all = new FileTypeFilter("Audio and Timbratune takes",
            [.. everything.SelectMany(t => t.Extensions).Distinct()],
            [.. everything.SelectMany(t => t.MimeTypes).Distinct()],
            // On Apple platforms a group is either all type identifiers or all extensions.
            everything.All(t => t.AppleUtis is not null) ? [.. everything.SelectMany(t => t.AppleUtis!).Distinct()] : null);
        return [all, .. types, Takes];
    }
}
