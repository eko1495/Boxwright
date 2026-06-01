using Boxwright.Qmp;

namespace Boxwright.Core;

/// <summary>
/// The per-launch values resolved at start time and fed into the
/// <see cref="CommandLineBuilder"/>: the QMP control endpoint, the display
/// (SPICE/VNC) port, and the UEFI firmware path (required only for UEFI VMs).
/// </summary>
public sealed record QemuLaunchContext
{
    /// <summary>The QMP control endpoint, allocated per launch.</summary>
    public required QmpEndpoint QmpEndpoint { get; init; }

    /// <summary>The display server port (SPICE, or VNC).</summary>
    public int SpicePort { get; init; }

    /// <summary>The QEMU Guest Agent channel port (TCP, loopback), allocated per launch.</summary>
    public int GuestAgentPort { get; init; }

    /// <summary>Read-only UEFI firmware CODE image (OVMF); required when the config uses UEFI.</summary>
    public string? UefiCodePath { get; init; }

    /// <summary>Writable per-VM UEFI VARS (NVRAM) image; required when the config uses UEFI.</summary>
    public string? UefiVarsPath { get; init; }
}
