using System.Text.Json.Serialization;

namespace Boxwright.Core;

/// <summary>
/// A qcow2 internal snapshot, parsed from <c>qemu-img info --output=json</c>'s
/// <c>snapshots</c> array. The <see cref="Name"/> (tag) is what addresses it for
/// create / restore / delete.
/// </summary>
public sealed record VmSnapshot
{
    /// <summary>The qemu-img-assigned snapshot id (informational; the tag is used for addressing).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The snapshot tag — the user-facing name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>Size of the saved VM state in bytes (0 for a disk-only snapshot).</summary>
    [JsonPropertyName("vm-state-size")]
    public long VmStateSize { get; init; }

    /// <summary>Unix-seconds component of the creation time.</summary>
    [JsonPropertyName("date-sec")]
    public long DateSeconds { get; init; }

    /// <summary>The creation time (UTC), derived from <see cref="DateSeconds"/>.</summary>
    [JsonIgnore]
    public DateTimeOffset Created => DateTimeOffset.FromUnixTimeSeconds(DateSeconds);
}
