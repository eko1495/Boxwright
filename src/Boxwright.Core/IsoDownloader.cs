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
    private readonly IOpenPgpVerifier? _pgp;
    private readonly ITrustedKeyProvider? _keys;

    public IsoDownloader(IHttpStreamSource http, string cacheDirectory)
        : this(http, cacheDirectory, pgp: null, keys: null)
    {
    }

    /// <param name="http">The HTTP stream source for the ISO and (when an entry is signed) its checksums/signature.</param>
    /// <param name="cacheDirectory">The shared ISO cache directory.</param>
    /// <param name="pgp">
    /// Optional OpenPGP verifier (ADR-0027). Required to honour an entry's <see cref="OsCatalogSignature"/>;
    /// when null, an entry that carries signature info is rejected rather than silently downgraded.
    /// </param>
    /// <param name="keys">Optional bundled trusted-key provider — the trust anchor for signature verification.</param>
    public IsoDownloader(IHttpStreamSource http, string cacheDirectory, IOpenPgpVerifier? pgp, ITrustedKeyProvider? keys)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _http = http;
        _cacheDirectory = cacheDirectory;
        _pgp = pgp;
        _keys = keys;
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
        // The marker is written only after the full gate (SHA-256 and, when the entry opts in, the
        // OpenPGP signature) passed, so its presence re-establishes trust without re-fetching the
        // checksums/signature — a signed entry's cache hit makes zero network calls.
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

            // SHA-256 proved integrity. If the entry opts into a signature, also prove provenance: the
            // hash must appear in a checksums document signed by a bundled trusted key (ADR-0027). This is
            // an *additional* gate after SHA-256, never a fallback — any failure throws and discards.
            if (entry.Signature is { } signature)
            {
                await VerifySignatureAsync(entry, signature, cancellationToken);
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

    // The OpenPGP provenance gate (ADR-0027), run AFTER SHA-256 matched and BEFORE the .part is promoted.
    // Fetches the checksums document and its detached signature, verifies the signature against the
    // bundled trusted key the entry names, and confirms the entry's SHA-256 is listed in the (now-trusted)
    // checksums against the expected filename. Fails closed: any problem throws DownloadException and the
    // caller discards the .part — there is no silent downgrade to SHA-256-only.
    private async Task VerifySignatureAsync(
        OsCatalogEntry entry, OsCatalogSignature signature, CancellationToken cancellationToken)
    {
        if (_pgp is null || _keys is null)
        {
            // An entry asked for signature verification but this downloader wasn't given the means to do
            // it. Refuse rather than trust the bytes on SHA-256 alone — that would defeat the opt-in gate.
            throw new DownloadException(
                $"{entry.Name} requires OpenPGP signature verification, but no verifier is configured. " +
                "The download was discarded.");
        }

        await using Stream? publicKey = _keys.OpenPublicKey(signature.KeyId);
        if (publicKey is null)
        {
            throw new DownloadException(
                $"No bundled trusted key '{signature.KeyId}' for {entry.Name}; the download was discarded. " +
                "The catalog entry references a key this build does not ship.");
        }

        byte[] checksums = await FetchAsync(signature.ChecksumsUrl, cancellationToken);
        byte[] detachedSignature = await FetchAsync(signature.SignatureUrl, cancellationToken);

        OpenPgpVerification verification;
        try
        {
            verification = _pgp.Verify(
                new MemoryStream(checksums, writable: false),
                new MemoryStream(detachedSignature, writable: false),
                publicKey);
        }
        catch (OpenPgpException ex)
        {
            // Malformed signature/key material, or a signature whose key id doesn't match the bundled key.
            throw new DownloadException(
                $"The OpenPGP signature for {entry.Name} could not be verified; the download was discarded.", ex);
        }

        if (!verification.IsValid)
        {
            throw new DownloadException(
                $"The OpenPGP signature for {entry.Name}'s checksums did not verify against the bundled key " +
                $"'{signature.KeyId}'; the download was discarded.");
        }

        // The checksums document is now authentic. Confirm THIS entry's hash is the one it vouches for,
        // pinned to the expected filename so a multi-image SHA256SUMS can't match one image's hash to
        // another's name.
        string expectedFileName = string.IsNullOrWhiteSpace(signature.ChecksumsFileName)
            ? LastUrlSegment(entry.IsoUrl)
            : signature.ChecksumsFileName!;

        if (!SignedChecksums.Contains(checksums, entry.Sha256, expectedFileName))
        {
            throw new DownloadException(
                $"{entry.Name}'s SHA-256 was not found for '{expectedFileName}' in the signed checksums; " +
                "the download was discarded. The catalog entry's hash and the signed checksums disagree.");
        }
    }

    private async Task<byte[]> FetchAsync(Uri uri, CancellationToken cancellationToken)
    {
        using HttpDownload download = await _http.OpenReadAsync(uri, cancellationToken);
        using var buffer = new MemoryStream();
        await download.Content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static string LastUrlSegment(Uri uri)
    {
        string segment = uri.Segments.Length > 0 ? uri.Segments[^1] : string.Empty;
        return Uri.UnescapeDataString(segment).Trim('/');
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
