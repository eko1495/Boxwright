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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Directory.CreateDirectory(_cacheDirectory);
        string finalPath = Path.Combine(_cacheDirectory, CacheFileName(entry));
        string markerPath = finalPath + ".sha256";

        // Cache hit: a previously-verified file whose marker still matches — reuse, no re-hash.
        if (File.Exists(finalPath) && File.Exists(markerPath))
        {
            string recorded = (await File.ReadAllTextAsync(markerPath, cancellationToken)).Trim();
            if (string.Equals(recorded, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                long length = new FileInfo(finalPath).Length;
                progress?.Report(new IsoDownloadProgress(length, length));
                return finalPath;
            }
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
