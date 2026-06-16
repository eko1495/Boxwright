namespace Boxwright.Core;

/// <summary>
/// Which tier served the OS catalog this session, and whether that source is trustworthy-fresh.
/// Lets a front end distinguish a live remote fetch, a recent cache, a <i>stale</i> cache served only
/// because the remote was unreachable, and the shipped bundled baseline (ADR-0020 freshness check).
/// </summary>
public enum OsCatalogFreshnessState
{
    /// <summary>No catalog load has completed yet, so freshness is not known.</summary>
    Unknown,

    /// <summary>Served live from the remote manifest this session — the freshest possible.</summary>
    Remote,

    /// <summary>Remote was unreachable, but the last-good cache is within the freshness window.</summary>
    FreshCache,

    /// <summary>Remote was unreachable and the cache is older than the freshness window — possibly outdated ISO URLs/SHA-256.</summary>
    StaleCache,

    /// <summary>No cache existed, so the shipped bundled list was used. The baseline floor, not a "stale" state.</summary>
    Bundled,
}

/// <summary>
/// A snapshot of how the OS catalog was sourced and how old it is, so the CLI/GUI can surface staleness
/// instead of silently serving an outdated cache (ADR-0020). A bundled fallback is the shipped baseline,
/// not "stale" — only a cache older than the freshness window is flagged.
/// </summary>
/// <param name="State">Which tier served the catalog this session.</param>
/// <param name="CachedAtUtc">When the cache was last refreshed from the remote, for cache states; otherwise <see langword="null"/>.</param>
/// <param name="Age">How old the cache is relative to "now", for cache states; otherwise <see langword="null"/>.</param>
public sealed record OsCatalogFreshness(
    OsCatalogFreshnessState State,
    DateTimeOffset? CachedAtUtc = null,
    TimeSpan? Age = null)
{
    /// <summary>True only when a cache was served past its freshness window because the remote was unreachable.</summary>
    public bool IsStale => State == OsCatalogFreshnessState.StaleCache;
}

/// <summary>
/// Exposes the catalog's <see cref="OsCatalogFreshness"/> so a front end can read it after loading entries.
/// Implemented by the remote source (and forwarded by the composite) without callers knowing the internals.
/// </summary>
public interface IOsCatalogFreshnessProvider
{
    /// <summary>Returns the current freshness snapshot; <see cref="OsCatalogFreshnessState.Unknown"/> before the first load.</summary>
    OsCatalogFreshness GetFreshness();
}
