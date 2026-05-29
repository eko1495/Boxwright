using System.Text.Json.Serialization;

namespace Boxwright.Qmp;

/// <summary>
/// Source-generated JSON metadata for the internal QMP wire envelopes — trim/AOT-
/// friendly and faster than reflection. Covers the fixed-shape DESERIALIZE paths
/// (greeting, reply, event, and the schema array).
/// </summary>
/// <remarks>
/// The command-envelope serialize (which carries an arbitrary <c>object? Arguments</c>)
/// and the generic <see cref="IQmpClient.ExecuteAsync{TResult}"/> are inherently
/// polymorphic and remain reflection-based — the AOT boundary, revisited at QMP-8.
/// </remarks>
[JsonSerializable(typeof(QmpGreetingEnvelope))]
[JsonSerializable(typeof(QmpReplyEnvelope))]
[JsonSerializable(typeof(QmpEventEnvelope))]
[JsonSerializable(typeof(List<QmpSchemaEntry>))]
internal sealed partial class QmpJsonContext : JsonSerializerContext
{
}
