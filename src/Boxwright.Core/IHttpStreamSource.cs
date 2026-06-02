namespace Boxwright.Core;

/// <summary>
/// Opens an HTTP(S) response body for streaming. Abstracted so the ISO downloader can be
/// unit-tested without real network I/O; the concrete implementation is
/// <see cref="HttpClientStreamSource"/>.
/// </summary>
public interface IHttpStreamSource
{
    /// <summary>Opens <paramref name="uri"/> for reading. The caller disposes the returned object.</summary>
    /// <exception cref="DownloadException">The request failed or returned a non-success status.</exception>
    Task<HttpDownload> OpenReadAsync(Uri uri, CancellationToken cancellationToken = default);
}

/// <summary>
/// A readable HTTP response body plus the server-declared length (null when unknown).
/// Disposing it disposes the body stream and any owning response.
/// </summary>
public sealed class HttpDownload : IDisposable
{
    private readonly IDisposable? _owner;

    /// <param name="content">The response body to read from.</param>
    /// <param name="totalBytes">The <c>Content-Length</c> if declared; otherwise null.</param>
    /// <param name="owner">An optional extra disposable (e.g. the HTTP response) released on dispose.</param>
    public HttpDownload(Stream content, long? totalBytes, IDisposable? owner = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
        TotalBytes = totalBytes;
        _owner = owner;
    }

    /// <summary>The response body to read from.</summary>
    public Stream Content { get; }

    /// <summary>The <c>Content-Length</c> if the server declared one; otherwise null.</summary>
    public long? TotalBytes { get; }

    /// <summary>Disposes the body stream and the owning response, if any.</summary>
    public void Dispose()
    {
        Content.Dispose();
        _owner?.Dispose();
    }
}
