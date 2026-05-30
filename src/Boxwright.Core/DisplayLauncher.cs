using Microsoft.Extensions.Logging;

namespace Boxwright.Core;

/// <summary>
/// Opens a VM's display by launching the external <c>remote-viewer</c> (SPICE)
/// against the VM's SPICE port — the MVP display strategy (ADR-0004). No display is
/// embedded. A missing viewer surfaces a clear, actionable error.
/// </summary>
public sealed class DisplayLauncher : IDisplayLauncher
{
    private readonly IProcessLauncher _processLauncher;
    private readonly IRemoteViewerLocator _locator;
    private readonly ILogger<DisplayLauncher> _logger;

    /// <summary>Creates a display launcher.</summary>
    public DisplayLauncher(IProcessLauncher processLauncher, IRemoteViewerLocator locator, ILogger<DisplayLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(logger);
        _processLauncher = processLauncher;
        _locator = locator;
        _logger = logger;
    }

    /// <summary>Launches <c>remote-viewer</c> against the display server at <paramref name="host"/>:<paramref name="port"/> using <paramref name="protocol"/> (<c>spice</c> or <c>vnc</c>).</summary>
    /// <exception cref="DisplayException"><c>remote-viewer</c> could not be found.</exception>
    public void Launch(int port, string protocol = "spice", string host = "127.0.0.1")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        string scheme = string.Equals(protocol, "vnc", StringComparison.OrdinalIgnoreCase) ? "vnc" : "spice";

        string? viewer = _locator.Locate();
        if (viewer is null)
        {
            _logger.LogWarning("remote-viewer not found; cannot open the {Scheme} display.", scheme);
            throw new DisplayException(
                "remote-viewer (from virt-viewer) was not found. Install virt-viewer, or ensure it is on PATH.");
        }

        _logger.LogInformation("Opening display via {Viewer} ({Scheme}://{Host}:{Port}).", viewer, scheme, host, port);

        // We don't supervise remote-viewer; launch it detached and release our handle.
        using (_processLauncher.Start(new ProcessLaunchRequest
        {
            Executable = viewer,
            Arguments = [$"{scheme}://{host}:{port}"],
        }))
        {
        }
    }
}
