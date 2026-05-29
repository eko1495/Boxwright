using System.Text.Json.Serialization;

namespace Boxwright.Qmp;

/// <summary>
/// The VM run state returned by the QMP <c>query-status</c> command.
/// </summary>
/// <param name="Status">The QEMU run state, e.g. <c>"running"</c>, <c>"paused"</c>, <c>"prelaunch"</c>.</param>
/// <param name="Running">True when the guest CPUs are executing.</param>
public sealed record QmpVmStatus(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("running")] bool Running);
