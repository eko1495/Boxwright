using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Boxwright.Qmp.Tests;

/// <summary>
/// An in-process fake QMP server over loopback TCP, for testing the QMP client
/// without a live QEMU (CLAUDE.md §6). On connect it sends a greeting, answers
/// <c>qmp_capabilities</c>, returns scripted <c>return</c>/<c>error</c> replies
/// (echoing the command <c>id</c>), and can push unsolicited events on demand.
/// Command handlers run concurrently, so replies/events can arrive out of order —
/// which lets tests exercise the client's id-correlation. One client per instance.
/// </summary>
internal sealed class FakeQmpServer : IAsyncDisposable
{
    private const string DefaultGreeting =
        "{\"QMP\": {\"version\": {\"qemu\": {\"major\": 11, \"minor\": 0, \"micro\": 50}, " +
        "\"package\": \"fake\"}, \"capabilities\": []}}";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TcpListener _listener;
    private readonly string _greeting;
    private readonly ConcurrentDictionary<string, Func<long?, CancellationToken, Task<string>>> _handlers = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<Task> _responses = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly TaskCompletionSource _clientConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _serveTask;

    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    private FakeQmpServer(string greeting)
    {
        _greeting = greeting;
        _handlers["qmp_capabilities"] = (id, _) => Task.FromResult(SuccessReply("{}", id));
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _serveTask = ServeAsync(_cts.Token);
    }

    /// <summary>Starts a fake server on a free loopback port. Pass a custom <paramref name="greeting"/> to override the default banner.</summary>
    public static FakeQmpServer Start(string? greeting = null) => new(greeting ?? DefaultGreeting);

    /// <summary>The loopback TCP port the server is listening on.</summary>
    public int Port { get; }

    /// <summary>The endpoint a QMP client should connect to.</summary>
    public QmpEndpoint Endpoint => QmpEndpoint.Tcp("127.0.0.1", Port);

    /// <summary>Every raw command line the client sent (in arrival order) — lets tests assert the wire payload/arguments.</summary>
    public ConcurrentQueue<string> ReceivedCommandLines { get; } = new();

    /// <summary>Scripts a successful reply for <paramref name="command"/>, using <paramref name="returnJson"/> as the raw <c>return</c> payload.</summary>
    public FakeQmpServer OnCommand(string command, string returnJson)
    {
        _handlers[command] = (id, _) => Task.FromResult(SuccessReply(returnJson, id));
        return this;
    }

    /// <summary>Scripts an error reply for <paramref name="command"/>.</summary>
    public FakeQmpServer OnCommandError(string command, string errorClass, string description)
    {
        _handlers[command] = (id, _) => Task.FromResult(ErrorReply(errorClass, description, id));
        return this;
    }

    /// <summary>Scripts a reply for <paramref name="command"/> that is withheld until <paramref name="gate"/> completes — used to force out-of-order replies.</summary>
    public FakeQmpServer OnCommandGated(string command, string returnJson, Task gate)
    {
        _handlers[command] = async (id, ct) =>
        {
            await gate.WaitAsync(ct);
            return SuccessReply(returnJson, id);
        };
        return this;
    }

    /// <summary>Pushes an unsolicited event to the connected client.</summary>
    public async Task EmitEventAsync(string name, JsonNode? data = null, long seconds = 0, long microseconds = 0)
    {
        await _clientConnected.Task;
        var ev = new JsonObject
        {
            ["timestamp"] = new JsonObject { ["seconds"] = seconds, ["microseconds"] = microseconds },
            ["event"] = name,
            ["data"] = data ?? new JsonObject(),
        };
        await WriteLineAsync(ev.ToJsonString(), _cts.Token);
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        try
        {
            _client = await _listener.AcceptTcpClientAsync(ct);
            var stream = _client.GetStream();
            _reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            _writer = new StreamWriter(stream, Utf8, bufferSize: 1024, leaveOpen: true) { NewLine = "\r\n", AutoFlush = true };

            await WriteLineAsync(_greeting, ct);
            _clientConnected.TrySetResult();

            string? line;
            while (!ct.IsCancellationRequested && (line = await _reader.ReadLineAsync(ct)) is not null)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (!TryParseCommand(line, out string? command, out long? id) || command is null)
                {
                    continue;
                }

                ReceivedCommandLines.Enqueue(line);

                Func<long?, CancellationToken, Task<string>> handler = _handlers.TryGetValue(command, out var registered)
                    ? registered
                    : (cmdId, _) => Task.FromResult(ErrorReply("CommandNotFound", $"The command {command} has not been found", cmdId));

                // Run handlers without blocking the read loop, so replies can interleave / arrive out of order.
                _responses.Add(RespondAsync(handler, id, ct));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException)
        {
            // Expected on dispose or client disconnect.
        }
    }

    private async Task RespondAsync(Func<long?, CancellationToken, Task<string>> handler, long? id, CancellationToken ct)
    {
        try
        {
            string reply = await handler(id, ct);
            await WriteLineAsync(reply, ct);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException)
        {
            // Server shutting down before this reply could be sent.
        }
    }

    private static bool TryParseCommand(string line, out string? command, out long? id)
    {
        command = null;
        id = null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("execute", out var executeElement))
            {
                command = executeElement.GetString();
            }

            if (doc.RootElement.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.Number)
            {
                id = idElement.GetInt64();
            }

            return command is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task WriteLineAsync(string line, CancellationToken ct)
    {
        var writer = _writer;
        if (writer is null)
        {
            return;
        }

        await _writeLock.WaitAsync(ct);
        try
        {
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string SuccessReply(string returnJson, long? id)
    {
        var obj = new JsonObject { ["return"] = JsonNode.Parse(returnJson) };
        if (id.HasValue)
        {
            obj["id"] = id.Value;
        }

        return obj.ToJsonString();
    }

    private static string ErrorReply(string errorClass, string description, long? id)
    {
        var obj = new JsonObject
        {
            ["error"] = new JsonObject { ["class"] = errorClass, ["desc"] = description },
        };
        if (id.HasValue)
        {
            obj["id"] = id.Value;
        }

        return obj.ToJsonString();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // Already stopped.
        }

        await _serveTask;
        await Task.WhenAll(_responses);

        // Readers/writers use leaveOpen; the client owns and closes the stream.
        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _listener.Dispose();
        _cts.Dispose();
        _writeLock.Dispose();
    }
}
