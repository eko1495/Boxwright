namespace Boxwright.Core;

/// <summary>
/// Ensures a catalog entry's ISO is present in the local cache and verified, returning its
/// absolute path. Reuses a previously-verified cached copy without re-downloading or re-hashing.
/// </summary>
public interface IIsoDownloader
{
    /// <summary>
    /// Returns the absolute path of the verified ISO for <paramref name="entry"/>, downloading it
    /// (verifying SHA-256 while streaming) if it is not already cached. Reports progress and honors
    /// cancellation; a cancel or failure leaves no partial file behind.
    /// <para>
    /// When <paramref name="reverifyCachedContent"/> is <see langword="true"/>, a cache hit is re-validated
    /// by hashing the cached file's full content (not just trusting the <c>.sha256</c> marker). This catches
    /// a previously-verified ISO that rotted on disk afterwards — silent corruption the marker and a size
    /// check can't see. It is a multi-gigabyte read, so callers opt in only at one-time moments (e.g. VM
    /// creation), not on every cache hit. A mismatch purges the cached file and re-downloads a fresh copy.
    /// </para>
    /// </summary>
    /// <exception cref="DownloadException">A network error, non-success status, or checksum mismatch.</exception>
    Task<string> EnsureAsync(
        OsCatalogEntry entry,
        IProgress<IsoDownloadProgress>? progress = null,
        bool reverifyCachedContent = false,
        CancellationToken cancellationToken = default);
}
