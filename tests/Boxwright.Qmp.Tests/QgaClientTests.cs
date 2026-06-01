using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace Boxwright.Qmp.Tests;

// The guest-agent client speaks QMP-like JSON with no greeting/handshake; tested against a
// minimal loopback channel that replies to each request with a canned line.
public class QgaClientTests
{
    [Fact]
    public async Task PingAsync_WhenAgentReplies_ReturnsTrue()
    {
        await using var server = FakeQgaServer.Start("{\"return\":{}}");
        await using var client = new QgaClient();
        await client.ConnectAsync("127.0.0.1", server.Port);

        Assert.True(await client.PingAsync());
    }

    [Fact]
    public async Task GetIpAddressesAsync_ReturnsNonLoopbackAddresses()
    {
        const string interfaces =
            "{\"return\":[" +
            "{\"name\":\"lo\",\"ip-addresses\":[{\"ip-address-type\":\"ipv4\",\"ip-address\":\"127.0.0.1\",\"prefix\":8}]}," +
            "{\"name\":\"enp0s3\",\"ip-addresses\":[{\"ip-address-type\":\"ipv4\",\"ip-address\":\"10.0.2.15\",\"prefix\":24}]}" +
            "]}";
        await using var server = FakeQgaServer.Start(interfaces);
        await using var client = new QgaClient();
        await client.ConnectAsync("127.0.0.1", server.Port);

        IReadOnlyList<string> addresses = await client.GetIpAddressesAsync();

        Assert.Equal("10.0.2.15", Assert.Single(addresses)); // loopback is filtered out
    }

    // A minimal QGA channel: no greeting; answers each request line with a fixed reply.
    private sealed class FakeQgaServer : IAsyncDisposable
    {
        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

        private readonly TcpListener _listener;
        private readonly string _reply;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _serveTask;

        private FakeQgaServer(string reply)
        {
            _reply = reply;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serveTask = ServeAsync(_cts.Token);
        }

        public int Port { get; }

        public static FakeQgaServer Start(string reply) => new(reply);

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
            _listener.Dispose();
            _cts.Dispose();
        }

        private async Task ServeAsync(CancellationToken ct)
        {
            try
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(ct);
                NetworkStream stream = client.GetStream();
                using var reader = new StreamReader(stream, Utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                await using var writer = new StreamWriter(stream, Utf8, bufferSize: 1024, leaveOpen: true) { NewLine = "\n", AutoFlush = true };

                string? line;
                while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync(ct)) is not null)
                {
                    await writer.WriteLineAsync(_reply.AsMemory(), ct);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or SocketException)
            {
                // Expected on dispose or client disconnect.
            }
        }
    }
}
