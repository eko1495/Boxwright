using Microsoft.Extensions.Logging;

namespace Boxwright.Core;

/// <summary>
/// Merges several <see cref="IOsCatalogSource"/>s into one catalog (ADR-0026): entries from later sources
/// override earlier ones by id, so local recipes can both add new OSes and pin/replace a built-in or
/// remote entry. First-seen order is preserved. A source that throws is logged and skipped, so one broken
/// source never empties the catalog.
/// </summary>
public sealed class CompositeOsCatalogSource : IOsCatalogSource, IOsCatalogFreshnessProvider
{
    private readonly IReadOnlyList<IOsCatalogSource> _sources;
    private readonly ILogger? _logger;

    /// <summary>Creates a composite over <paramref name="sources"/>, in precedence order (later wins).</summary>
    public CompositeOsCatalogSource(IReadOnlyList<IOsCatalogSource> sources, ILogger<CompositeOsCatalogSource>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var byId = new Dictionary<string, OsCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (IOsCatalogSource source in _sources)
        {
            IReadOnlyList<OsCatalogEntry> entries;
            try
            {
                entries = await source.GetEntriesAsync(cancellationToken);
            }
            catch (OsCatalogException ex)
            {
                _logger?.LogWarning(ex, "Skipping an OS catalog source: {Message}", ex.Message);
                continue;
            }

            foreach (OsCatalogEntry entry in entries)
            {
                if (!byId.ContainsKey(entry.Id))
                {
                    order.Add(entry.Id);
                }

                byId[entry.Id] = entry; // later source wins
            }
        }

        return order.Select(id => byId[id]).ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Forwards the freshness of the first wrapped source that tracks it (the <see cref="RemoteOsCatalogSource"/>),
    /// so callers holding only the composite can still surface a stale cache (ADR-0020). Returns
    /// <see cref="OsCatalogFreshnessState.Unknown"/> when no wrapped source reports freshness.
    /// </remarks>
    public OsCatalogFreshness GetFreshness()
    {
        foreach (IOsCatalogSource source in _sources)
        {
            if (source is IOsCatalogFreshnessProvider provider)
            {
                return provider.GetFreshness();
            }
        }

        return new OsCatalogFreshness(OsCatalogFreshnessState.Unknown);
    }
}
