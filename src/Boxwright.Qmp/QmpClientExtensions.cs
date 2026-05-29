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
}
