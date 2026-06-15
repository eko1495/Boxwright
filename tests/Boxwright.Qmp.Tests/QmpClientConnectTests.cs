using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Boxwright.Qmp.Tests;

// QMP-3: connect + capabilities handshake, exercised against the QMP-2 fixture.
public class QmpClientConnectTests
{
    [Fact]
    public async Task IsConnected_IsFalseBeforeConnect()
    {
        await using var client = new QmpClient();

        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_AgainstFakeServer_SetsIsConnected()
    {
        await using var server = FakeQmpServer.Start();
        await using var client = new QmpClient();

        await client.ConnectAsync(server.Endpoint);

        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_MalformedGreetingJson_ThrowsProtocol()
    {
        await using var server = FakeQmpServer.Start("this is not json");
        await using var client = new QmpClient();

        await Assert.ThrowsAsync<QmpProtocolException>(() => client.ConnectAsync(server.Endpoint));
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_NonGreetingJson_ThrowsProtocol()
    {
        await using var server = FakeQmpServer.Start("{\"notQmp\":true}");
        await using var client = new QmpClient();

        await Assert.ThrowsAsync<QmpProtocolException>(() => client.ConnectAsync(server.Endpoint));
    }

    [Fact]
    public async Task ConnectAsync_ServerClosesBeforeGreeting_ThrowsProtocol()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serverSide = AcceptAndCloseAsync(listener);

        await using var client = new QmpClient();
        await Assert.ThrowsAsync<QmpProtocolException>(() => client.ConnectAsync(QmpEndpoint.Tcp("127.0.0.1", port)));

        await serverSide;
    }

    [Fact]
    public async Task ConnectAsync_CapabilitiesRejected_ThrowsCommand()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommandError("qmp_capabilities", "GenericError", "capabilities denied");
        await using var client = new QmpClient();

        var ex = await Assert.ThrowsAsync<QmpCommandException>(() => client.ConnectAsync(server.Endpoint));

        Assert.Equal("GenericError", ex.ErrorClass);
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WhenCancelled_ThrowsOperationCanceled()
    {
        await using var server = FakeQmpServer.Start();
        await using var client = new QmpClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ConnectAsync(server.Endpoint, cts.Token));
    }

    [Fact]
    public async Task ConnectAsync_AfterAFailedConnect_CanRetryOnTheSameInstance()
    {
        // A failed handshake must dispose what it opened and leave the client reusable — not leak the
        // socket and not trip the "already been used" guard (the latter is what a retry would hit).
        await using var client = new QmpClient();

        await using (var badServer = FakeQmpServer.Start("{\"notQmp\":true}"))
        {
            await Assert.ThrowsAsync<QmpProtocolException>(() => client.ConnectAsync(badServer.Endpoint));
            Assert.False(client.IsConnected);
        }

        // The same instance now connects cleanly to a good server.
        await using var goodServer = FakeQmpServer.Start();
        await client.ConnectAsync(goodServer.Endpoint);

        Assert.True(client.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_WhenAlreadyConnected_ThrowsInvalidOperation()
    {
        await using var server = FakeQmpServer.Start();
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(server.Endpoint));
    }

    private static async Task AcceptAndCloseAsync(TcpListener listener)
    {
        using TcpClient accepted = await listener.AcceptTcpClientAsync();
        // Drop the connection immediately, without sending a greeting.
    }
}
