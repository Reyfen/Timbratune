namespace Reyfen.Timbratune.Core.Platform;

/// <summary>Fetches a file over HTTPS (a decoder the user asked for). A port, so installers can be tested offline.</summary>
public interface IHttpDownloader
{
    /// <summary>Writes the body of <paramref name="url"/> into <paramref name="target"/>, reporting 0–1 when the size is known.</summary>
    Task DownloadAsync(Uri url, Stream target, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary><see cref="IHttpDownloader"/> over <see cref="HttpClient"/>; refuses anything but HTTPS.</summary>
public sealed class HttpClientDownloader : IHttpDownloader
{
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan }; // long downloads; cancelled by the caller

    public async Task DownloadAsync(Uri url, Stream target, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (url.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("Only HTTPS downloads.", nameof(url));
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var length = response.Content.Headers.ContentLength;
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[81920];
        long done = 0;
        int n;
        while ((n = await body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, n), cancellationToken);
            done += n;
            if (length > 0) progress?.Report((double)done / length.Value);
        }
    }
}
