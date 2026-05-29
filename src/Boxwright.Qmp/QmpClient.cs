using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Boxwright.Qmp;

/// <summary>
/// The default <see cref="IQmpClient"/>: a QMP client over a TCP (Windows) or
/// Unix-domain (Linux/macOS) socket. <see cref="ConnectAsync"/> opens the socket,
/// reads the greeting, and performs the <c>qmp_capabilities</c> handshake.
/// </summary>
/// <remarks>
/// Correlated command execution and the <see cref="Events"/> stream are
/// implemented in later milestones (backlog QMP-4/QMP-5); calling
/// <c>ExecuteAsync</c> before then throws, and <see cref="Events"/> is inert.
/// </remarks>
public sealed class QmpClient : IQmpClient
{
    private const string CapabilitiesCommand = "{\"execute\":\"qmp_capabilities\"}";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private TcpClient? _tcpClient;
    private Socket? _unixSocket;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private volatile bool _connected;

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public IObservable<QmpEvent> Events => NoOpObservable.Instance; // Replaced by a real stream in QMP-5.

    /// <inheritdoc />
    public async Task ConnectAsync(QmpEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (_connected)
        {
            throw new InvalidOperationException("The QMP client is already connected.");
        }

        if (_stream is not null)
        {
            throw new InvalidOperationException("This QMP client has already been used; create a new instance to reconnect.");
        }

        _stream = await OpenStreamAsync(endpoint, cancellationToken);
        _reader = new StreamReader(_stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        _writer = new StreamWriter(_stream, Utf8, bufferSize: 4096, leaveOpen: true) { NewLine = "\n", AutoFlush = true };

        // 1) The server speaks first with a {"QMP": ...} greeting banner.
        string greetingLine = await ReadLineExpectedAsync("the QMP greeting", cancellationToken);
        QmpGreetingEnvelope? greeting;
        try
        {
            greeting = JsonSerializer.Deserialize<QmpGreetingEnvelope>(greetingLine);
        }
        catch (JsonException ex)
        {
            throw new QmpProtocolException($"The QMP greeting was not valid JSON: '{greetingLine}'.", ex);
        }

        if (greeting?.Qmp is null)
        {
            throw new QmpProtocolException($"Expected a QMP greeting banner but received: '{greetingLine}'.");
        }

        // 2) qmp_capabilities must be sent before any other command.
        await _writer.WriteLineAsync(CapabilitiesCommand.AsMemory(), cancellationToken);

        // 3) Expect a success reply ({"return": {}}); an error means the handshake failed.
        string replyLine = await ReadLineExpectedAsync("the qmp_capabilities reply", cancellationToken);
        QmpReplyEnvelope reply = ParseReply(replyLine);
        if (reply.Error is not null)
        {
            throw new QmpCommandException(reply.Error.Class ?? "GenericError", reply.Error.Desc ?? "qmp_capabilities was rejected.");
        }

        _connected = true;
    }

    /// <inheritdoc />
    public Task<JsonElement> ExecuteAsync(string command, object? arguments = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Command execution is implemented in a later milestone (backlog QMP-4).");

    /// <inheritdoc />
    public Task<TResult> ExecuteAsync<TResult>(string command, object? arguments = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Command execution is implemented in a later milestone (backlog QMP-4/QMP-6).");

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _connected = false;
        try
        {
            _writer?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            // The peer may already be gone; there is nothing to flush.
        }

        _reader?.Dispose();
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        _tcpClient?.Dispose();
        _unixSocket?.Dispose();
    }

    private async Task<Stream> OpenStreamAsync(QmpEndpoint endpoint, CancellationToken cancellationToken)
    {
        switch (endpoint.Transport)
        {
            case QmpTransport.Tcp:
                var tcp = new TcpClient();
                _tcpClient = tcp;
                await tcp.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
                return tcp.GetStream();

            case QmpTransport.Unix:
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                _unixSocket = socket;
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.SocketPath), cancellationToken);
                return new NetworkStream(socket, ownsSocket: false);

            default:
                throw new NotSupportedException($"Unsupported QMP transport '{endpoint.Transport}'.");
        }
    }

    private async Task<string> ReadLineExpectedAsync(string what, CancellationToken cancellationToken)
    {
        string? line;
        try
        {
            line = await _reader!.ReadLineAsync(cancellationToken);
        }
        catch (IOException ex)
        {
            throw new QmpProtocolException($"The connection failed while reading {what}.", ex);
        }

        return line ?? throw new QmpProtocolException($"The QMP server closed the connection before sending {what}.");
    }

    private static QmpReplyEnvelope ParseReply(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<QmpReplyEnvelope>(line)
                ?? throw new QmpProtocolException($"Received an empty QMP reply: '{line}'.");
        }
        catch (JsonException ex)
        {
            throw new QmpProtocolException($"Received a malformed QMP reply: '{line}'.", ex);
        }
    }

    private sealed class NoOpObservable : IObservable<QmpEvent>
    {
        public static readonly NoOpObservable Instance = new();

        public IDisposable Subscribe(IObserver<QmpEvent> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            return NoOpSubscription.Instance;
        }
    }

    private sealed class NoOpSubscription : IDisposable
    {
        public static readonly NoOpSubscription Instance = new();

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
