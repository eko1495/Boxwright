namespace Boxwright.Core;

/// <summary>
/// Supplies the catalog of installable OS images. The default implementation
/// (<see cref="BundledOsCatalogSource"/>) reads a curated list bundled with the app;
/// a remote source could implement this later without changing callers.
/// </summary>
public interface IOsCatalogSource
{
    /// <summary>Returns the available OS catalog entries.</summary>
    /// <exception cref="OsCatalogException">The catalog could not be loaded or parsed.</exception>
    Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default);
}
