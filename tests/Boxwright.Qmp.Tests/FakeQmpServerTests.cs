using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Boxwright.Qmp.Tests;

// Self-tests for the QMP-2 fake server. They drive it over a raw socket (the
// real client does not exist until QMP-3), proving the fixture greets, answers
// qmp_capabilities, returns scripted replies with id-echo, and emits events.
public class FakeQmpServerTests
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [Fact]
    public async Task Endpoint_ReflectsListeningPort()
    {
        await using var server = FakeQmpServer.Start();

        Assert.Equal(QmpTransport.Tcp, server.Endpoint.Transport);
        Assert.Equal(server.Port, server.Endpoint.Port);
    }

    [Fact]
    public async Task Start_SendsGreetingOnConnect()
    {
        await using var server = FakeQmpServer.Start();
        using var conn = await ConnectAsync(server);

        string? greeting = await conn.Reader.ReadLineAsync();

        Assert.NotNull(greeting);
        Assert.Contains("\"QMP\"", greeting, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_WithCustomGreeting_SendsThatGreeting()
    {
        await using var server = FakeQmpServer.Start("{\"QMP\":{\"custom\":true}}");
        using var conn = await ConnectAsync(server);

        string? greeting = await conn.Reader.ReadLineAsync();

        Assert.Equal("{\"QMP\":{\"custom\":true}}", greeting);
    }

    [Fact]
    public async Task Capabilities_RepliesEmptyReturn_AndEchoesId()
    {
        await using var server = FakeQmpServer.Start();
        using var conn = await ConnectAsync(server);
        await conn.Reader.ReadLineAsync(); // greeting

        await conn.Writer.WriteLineAsync("{\"execute\":\"qmp_capabilities\",\"id\":1}");
        using var reply = JsonDocument.Parse((await conn.Reader.ReadLineAsync())!);

        Assert.Equal(JsonValueKind.Object, reply.RootElement.GetProperty("return").ValueKind);
        Assert.Equal(1, reply.RootElement.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task OnCommand_ReturnsScriptedReply_WithIdEcho()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-status", "{\"status\":\"running\",\"running\":true}");
        using var conn = await ConnectAsync(server);
        await conn.Reader.ReadLineAsync(); // greeting

        await conn.Writer.WriteLineAsync("{\"execute\":\"query-status\",\"id\":42}");
        using var reply = JsonDocument.Parse((await conn.Reader.ReadLineAsync())!);

        Assert.Equal("running", reply.RootElement.GetProperty("return").GetProperty("status").GetString());
        Assert.Equal(42, reply.RootElement.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task OnCommandError_ReturnsErrorReply_WithIdEcho()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommandError("explode", "GenericError", "kaboom");
        using var conn = await ConnectAsync(server);
        await conn.Reader.ReadLineAsync(); // greeting

        await conn.Writer.WriteLineAsync("{\"execute\":\"explode\",\"id\":5}");
        using var reply = JsonDocument.Parse((await conn.Reader.ReadLineAsync())!);
        var error = reply.RootElement.GetProperty("error");

        Assert.Equal("GenericError", error.GetProperty("class").GetString());
        Assert.Equal("kaboom", error.GetProperty("desc").GetString());
        Assert.Equal(5, reply.RootElement.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task UnknownCommand_RepliesCommandNotFound()
    {
        await using var server = FakeQmpServer.Start();
        using var conn = await ConnectAsync(server);
        await conn.Reader.ReadLineAsync(); // greeting

        await conn.Writer.WriteLineAsync("{\"execute\":\"no-such-command\",\"id\":9}");
        using var reply = JsonDocument.Parse((await conn.Reader.ReadLineAsync())!);

        Assert.Equal("CommandNotFound", reply.RootElement.GetProperty("error").GetProperty("class").GetString());
    }

    [Fact]
    public async Task EmitEventAsync_DeliversEventToClient()
    {
        await using var server = FakeQmpServer.Start();
        using var conn = await ConnectAsync(server);
        await conn.Reader.ReadLineAsync(); // greeting

        await server.EmitEventAsync("STOP");
        using var ev = JsonDocument.Parse((await conn.Reader.ReadLineAsync())!);

        Assert.Equal("STOP", ev.RootElement.GetProperty("event").GetString());
        Assert.True(ev.RootElement.TryGetProperty("timestamp", out _));
    }

    private static async Task<Conn> ConnectAsync(FakeQmpServer server)
    {
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, server.Port);
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Utf8);
        var writer = new StreamWriter(stream, Utf8) { NewLine = "\n", AutoFlush = true };
        return new Conn(client, reader, writer);
    }

    private sealed class Conn(TcpClient client, StreamReader reader, StreamWriter writer) : IDisposable
    {
        public StreamReader Reader { get; } = reader;

        public StreamWriter Writer { get; } = writer;

        public void Dispose()
        {
            Writer.Dispose();
            Reader.Dispose();
            client.Dispose();
        }
    }
}
