using System.Net;
using System.Net.Sockets;
using Boxwright.Qmp;

namespace Boxwright.Core;

/// <summary>Allocates per-launch QMP and display endpoints.</summary>
public interface IEndpointAllocator
{
    /// <summary>Allocates a QMP endpoint for this host (TCP on Windows, Unix socket elsewhere).</summary>
    QmpEndpoint AllocateQmpEndpoint();

    /// <summary>
    /// Allocates a currently-free loopback TCP port (e.g. for the display server). When
    /// <paramref name="minPort"/> is positive, the chosen port is at or above it — VNC needs
    /// ≥ 5900 because its display number is <c>port − 5900</c>.
    /// </summary>
    int AllocateFreeTcpPort(int minPort = 0);
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
    public int AllocateFreeTcpPort(int minPort = 0)
    {
        if (minPort <= 0)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        // Scan upward for a free port at or above minPort (VNC display ports must be ≥ 5900).
        for (int port = minPort; port <= 65535; port++)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
                // Port in use — try the next one.
            }
        }

        throw new InvalidOperationException($"No free loopback TCP port available at or above {minPort}.");
    }
}
