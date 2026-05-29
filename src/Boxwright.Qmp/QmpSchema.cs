using System.Text.Json.Serialization;

namespace Boxwright.Qmp;

/// <summary>
/// A parsed view of QEMU's QMP schema (from <c>query-qmp-schema</c>), exposing the
/// set of supported command and event names so callers can feature-detect instead
/// of guessing at the installed QEMU's capabilities.
/// </summary>
public sealed class QmpSchema
{
    private readonly HashSet<string> _commands;
    private readonly HashSet<string> _events;

    internal QmpSchema(IReadOnlyList<QmpSchemaEntry> entries)
    {
        _commands = new HashSet<string>(StringComparer.Ordinal);
        _events = new HashSet<string>(StringComparer.Ordinal);
        foreach (QmpSchemaEntry entry in entries)
        {
            if (entry.Name is null)
            {
                continue;
            }

            switch (entry.MetaType)
            {
                case "command":
                    _commands.Add(entry.Name);
                    break;
                case "event":
                    _events.Add(entry.Name);
                    break;
            }
        }
    }

    /// <summary>The command names QEMU advertises.</summary>
    public IReadOnlyCollection<string> Commands => _commands;

    /// <summary>The event names QEMU advertises.</summary>
    public IReadOnlyCollection<string> Events => _events;

    /// <summary>Returns true if QEMU supports the given QMP command.</summary>
    public bool HasCommand(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _commands.Contains(name);
    }

    /// <summary>Returns true if QEMU can emit the given QMP event.</summary>
    public bool HasEvent(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _events.Contains(name);
    }
}

/// <summary>One entry of the <c>query-qmp-schema</c> array.</summary>
internal sealed class QmpSchemaEntry
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("meta-type")]
    public string? MetaType { get; init; }
}
