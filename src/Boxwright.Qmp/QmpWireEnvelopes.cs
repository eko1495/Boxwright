using System.Text.Json;
using System.Text.Json.Serialization;

namespace Boxwright.Qmp;

// Internal data-transfer types mirroring the QMP wire formats. They are exposed
// to the test project via InternalsVisibleTo. The read/dispatch logic that uses
// them is implemented later (backlog QMP-3/QMP-4/QMP-5).

/// <summary>Wire form of a QMP command: <c>{"execute":…,"arguments":…,"id":…}</c>.</summary>
internal sealed class QmpCommandEnvelope
{
    [JsonPropertyName("execute")]
    public required string Execute { get; init; }

    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Arguments { get; init; }

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Id { get; init; }
}

/// <summary>Wire form of a QMP reply: exactly one of <c>return</c> or <c>error</c>, with the correlating <c>id</c>.</summary>
internal sealed class QmpReplyEnvelope
{
    [JsonPropertyName("return")]
    public JsonElement? Return { get; init; }

    [JsonPropertyName("error")]
    public QmpErrorBody? Error { get; init; }

    [JsonPropertyName("id")]
    public long? Id { get; init; }
}

/// <summary>The <c>error</c> object of a failed QMP reply.</summary>
internal sealed class QmpErrorBody
{
    [JsonPropertyName("class")]
    public string? Class { get; init; }

    [JsonPropertyName("desc")]
    public string? Desc { get; init; }
}

/// <summary>Wire form of an unsolicited QMP event.</summary>
internal sealed class QmpEventEnvelope
{
    [JsonPropertyName("event")]
    public string? Event { get; init; }

    [JsonPropertyName("data")]
    public JsonElement Data { get; init; }

    [JsonPropertyName("timestamp")]
    public QmpTimestamp? Timestamp { get; init; }
}

/// <summary>The <c>timestamp</c> of a QMP event.</summary>
internal sealed class QmpTimestamp
{
    [JsonPropertyName("seconds")]
    public long Seconds { get; init; }

    [JsonPropertyName("microseconds")]
    public long Microseconds { get; init; }
}

/// <summary>Wire form of the QMP greeting banner: <c>{"QMP":{"version":…,"capabilities":[…]}}</c>.</summary>
internal sealed class QmpGreetingEnvelope
{
    [JsonPropertyName("QMP")]
    public QmpGreetingBody? Qmp { get; init; }
}

/// <summary>The body of the QMP greeting.</summary>
internal sealed class QmpGreetingBody
{
    [JsonPropertyName("version")]
    public JsonElement Version { get; init; }

    [JsonPropertyName("capabilities")]
    public IReadOnlyList<string>? Capabilities { get; init; }
}
