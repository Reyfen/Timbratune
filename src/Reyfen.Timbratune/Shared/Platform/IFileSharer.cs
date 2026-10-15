namespace Reyfen.Timbratune.Shared.Platform;

/// <summary>
/// The platform's share sheet ("Share via…"): hands a file to another app (messengers, mail,
/// cloud drives). Provided where there is one (Android); null elsewhere, where takes are saved
/// through <see cref="IFileDialogs"/> instead.
/// </summary>
public interface IFileSharer
{
    /// <summary>
    /// Writes a file called <paramref name="fileName"/> with <paramref name="write"/>, then opens
    /// the share sheet for it. Returns once the sheet is shown: whether the person went on to
    /// share it isn't known.
    /// </summary>
    Task ShareAsync(string title, string fileName, string mimeType, Func<Stream, Task> write);
}
