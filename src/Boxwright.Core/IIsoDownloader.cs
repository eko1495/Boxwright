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
    /// </summary>
    /// <exception cref="DownloadException">A network error, non-success status, or checksum mismatch.</exception>
    Task<string> EnsureAsync(
        OsCatalogEntry entry,
        IProgress<IsoDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
