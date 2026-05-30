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

    /// <summary>Creates a display launcher.</summary>
    public DisplayLauncher(IProcessLauncher processLauncher, IRemoteViewerLocator locator)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(locator);
        _processLauncher = processLauncher;
        _locator = locator;
    }

    /// <summary>Launches <c>remote-viewer</c> against the SPICE server at <paramref name="host"/>:<paramref name="spicePort"/>.</summary>
    /// <exception cref="DisplayException"><c>remote-viewer</c> could not be found.</exception>
    public void Launch(int spicePort, string host = "127.0.0.1")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spicePort);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        string? viewer = _locator.Locate();
        if (viewer is null)
        {
            throw new DisplayException(
                "remote-viewer (from virt-viewer) was not found. Install virt-viewer, or ensure it is on PATH.");
        }

        // We don't supervise remote-viewer; launch it detached and release our handle.
        using (_processLauncher.Start(new ProcessLaunchRequest
        {
            Executable = viewer,
            Arguments = [$"spice://{host}:{spicePort}"],
        }))
        {
        }
    }
}
