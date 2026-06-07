using System.Text.Json;

namespace Boxwright.Qmp;

/// <summary>
/// Typed convenience wrappers over <see cref="IQmpClient"/> for common QMP
/// queries. They are thin shims over <c>ExecuteAsync</c>.
/// </summary>
public static class QmpClientExtensions
{
    /// <summary>Runs <c>query-status</c> and returns the typed VM run state.</summary>
    public static Task<QmpVmStatus> QueryStatusAsync(this IQmpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.ExecuteAsync<QmpVmStatus>("query-status", arguments: null, cancellationToken);
    }

    /// <summary>Runs <c>query-name</c> and returns the guest name, or <see langword="null"/> when none is set.</summary>
    public static async Task<string?> QueryNameAsync(this IQmpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        JsonElement result = await client.ExecuteAsync("query-name", arguments: null, cancellationToken);
        return result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("name", out JsonElement name)
            && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
    }

    /// <summary>
    /// Runs <c>query-blockstats</c> and sums the cumulative read/write byte counters across all block
    /// devices. The counters only ever grow, so a caller differences successive samples to get a live
    /// disk-throughput rate. Defensive against the slightly varying per-version shape (missing fields → 0).
    /// </summary>
    public static async Task<QmpBlockStats> QueryBlockStatsAsync(this IQmpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        JsonElement result = await client.ExecuteAsync("query-blockstats", arguments: null, cancellationToken);

        long read = 0;
        long write = 0;
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement device in result.EnumerateArray())
            {
                if (device.TryGetProperty("stats", out JsonElement stats) && stats.ValueKind == JsonValueKind.Object)
                {
                    read += ReadLong(stats, "rd_bytes");
                    write += ReadLong(stats, "wr_bytes");
                }
            }
        }

        return new QmpBlockStats(read, write);
    }

    private static long ReadLong(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
}

/// <summary>Cumulative block-device byte counters from <c>query-blockstats</c>, summed across devices.</summary>
public readonly record struct QmpBlockStats(long ReadBytes, long WriteBytes);
