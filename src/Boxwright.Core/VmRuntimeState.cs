using Boxwright.Qmp;

namespace Boxwright.Core;

/// <summary>
/// The ephemeral runtime identity of a launched VM, persisted to <c>runtime.json</c> in the VM folder
/// so a restarted Boxwright can re-adopt the still-running QEMU process — reconnect its QMP control
/// channel — instead of orphaning it (ADR-0014). Written at launch and cleared when the VM stops
/// (see <see cref="VmRuntimeStore"/>). The QMP endpoint is stored flattened so <c>Boxwright.Qmp</c>
/// stays free of any JSON shape; rebuild it with <see cref="ToQmpEndpoint"/>.
/// </summary>
public sealed record VmRuntimeState
{
    /// <summary>The runtime-state schema version this build reads and writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Versions the runtime-state format.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>The QEMU process id to re-adopt.</summary>
    public int ProcessId { get; init; }

    /// <summary>QMP transport (TCP on Windows, Unix socket elsewhere).</summary>
    public QmpTransport QmpTransport { get; init; }

    /// <summary>QMP host for a TCP endpoint.</summary>
    public string QmpHost { get; init; } = string.Empty;

    /// <summary>QMP port for a TCP endpoint.</summary>
    public int QmpPort { get; init; }

    /// <summary>QMP socket path for a Unix endpoint.</summary>
    public string QmpSocketPath { get; init; } = string.Empty;

    /// <summary>The display server port (SPICE or VNC).</summary>
    public int SpicePort { get; init; }

    /// <summary>The display protocol the VM was launched with (<c>spice</c> or <c>vnc</c>).</summary>
    public string DisplayProtocol { get; init; } = "spice";

    /// <summary>The guest-agent channel port.</summary>
    public int GuestAgentPort { get; init; }

    /// <summary>The accelerator the VM was launched with.</summary>
    public Accelerator Accelerator { get; init; }

    /// <summary>Rebuilds the QMP endpoint to reconnect to.</summary>
    public QmpEndpoint ToQmpEndpoint() => QmpTransport == QmpTransport.Unix
        ? QmpEndpoint.UnixSocket(QmpSocketPath)
        : QmpEndpoint.Tcp(QmpHost, QmpPort);

    /// <summary>Captures the runtime identity of a freshly-launched VM.</summary>
    public static VmRuntimeState From(
        int processId, QmpEndpoint qmp, int spicePort, string displayProtocol, int guestAgentPort, Accelerator accelerator)
    {
        ArgumentNullException.ThrowIfNull(qmp);
        return new VmRuntimeState
        {
            ProcessId = processId,
            QmpTransport = qmp.Transport,
            QmpHost = qmp.Host,
            QmpPort = qmp.Port,
            QmpSocketPath = qmp.SocketPath,
            SpicePort = spicePort,
            DisplayProtocol = displayProtocol,
            GuestAgentPort = guestAgentPort,
            Accelerator = accelerator,
        };
    }
}
