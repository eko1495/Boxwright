using System.Collections.Concurrent;
using Xunit;

namespace Boxwright.Qmp.Tests;

// QMP traffic tracing: the optional ctor hooks fire with the raw JSON sent/received,
// which is how Core attaches Debug-level traffic logging without the Qmp library
// taking a logging dependency. Deterministic against the FakeQmpServer: by the time
// ExecuteAsync returns, its reply has already been read (so the receive hook has run).
public class QmpClientTraceTests
{
    [Fact]
    public async Task TraceHooks_CaptureJsonSentAndReceived()
    {
        // ConcurrentQueue: the receive hook runs on the background read loop, the send
        // hook on the calling thread — capture must be thread-safe.
        var sent = new ConcurrentQueue<string>();
        var received = new ConcurrentQueue<string>();

        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-status", "{\"status\":\"running\",\"running\":true}");
        await using var client = new QmpClient(onSent: sent.Enqueue, onReceived: received.Enqueue);

        await client.ConnectAsync(server.Endpoint);
        await client.ExecuteAsync("query-status");

        // Sent: the capabilities handshake and the command.
        Assert.Contains(sent, line => line.Contains("qmp_capabilities", StringComparison.Ordinal));
        Assert.Contains(sent, line => line.Contains("query-status", StringComparison.Ordinal));

        // Received: the greeting banner and the command's reply payload.
        Assert.Contains(received, line => line.Contains("QMP", StringComparison.Ordinal));
        Assert.Contains(received, line => line.Contains("running", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NullHooks_AreSafe()
    {
        await using var server = FakeQmpServer.Start();
        await using var client = new QmpClient();

        await client.ConnectAsync(server.Endpoint);

        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task ThrowingHook_DoesNotDisruptTheProtocol()
    {
        // A misbehaving diagnostic hook must never break the connection or fault the read loop.
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-status", "{\"status\":\"running\"}");
        await using var client = new QmpClient(
            onSent: _ => throw new InvalidOperationException("boom"),
            onReceived: _ => throw new InvalidOperationException("boom"));

        await client.ConnectAsync(server.Endpoint);
        var status = await client.ExecuteAsync("query-status");

        Assert.True(client.IsConnected);
        Assert.Equal("running", status.GetProperty("status").GetString());
    }
}
