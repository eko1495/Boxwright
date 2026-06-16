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

    /// <summary>Number of corruptions repaired this run (0 unless a repair was requested).</summary>
    [JsonPropertyName("corruptions-fixed")]
    public long CorruptionsFixed { get; init; }

    /// <summary>Number of leaked clusters reclaimed this run (0 unless a repair was requested).</summary>
    [JsonPropertyName("leaks-fixed")]
    public long LeaksFixed { get; init; }

    /// <summary>
    /// True when the image is consistent: no <b>unfixed</b> corruptions and no check errors (leaks are
    /// tolerated). For a read-only check nothing is fixed, so this is "no corruptions found"; after a
    /// repair it's "every found corruption was fixed" (<see cref="Corruptions"/> counts those found, not
    /// those remaining — qemu-img reports the fixed count separately).
    /// </summary>
    [JsonIgnore]
    public bool Healthy => Corruptions - CorruptionsFixed <= 0 && CheckErrors == 0;

    /// <summary>True when this run repaired anything (corruptions or leaks fixed).</summary>
    [JsonIgnore]
    public bool Repaired => CorruptionsFixed > 0 || LeaksFixed > 0;
}
