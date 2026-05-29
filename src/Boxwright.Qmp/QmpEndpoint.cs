namespace Boxwright.Qmp;

/// <summary>The transport used to reach a QMP server.</summary>
public enum QmpTransport
{
    /// <summary>A TCP socket on loopback — the transport used on Windows.</summary>
    Tcp,

    /// <summary>A Unix domain socket — the transport used on Linux and macOS.</summary>
    Unix,
}

/// <summary>
/// The address of a QEMU QMP monitor. QEMU is reached over TCP on Windows
/// (AF_UNIX is not reliable there) and over a Unix domain socket on Linux/macOS.
/// Construct via <see cref="Tcp"/> or <see cref="UnixSocket"/>.
/// </summary>
public sealed record QmpEndpoint
{
    private QmpEndpoint(QmpTransport transport, string host, int port, string socketPath)
    {
        Transport = transport;
        Host = host;
        Port = port;
        SocketPath = socketPath;
    }

    /// <summary>The transport used to reach the server.</summary>
    public QmpTransport Transport { get; }

    /// <summary>Host or IP for a <see cref="QmpTransport.Tcp"/> endpoint; otherwise empty.</summary>
    public string Host { get; }

    /// <summary>Port for a <see cref="QmpTransport.Tcp"/> endpoint; otherwise 0.</summary>
    public int Port { get; }

    /// <summary>Socket file path for a <see cref="QmpTransport.Unix"/> endpoint; otherwise empty.</summary>
    public string SocketPath { get; }

    /// <summary>Creates a TCP endpoint, e.g. <c>QmpEndpoint.Tcp("127.0.0.1", 4444)</c>.</summary>
    /// <param name="host">Host name or IP (typically <c>127.0.0.1</c>).</param>
    /// <param name="port">TCP port, 1–65535.</param>
    public static QmpEndpoint Tcp(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        return new QmpEndpoint(QmpTransport.Tcp, host, port, string.Empty);
    }

    /// <summary>Creates a Unix-domain-socket endpoint (Linux/macOS).</summary>
    /// <param name="socketPath">Filesystem path of the QMP socket.</param>
    public static QmpEndpoint UnixSocket(string socketPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(socketPath);
        return new QmpEndpoint(QmpTransport.Unix, string.Empty, 0, socketPath);
    }

    /// <summary>Returns a QMP-style address such as <c>tcp:127.0.0.1:4444</c> or <c>unix:/path</c>.</summary>
    public override string ToString() =>
        Transport == QmpTransport.Tcp ? $"tcp:{Host}:{Port}" : $"unix:{SocketPath}";
}
