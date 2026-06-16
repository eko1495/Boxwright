using System.Text.Json.Serialization;

namespace Boxwright.Core;

/// <summary>
/// The result of a consistency check on one disk image, parsed from <c>qemu-img check --output=json</c>.
/// <see cref="Corruptions"/> mean the image is damaged; <see cref="Leaks"/> are wasted-but-safe clusters
/// (recoverable, not corruption). <see cref="Healthy"/> folds these into a single verdict.
/// </summary>
public sealed record DiskCheckResult
{
    /// <summary>Number of corruptions found (any &gt; 0 means the image is damaged).</summary>
    [JsonPropertyName("corruptions")]
    public long Corruptions { get; init; }

    /// <summary>Number of leaked clusters (allocated but unreferenced — wasted space, not corruption).</summary>
    [JsonPropertyName("leaks")]
    public long Leaks { get; init; }

    /// <summary>Number of errors the check itself hit while reading the image's metadata.</summary>
    [JsonPropertyName("check-errors")]
    public long CheckErrors { get; init; }

    /// <summary>True when the image is consistent: no corruptions and no check errors (leaks are tolerated).</summary>
    [JsonIgnore]
    public bool Healthy => Corruptions == 0 && CheckErrors == 0;
}
