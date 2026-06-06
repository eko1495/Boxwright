using Microsoft.Extensions.Logging;

namespace Boxwright.Core;

/// <summary>
/// An <see cref="IOsCatalogSource"/> that fetches the curated <c>OsCatalog.json</c> hosted in the project
/// repository over HTTPS, so the catalog can grow and stay fresh without shipping a new build (ADR-0020).
/// It wraps the bundled source and degrades gracefully: <b>remote → local cache → bundled</b>. A successful
/// remote fetch fully replaces the bundled list (the hosted file is the same source-of-truth file, just
/// fresher) and is written to a last-good cache for the next offline start.
/// </summary>
/// <remarks>
/// Best-effort by design: any remote failure (offline, timeout, non-success status, malformed JSON) falls
/// back silently so the catalog UI always gets a usable list. Only genuine cancellation by the caller
/// propagates. The first successful result is memoized for the process lifetime, so repeated catalog opens
/// in one session don't re-hit the network.
/// </remarks>
public sealed class RemoteOsCatalogSource : IOsCatalogSource
{
    /// <summary>The hosted catalog (raw <c>OsCatalog.json</c> on the repo's default branch).</summary>
    public const string DefaultCatalogUrl =
        "https://raw.githubusercontent.com/eko1495/Boxwright/main/src/Boxwright.Core/OsCatalog.json";

    private readonly IHttpStreamSource _http;
    private readonly IOsCatalogSource _bundled;
    private readonly Uri _catalogUrl;
    private readonly string _cacheFilePath;
    private readonly TimeSpan _timeout;
    private readonly ILogger<RemoteOsCatalogSource>? _logger;

    private readonly object _gate = new();
    private Task<IReadOnlyList<OsCatalogEntry>>? _inFlight;

    /// <summary>Creates a remote catalog source over a <paramref name="bundled"/> fallback.</summary>
    /// <param name="http">Fetches the remote JSON (shared, unit-testable).</param>
    /// <param name="bundled">The offline fallback (typically <see cref="BundledOsCatalogSource"/>).</param>
    /// <param name="catalogUrl">The hosted catalog URL (see <see cref="DefaultCatalogUrl"/>).</param>
    /// <param name="cacheFilePath">Where to store the last-good catalog JSON (see <see cref="DefaultCacheFilePath"/>).</param>
    /// <param name="timeout">How long to wait on the network before falling back.</param>
    /// <param name="logger">Optional; records which tier served the list.</param>
    public RemoteOsCatalogSource(
        IHttpStreamSource http,
        IOsCatalogSource bundled,
        Uri catalogUrl,
        string cacheFilePath,
        TimeSpan timeout,
        ILogger<RemoteOsCatalogSource>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(bundled);
        ArgumentNullException.ThrowIfNull(catalogUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFilePath);
        _http = http;
        _bundled = bundled;
        _catalogUrl = catalogUrl;
        _cacheFilePath = cacheFilePath;
        _timeout = timeout;
        _logger = logger;
    }

    /// <summary>The default last-good cache location (e.g. <c>%LOCALAPPDATA%\Boxwright\os-catalog-cache.json</c>).</summary>
    public static string DefaultCacheFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Boxwright",
        "os-catalog-cache.json");

    /// <inheritdoc />
    public Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        // Share one fetch across concurrent/repeated calls; memoize the first success for the session.
        lock (_gate)
        {
            _inFlight ??= LoadAsync(cancellationToken);
            return _inFlight;
        }
    }

    private async Task<IReadOnlyList<OsCatalogEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<OsCatalogEntry> entries = await FetchRemoteAsync(cancellationToken);
            _logger?.LogInformation("OS catalog loaded from the remote manifest ({Count} entries).", entries.Count);
            return entries;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller cancelled — don't memoize a failed/incomplete result, and surface the cancellation.
            lock (_gate)
            {
                _inFlight = null;
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Remote OS catalog fetch failed; falling back to the cache, then the bundled list.");
        }

        if (TryReadCache(out IReadOnlyList<OsCatalogEntry> cached))
        {
            _logger?.LogInformation("OS catalog loaded from the last-good cache ({Count} entries).", cached.Count);
            return cached;
        }

        IReadOnlyList<OsCatalogEntry> bundled = await _bundled.GetEntriesAsync(cancellationToken);
        _logger?.LogInformation("OS catalog loaded from the bundled list ({Count} entries).", bundled.Count);
        return bundled;
    }

    private async Task<IReadOnlyList<OsCatalogEntry>> FetchRemoteAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_timeout > TimeSpan.Zero)
        {
            timeoutCts.CancelAfter(_timeout);
        }

        string json;
        try
        {
            using HttpDownload download = await _http.OpenReadAsync(_catalogUrl, timeoutCts.Token);
            using var reader = new StreamReader(download.Content);
            json = await reader.ReadToEndAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The internal timeout fired (not the caller) — treat as a fallback-worthy failure.
            throw new DownloadException($"The OS catalog request to {_catalogUrl} timed out.");
        }

        OsCatalogDocument document = OsCatalogJson.Deserialize(json); // throws OsCatalogException on bad JSON
        WriteCache(json);
        return document.Entries;
    }

    private bool TryReadCache(out IReadOnlyList<OsCatalogEntry> entries)
    {
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                entries = OsCatalogJson.Deserialize(File.ReadAllText(_cacheFilePath)).Entries;
                return true;
            }
        }
        catch (Exception ex) when (ex is OsCatalogException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "The cached OS catalog at {Path} could not be read.", _cacheFilePath);
        }

        entries = [];
        return false;
    }

    private void WriteCache(string json)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write to a temp file then move into place so a crash mid-write can't corrupt the last-good cache.
            string tempPath = _cacheFilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _cacheFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogDebug(ex, "The OS catalog cache at {Path} could not be written.", _cacheFilePath);
        }
    }
}
