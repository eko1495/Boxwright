namespace Boxwright.Core;

/// <summary>
/// <see cref="IHttpStreamSource"/> backed by a shared <see cref="HttpClient"/>. Streams the
/// response body (does not buffer it) and surfaces <c>Content-Length</c> when present. The
/// injected client is shared process-wide and is not disposed here.
/// </summary>
public sealed class HttpClientStreamSource : IHttpStreamSource
{
    private readonly HttpClient _httpClient;

    public HttpClientStreamSource(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task<HttpDownload> OpenReadAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
            return new HttpDownload(content, total, response); // response released when the wrapper is disposed
        }
        catch (HttpRequestException ex)
        {
            response?.Dispose();
            throw new DownloadException($"Couldn't download {uri}: {ex.Message}", ex);
        }
        catch
        {
            response?.Dispose();
            throw; // cancellation and other failures propagate unwrapped
        }
    }
}
