using System.Text.Json;

namespace Boxwright.Qmp;

/// <summary>
/// An asynchronous QMP event emitted by QEMU (for example <c>SHUTDOWN</c>,
/// <c>RESET</c>, <c>STOP</c>, <c>RESUME</c>, <c>POWERDOWN</c>). Events arrive
/// unsolicited and are surfaced via <see cref="IQmpClient.Events"/>.
/// </summary>
/// <param name="Name">The event name, e.g. <c>"SHUTDOWN"</c>.</param>
/// <param name="Data">The event's <c>data</c> payload; an undefined element when absent.</param>
/// <param name="TimestampSeconds">Seconds component of the QEMU event timestamp.</param>
/// <param name="TimestampMicroseconds">Microseconds component of the QEMU event timestamp.</param>
public sealed record QmpEvent(
    string Name,
    JsonElement Data,
    long TimestampSeconds,
    long TimestampMicroseconds);
