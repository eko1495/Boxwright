namespace Boxwright.App.Services;

/// <summary>
/// Opens an in-app VNC display window for a running VM. A UI seam (like <see cref="IFilePicker"/>)
/// so view models don't construct windows directly. The default implementation renders the VM's
/// QEMU <c>-vnc</c> server with the embedded VncView (see <c>AvaloniaVncDisplay</c>).
/// </summary>
public interface IEmbeddedVncDisplay
{
    /// <summary>Opens a window that connects to and renders the VM's display at <paramref name="host"/>:<paramref name="port"/>.</summary>
    void Open(string title, string host, int port);
}
