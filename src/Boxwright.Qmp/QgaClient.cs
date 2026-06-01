using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Boxwright.Qmp;

/// <summary>
/// A minimal QEMU Guest Agent client over a TCP channel. The guest-agent protocol is
/// QMP-like JSON but has NO greeting and NO capabilities handshake; requests are
/// serialized (one outstanding at a time) and correlated by order, not id. The TCP
/// connect succeeds as soon as QEMU's channel accepts it — whether the in-guest agent
/// actually answers is established with <see cref="PingAsync"/> (it stays silent when
/// qemu-guest-agent isn't installed/running).
/// </summary>
public sealed class QgaClient : IQgaClient
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _lock = new(1, 1);

    private TcpClient? _tcpClient;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    /// <inheritdoc />
    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (_stream is not null)
        {
            throw new InvalidOperationException("This QGA client has already been used; create a new instance to reconnect.");
        }

        var tcp = new TcpClient();
        _tcpClient = tcp;
        await tcp.ConnectAsync(host, port, cancellationToken);
        _stream = tcp.GetStream();
        _reader = new StreamReader(_stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        _writer = new StreamWriter(_stream, Utf8, bufferSize: 4096, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
    }

    /// <inheritdoc />
    public async Task<bool> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await ExecuteAsync("guest-ping", arguments: null, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is QmpProtocolException or QmpCommandException or IOException or SocketException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        // The agent powers off; its reply usually never arrives, so don't wait for one.
        SendAsync(new QmpCommandEnvelope { Execute = "guest-shutdown", Arguments = new { mode = "powerdown" } }, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetIpAddressesAsync(CancellationToken cancellationToken = default)
    {
        JsonElement result = await ExecuteAsync("guest-network-get-interfaces", arguments: null, cancellationToken);
        var addresses = new List<string>();
        if (result.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement iface in result.EnumerateArray())
            {
                if (!iface.TryGetProperty("ip-addresses", out JsonElement ips) || ips.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement ip in ips.EnumerateArray())
                {
                    string? address = ip.TryGetProperty("ip-address", out JsonElement value) ? value.GetString() : null;
                    if (!string.IsNullOrEmpty(address) && !IsUninteresting(address))
                    {
                        addresses.Add(address);
                    }
                }
            }
        }

        return addresses;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            // The peer may already be gone; nothing to flush.
        }

        _reader?.Dispose();
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        _tcpClient?.Dispose();
        _lock.Dispose();
    }

    // Loopback (127.0.0.1), IPv6 loopback (::1), and IPv6 link-local (fe80::/10) aren't useful to show.
    private static bool IsUninteresting(string address) =>
        address.StartsWith("127.", StringComparison.Ordinal) ||
        address == "::1" ||
        address.StartsWith("fe80", StringComparison.OrdinalIgnoreCase);

    private async Task<JsonElement> ExecuteAsync(string command, object? arguments, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await WriteLineAsync(new QmpCommandEnvelope { Execute = command, Arguments = arguments }, cancellationToken);

            string? line = await _reader!.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new QmpProtocolException("The guest-agent channel closed before replying.");
            }

            QmpReplyEnvelope reply = JsonSerializer.Deserialize(line, QmpJsonContext.Default.QmpReplyEnvelope)
                ?? throw new QmpProtocolException($"Received an empty guest-agent reply: '{line}'.");
            if (reply.Error is not null)
            {
                throw new QmpCommandException(reply.Error.Class ?? "GenericError", reply.Error.Desc ?? "The guest agent rejected the command.");
            }

            return reply.Return ?? default;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SendAsync(QmpCommandEnvelope envelope, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await WriteLineAsync(envelope, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task WriteLineAsync(QmpCommandEnvelope envelope, CancellationToken cancellationToken) =>
        _writer!.WriteLineAsync(JsonSerializer.Serialize(envelope).AsMemory(), cancellationToken);
}
