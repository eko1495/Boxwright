namespace Boxwright.Core;

/// <summary>
/// Opens a VM's display by launching the external <c>remote-viewer</c> (SPICE)
/// — implemented by <see cref="DisplayLauncher"/> (ADR-0004). Abstracted so the UI
/// can be unit-tested without spawning a viewer.
/// </summary>
public interface IDisplayLauncher
{
    /// <summary>Launches <c>remote-viewer</c> against the SPICE server at <paramref name="host"/>:<paramref name="spicePort"/>.</summary>
    /// <exception cref="DisplayException"><c>remote-viewer</c> could not be found.</exception>
    void Launch(int spicePort, string host = "127.0.0.1");
}
