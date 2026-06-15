using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Boxwright.Qmp;

/// <summary>
/// The default <see cref="IQmpClient"/>: a QMP client over a TCP (Windows) or
/// Unix-domain (Linux/macOS) socket. <see cref="ConnectAsync"/> performs the
/// greeting + <c>qmp_capabilities</c> handshake; a background read loop then
/// correlates replies to <see cref="ExecuteAsync(string, object?, CancellationToken)"/>
/// callers by id and routes events to <see cref="Events"/>.
/// </summary>
public sealed class QmpClient : IQmpClient
{
    private const string CapabilitiesCommand = "{\"execute\":\"qmp_capabilities\"}";
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private readonly EventStream _events = new();
    private readonly Action<string>? _onSent;
    private readonly Action<string>? _onReceived;

    private TcpClient? _tcpClient;
    private Socket? _unixSocket;
    private Stream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource? _loopCts;
    private Task? _readLoopTask;
    private QmpSchema? _schema;
    private long _nextId;
    private volatile bool _connected;

    /// <summary>
    /// Creates a QMP client. The optional diagnostic hooks are invoked with each raw JSON
    /// line sent to and received from the server — intended for traffic logging. They keep
    /// this library dependency-free (no logging framework leaks in). Hook exceptions are
    /// swallowed, so a misbehaving hook can never disrupt the protocol or the read loop.
    /// </summary>
    /// <param name="onSent">Invoked with each JSON line written to the server, or <see langword="null"/>.</param>
    /// <param name="onReceived">Invoked with each JSON line read from the server, or <see langword="null"/>.</param>
    public QmpClient(Action<string>? onSent = null, Action<string>? onReceived = null)
    {
        _onSent = onSent;
        _onReceived = onReceived;
    }

    /// <inheritdoc />
    public bool IsConnected => _connected;

    /// <inheritdoc />
    public IObservable<QmpEvent> Events => _events;

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

        try
        {
            _stream = await OpenStreamAsync(endpoint, cancellationToken);
            _reader = new StreamReader(_stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
            _writer = new StreamWriter(_stream, Utf8, bufferSize: 4096, leaveOpen: true) { NewLine = "\n", AutoFlush = true };

            // 1) The server speaks first with a {"QMP": ...} greeting banner.
            string greetingLine = await ReadLineExpectedAsync("the QMP greeting", cancellationToken);
            QmpGreetingEnvelope? greeting;
            try
            {
                greeting = JsonSerializer.Deserialize(greetingLine, QmpJsonContext.Default.QmpGreetingEnvelope);
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
            await SendLineAsync(CapabilitiesCommand, cancellationToken);

            // 3) Expect a success reply ({"return": {}}); an error means the handshake failed.
            string replyLine = await ReadLineExpectedAsync("the qmp_capabilities reply", cancellationToken);
            QmpReplyEnvelope reply = ParseReply(replyLine);
            if (reply.Error is not null)
            {
                throw new QmpCommandException(reply.Error.Class ?? "GenericError", reply.Error.Desc ?? "qmp_capabilities was rejected.");
            }

            _connected = true;
            _loopCts = new CancellationTokenSource();
            _readLoopTask = ReadLoopAsync(_loopCts.Token);
        }
        catch
        {
            // A failed connect/handshake must not leak the socket/stream, and must leave the instance
            // reusable for a retry. The read loop never started, so this is a straight teardown of what
            // OpenStreamAsync and the handshake allocated; DisposeAsync owns teardown once it has.
            CloseConnectionResources();
            throw;
        }
    }

    // Disposes the transport/stream opened during ConnectAsync and clears the fields, so a failed
    // connection neither leaks resources nor trips the "already been used" guard on a later retry.
    private void CloseConnectionResources()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            // The peer may already be gone; there is nothing to flush.
        }

        _reader?.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        _unixSocket?.Dispose();

        _writer = null;
        _reader = null;
        _stream = null;
        _tcpClient = null;
        _unixSocket = null;
    }

    /// <inheritdoc />
    public async Task<JsonElement> ExecuteAsync(string command, object? arguments = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (!_connected)
        {
            throw new InvalidOperationException("The QMP client is not connected.");
        }

        long id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        try
        {
            string json = JsonSerializer.Serialize(new QmpCommandEnvelope { Execute = command, Arguments = arguments, Id = id });
            await SendLineAsync(json, cancellationToken);

            await using (cancellationToken.Register(static state => ((TaskCompletionSource<JsonElement>)state!).TrySetCanceled(), tcs))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteAsync<TResult>(string command, object? arguments = null, CancellationToken cancellationToken = default)
    {
        JsonElement payload = await ExecuteAsync(command, arguments, cancellationToken);
        TResult? result = payload.Deserialize<TResult>();
        if (result is null)
        {
            throw new QmpProtocolException($"The '{command}' reply could not be deserialized to {typeof(TResult).Name}.");
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<QmpSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        QmpSchema? cached = _schema;
        if (cached is not null)
        {
            return cached;
        }

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_schema is null)
            {
                JsonElement payload = await ExecuteAsync("query-qmp-schema", arguments: null, cancellationToken);
                List<QmpSchemaEntry> entries = payload.Deserialize(QmpJsonContext.Default.ListQmpSchemaEntry) ?? [];
                _schema = new QmpSchema(entries);
            }

            return _schema;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _connected = false;
        if (_loopCts is not null)
        {
            await _loopCts.CancelAsync();
        }

        if (_readLoopTask is not null)
        {
            await _readLoopTask;
        }
        else
        {
            CancelPending();
        }

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
        _loopCts?.Dispose();
        _writeLock.Dispose();
        _schemaLock.Dispose();
        _events.Complete();
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? line;
            while (!cancellationToken.IsCancellationRequested && (line = await _reader!.ReadLineAsync(cancellationToken)) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                NotifyTrace(_onReceived, line);
                Dispatch(line);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
        {
            // Loop cancelled (dispose) or connection dropped.
        }
        finally
        {
            _connected = false;
            if (cancellationToken.IsCancellationRequested)
            {
                CancelPending();
            }
            else
            {
                FailPending(new QmpProtocolException("The QMP connection was closed unexpectedly."));
            }
        }
    }

    private void Dispatch(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("event", out _))
            {
                QmpEventEnvelope? evt = JsonSerializer.Deserialize(line, QmpJsonContext.Default.QmpEventEnvelope);
                if (evt?.Event is not null)
                {
                    _events.Publish(new QmpEvent(
                        evt.Event,
                        evt.Data,
                        evt.Timestamp?.Seconds ?? 0,
                        evt.Timestamp?.Microseconds ?? 0));
                }

                return;
            }

            bool hasReturn = root.TryGetProperty("return", out _);
            bool hasError = root.TryGetProperty("error", out JsonElement errorElement);
            if (!hasReturn && !hasError)
            {
                return; // Not a reply we can act on.
            }

            if (!root.TryGetProperty("id", out JsonElement idElement) || idElement.ValueKind != JsonValueKind.Number)
            {
                return; // No id to correlate by.
            }

            if (!_pending.TryRemove(idElement.GetInt64(), out var tcs))
            {
                return; // Already cancelled, or unknown id.
            }

            if (hasError)
            {
                string errorClass = errorElement.TryGetProperty("class", out var c) ? c.GetString() ?? "GenericError" : "GenericError";
                string description = errorElement.TryGetProperty("desc", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                tcs.TrySetException(new QmpCommandException(errorClass, description));
            }
            else
            {
                tcs.TrySetResult(root.GetProperty("return").Clone());
            }
        }
        catch (JsonException)
        {
            // Ignore malformed lines.
        }
    }

    private void CancelPending()
    {
        foreach (long id in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(id, out var tcs))
            {
                tcs.TrySetCanceled();
            }
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (long id in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(id, out var tcs))
            {
                tcs.TrySetException(exception);
            }
        }
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

        if (line is null)
        {
            throw new QmpProtocolException($"The QMP server closed the connection before sending {what}.");
        }

        NotifyTrace(_onReceived, line);
        return line;
    }

    private async Task SendLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _writer!.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        NotifyTrace(_onSent, line);
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A diagnostic trace hook (e.g. logging) must never disrupt the QMP protocol or fault the read loop; all hook exceptions are intentionally swallowed.")]
    private static void NotifyTrace(Action<string>? hook, string line)
    {
        if (hook is null)
        {
            return;
        }

        try
        {
            hook(line);
        }
        catch (Exception)
        {
            // Swallow: a diagnostic hook must never affect protocol behavior.
        }
    }

    private static QmpReplyEnvelope ParseReply(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, QmpJsonContext.Default.QmpReplyEnvelope)
                ?? throw new QmpProtocolException($"Received an empty QMP reply: '{line}'.");
        }
        catch (JsonException ex)
        {
            throw new QmpProtocolException($"Received a malformed QMP reply: '{line}'.", ex);
        }
    }

    // Minimal dependency-free multicast observable (resolves the QMP-5 surface question:
    // hand-rolled, no System.Reactive). Observer-exception isolation is a later refinement.
    private sealed class EventStream : IObservable<QmpEvent>
    {
        private readonly object _gate = new();
        private readonly List<IObserver<QmpEvent>> _observers = new();

        public IDisposable Subscribe(IObserver<QmpEvent> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            lock (_gate)
            {
                _observers.Add(observer);
            }

            return new Subscription(this, observer);
        }

        public void Publish(QmpEvent value)
        {
            IObserver<QmpEvent>[] snapshot;
            lock (_gate)
            {
                snapshot = _observers.ToArray();
            }

            foreach (IObserver<QmpEvent> observer in snapshot)
            {
                observer.OnNext(value);
            }
        }

        public void Complete()
        {
            IObserver<QmpEvent>[] snapshot;
            lock (_gate)
            {
                snapshot = _observers.ToArray();
                _observers.Clear();
            }

            foreach (IObserver<QmpEvent> observer in snapshot)
            {
                observer.OnCompleted();
            }
        }

        private void Remove(IObserver<QmpEvent> observer)
        {
            lock (_gate)
            {
                _observers.Remove(observer);
            }
        }

        private sealed class Subscription(EventStream stream, IObserver<QmpEvent> observer) : IDisposable
        {
            private EventStream? _stream = stream;

            public void Dispose()
            {
                _stream?.Remove(observer);
                _stream = null;
            }
        }
    }
}
