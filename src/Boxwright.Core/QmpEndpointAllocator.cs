using System.Net;
using System.Net.Sockets;
using Boxwright.Qmp;

namespace Boxwright.Core;

/// <summary>Allocates per-launch QMP and display endpoints.</summary>
public interface IEndpointAllocator
{
    /// <summary>Allocates a QMP endpoint for this host (TCP on Windows, Unix socket elsewhere).</summary>
    QmpEndpoint AllocateQmpEndpoint();

    /// <summary>Allocates a currently-free loopback TCP port (e.g. for the display server).</summary>
    int AllocateFreeTcpPort();
}

/// <summary>
/// The default <see cref="IEndpointAllocator"/>: a free loopback TCP port on
/// Windows (AF_UNIX is unreliable there) or a unique Unix-domain socket path on
/// Linux/macOS for QMP, and free loopback TCP ports for the display server.
/// </summary>
public sealed class QmpEndpointAllocator : IEndpointAllocator
{
    /// <inheritdoc />
    public QmpEndpoint AllocateQmpEndpoint()
    {
        if (OperatingSystem.IsWindows())
        {
            return QmpEndpoint.Tcp("127.0.0.1", AllocateFreeTcpPort());
        }

        string socketPath = Path.Combine(Path.GetTempPath(), $"boxwright-qmp-{Guid.NewGuid():N}.sock");
        return QmpEndpoint.UnixSocket(socketPath);
    }

    /// <inheritdoc />
    public int AllocateFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
