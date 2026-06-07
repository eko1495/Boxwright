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

    /// <summary>
    /// Presses a chord of keys in the guest via QMP <c>send-key</c>. Each entry is a QEMU <c>qcode</c>
    /// (e.g. <c>ret</c>, <c>spc</c>, <c>esc</c>); QEMU presses them together and releases them. Useful to
    /// drive a boot-time firmware prompt — e.g. Windows Setup's "Press any key to boot from CD or DVD…" —
    /// with no human at the console. <paramref name="holdTimeMs"/> sets the QMP <c>hold-time</c> (how long
    /// the keys stay down before release); a longer hold helps a firmware poll that samples the keyboard
    /// only periodically actually register the press. Omitted (QEMU default) when <see langword="null"/>.
    /// </summary>
    public static Task SendKeyAsync(this IQmpClient client, IReadOnlyList<string> qcodes, int? holdTimeMs = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(qcodes);

        var args = new Dictionary<string, object>
        {
            ["keys"] = qcodes.Select(code => new { type = "qcode", data = code }).ToArray(),
        };
        if (holdTimeMs is { } ms)
        {
            args["hold-time"] = ms;
        }

        return client.ExecuteAsync("send-key", args, cancellationToken);
    }

    /// <summary>
    /// Sends a single key <b>press</b> (<paramref name="down"/> = <see langword="true"/>) or
    /// <b>release</b> event via QMP <c>input-send-event</c> for a QEMU <c>qcode</c> (e.g. <c>ret</c>).
    /// Unlike <see cref="SendKeyAsync"/> (which presses and immediately releases), this lets the caller
    /// <b>hold</b> a key down and release it later — a continuously-held key is reliably seen by a firmware
    /// prompt that polls the keyboard only briefly (e.g. "Press any key to boot from CD…"), where discrete
    /// presses can race the poll and be missed.
    /// </summary>
    public static Task SendKeyEventAsync(this IQmpClient client, string qcode, bool down, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(qcode);

        var args = new
        {
            events = new[]
            {
                new { type = "key", data = new { down, key = new { type = "qcode", data = qcode } } },
            },
        };
        return client.ExecuteAsync("input-send-event", args, cancellationToken);
    }
}

/// <summary>Cumulative block-device byte counters from <c>query-blockstats</c>, summed across devices.</summary>
public readonly record struct QmpBlockStats(long ReadBytes, long WriteBytes);
