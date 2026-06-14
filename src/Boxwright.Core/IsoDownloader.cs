using System.Security.Cryptography;

namespace Boxwright.Core;

/// <summary>
/// Downloads catalog ISOs into a shared cache, verifying SHA-256 while streaming. A verified
/// download writes the ISO plus a small <c>.sha256</c> marker; a later request for the same entry
/// reuses the cached file (when its marker matches the expected hash) without re-downloading or
/// re-hashing the multi-gigabyte image. The download streams to a <c>.part</c> file that is
/// atomically moved into place only after verification — a cancel, error, or mismatch leaves no
/// partial or unverified file behind.
/// </summary>
/// <remarks>
/// Trusting the marker keeps cache hits cheap, but it can't see an ISO that rotted on disk <em>after</em>
/// it was verified (bad sectors, a torn write). Two guards mitigate that: every cache hit cheaply checks
/// the file length against the catalog's <see cref="OsCatalogEntry.SizeBytes"/> (catches truncation in
/// O(1)), and callers can pass <c>reverifyCachedContent</c> to re-hash the full file at one-time moments
/// like VM creation (catches same-length corruption). A failed guard purges the cached file <em>and</em>
/// its marker before re-downloading, so a bad copy can't be trusted again if the re-download also fails.
/// </remarks>
public sealed class IsoDownloader : IIsoDownloader
{
    private const int BufferSize = 1 << 20;            // 1 MiB read buffer
    private const long ProgressInterval = 4L << 20;    // report at most every ~4 MiB

    private readonly IHttpStreamSource _http;
    private readonly string _cacheDirectory;

    public IsoDownloader(IHttpStreamSource http, string cacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _http = http;
        _cacheDirectory = cacheDirectory;
    }

    /// <summary>The default shared ISO cache (e.g. <c>%LOCALAPPDATA%\Boxwright\ISOs</c>).</summary>
    public static string DefaultCacheDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Boxwright", "ISOs");

    /// <inheritdoc />
    public async Task<string> EnsureAsync(
        OsCatalogEntry entry,
        IProgress<IsoDownloadProgress>? progress = null,
        bool reverifyCachedContent = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Directory.CreateDirectory(_cacheDirectory);
        string finalPath = Path.Combine(_cacheDirectory, CacheFileName(entry));
        string markerPath = finalPath + ".sha256";

        // Cache hit: a previously-verified file whose marker still matches and which still passes the
        // integrity guards (size always; full content when the caller opts in) — reuse, no download.
        if (File.Exists(finalPath) && File.Exists(markerPath))
        {
            string recorded = (await File.ReadAllTextAsync(markerPath, cancellationToken)).Trim();
            if (string.Equals(recorded, entry.Sha256, StringComparison.OrdinalIgnoreCase)
                && await CachedFileIsIntactAsync(finalPath, entry, reverifyCachedContent, cancellationToken))
            {
                long length = new FileInfo(finalPath).Length;
                progress?.Report(new IsoDownloadProgress(length, length));
                return finalPath;
            }

            // Stale marker, wrong size, or rotted content: drop the file and its marker so a failed
            // re-download can't leave a bad copy that the marker would vouch for next time.
            TryDelete(finalPath);
            TryDelete(markerPath);
        }

        string partPath = finalPath + ".part";
        try
        {
            string actualHash = await DownloadAndHashAsync(entry, partPath, progress, cancellationToken);

            if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new DownloadException(
                    $"Checksum did not match for {entry.Name} (expected {entry.Sha256}, got {actualHash}). " +
                    "The download was discarded — the catalog entry may be out of date.");
            }

            File.Move(partPath, finalPath, overwrite: true);
            await File.WriteAllTextAsync(markerPath, actualHash, cancellationToken);
            return finalPath;
        }
        catch
        {
            TryDelete(partPath); // clean up a partial / failed / cancelled / mismatched download
            throw;
        }
    }

    private async Task<string> DownloadAndHashAsync(
        OsCatalogEntry entry, string partPath, IProgress<IsoDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using HttpDownload download = await _http.OpenReadAsync(entry.IsoUrl, cancellationToken);
        long? total = download.TotalBytes ?? (entry.SizeBytes > 0 ? entry.SizeBytes : null);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0;

        await using (var file = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] buffer = new byte[BufferSize];
            long lastReported = 0;
            int read;
            while ((read = await download.Content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                received += read;

                if (received - lastReported >= ProgressInterval)
                {
                    progress?.Report(new IsoDownloadProgress(received, total));
                    lastReported = received;
                }
            }
        }

        progress?.Report(new IsoDownloadProgress(received, total ?? received));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // Whether a marker-matched cached file still looks intact. The size check is O(1) and always runs;
    // the full re-hash is multi-gigabyte, so it runs only when the caller opted in. A false result means
    // the cache is corrupt/stale and the caller will purge and re-download.
    private static async Task<bool> CachedFileIsIntactAsync(
        string finalPath, OsCatalogEntry entry, bool reverifyContent, CancellationToken cancellationToken)
    {
        // A previously-verified download has the catalog's exact size; a truncated or partially-overwritten
        // file fails this immediately. (SizeBytes is approximate for progress, but exact once verified.)
        if (entry.SizeBytes > 0 && new FileInfo(finalPath).Length != entry.SizeBytes)
        {
            return false;
        }

        if (!reverifyContent)
        {
            return true;
        }

        string actualHash = await HashFileAsync(finalPath, cancellationToken);
        return string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        byte[] digest = await SHA256.HashDataAsync(file, cancellationToken);
        return Convert.ToHexStringLower(digest);
    }

    // Cache file name: the URL's last path segment (unescaped); fall back to "{id}.iso".
    private static string CacheFileName(OsCatalogEntry entry)
    {
        string segment = entry.IsoUrl.Segments.Length > 0 ? entry.IsoUrl.Segments[^1] : string.Empty;
        segment = Uri.UnescapeDataString(segment).Trim('/');
        if (string.IsNullOrWhiteSpace(segment) || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            segment = $"{entry.Id}.iso";
        }

        return segment;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // best-effort cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }
}
