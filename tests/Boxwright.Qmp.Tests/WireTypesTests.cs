using System.Text.Json;
using Xunit;

namespace Boxwright.Qmp.Tests;

// Validates the QMP-1 wire types. Reply/event/greeting samples are taken
// verbatim from the GATE-0 spike's real QEMU 11.0.50 output.
public class WireTypesTests
{
    [Fact]
    public void CommandEnvelope_WithArgumentsAndId_RoundTrips()
    {
        var envelope = new QmpCommandEnvelope
        {
            Execute = "block_resize",
            Arguments = new Dictionary<string, object> { ["device"] = "disk0", ["size"] = 1024L },
            Id = 7,
        };

        string json = JsonSerializer.Serialize(envelope);
        var back = JsonSerializer.Deserialize<QmpCommandEnvelope>(json);

        Assert.NotNull(back);
        Assert.Equal("block_resize", back.Execute);
        Assert.Equal(7, back.Id);
        Assert.Contains("\"execute\":\"block_resize\"", json, StringComparison.Ordinal);
        Assert.Contains("\"id\":7", json, StringComparison.Ordinal);
        Assert.Contains("\"arguments\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandEnvelope_WithoutArgumentsOrId_OmitsThoseKeys()
    {
        var envelope = new QmpCommandEnvelope { Execute = "qmp_capabilities" };

        string json = JsonSerializer.Serialize(envelope);

        Assert.Equal("{\"execute\":\"qmp_capabilities\"}", json);
    }

    [Fact]
    public void ReplyEnvelope_Success_ParsesReturnAndHasNoError()
    {
        const string json = "{\"return\": {\"status\": \"prelaunch\", \"running\": false}}";

        var reply = JsonSerializer.Deserialize<QmpReplyEnvelope>(json);

        Assert.NotNull(reply);
        Assert.Null(reply.Error);
        Assert.True(reply.Return.HasValue);
        Assert.Equal("prelaunch", reply.Return.Value.GetProperty("status").GetString());
        Assert.False(reply.Return.Value.GetProperty("running").GetBoolean());
    }

    [Fact]
    public void ReplyEnvelope_Error_ParsesClassAndDescription()
    {
        const string json = "{\"error\": {\"class\": \"CommandNotFound\", \"desc\": \"no command 'bogus'\"}}";

        var reply = JsonSerializer.Deserialize<QmpReplyEnvelope>(json);

        Assert.NotNull(reply);
        Assert.NotNull(reply.Error);
        Assert.Equal("CommandNotFound", reply.Error.Class);
        Assert.Equal("no command 'bogus'", reply.Error.Desc);
    }

    [Fact]
    public void EventEnvelope_ParsesNameTimestampAndData()
    {
        // Verbatim SHUTDOWN event observed during the GATE-0 spike.
        const string json =
            "{\"timestamp\": {\"seconds\": 1780051456, \"microseconds\": 433046}, " +
            "\"event\": \"SHUTDOWN\", \"data\": {\"guest\": false, \"reason\": \"host-qmp-quit\"}}";

        var ev = JsonSerializer.Deserialize<QmpEventEnvelope>(json);

        Assert.NotNull(ev);
        Assert.Equal("SHUTDOWN", ev.Event);
        Assert.NotNull(ev.Timestamp);
        Assert.Equal(1780051456, ev.Timestamp.Seconds);
        Assert.Equal(433046, ev.Timestamp.Microseconds);
        Assert.Equal("host-qmp-quit", ev.Data.GetProperty("reason").GetString());
    }

    [Fact]
    public void GreetingEnvelope_ParsesQmpVersionAndCapabilities()
    {
        const string json =
            "{\"QMP\": {\"version\": {\"qemu\": {\"major\": 11, \"minor\": 0, \"micro\": 50}, " +
            "\"package\": \"v11.0.0\"}, \"capabilities\": [\"oob\"]}}";

        var greeting = JsonSerializer.Deserialize<QmpGreetingEnvelope>(json);

        Assert.NotNull(greeting);
        Assert.NotNull(greeting.Qmp);
        Assert.Equal(11, greeting.Qmp.Version.GetProperty("qemu").GetProperty("major").GetInt32());
        Assert.Equal("oob", Assert.Single(greeting.Qmp.Capabilities!));
    }

    [Fact]
    public void QmpEndpoint_Tcp_CapturesHostPortAndFormatsToString()
    {
        var ep = QmpEndpoint.Tcp("127.0.0.1", 4444);

        Assert.Equal(QmpTransport.Tcp, ep.Transport);
        Assert.Equal("127.0.0.1", ep.Host);
        Assert.Equal(4444, ep.Port);
        Assert.Equal("tcp:127.0.0.1:4444", ep.ToString());
    }

    [Fact]
    public void QmpEndpoint_UnixSocket_CapturesPathAndFormatsToString()
    {
        var ep = QmpEndpoint.UnixSocket("/tmp/qmp.sock");

        Assert.Equal(QmpTransport.Unix, ep.Transport);
        Assert.Equal("/tmp/qmp.sock", ep.SocketPath);
        Assert.Equal("unix:/tmp/qmp.sock", ep.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void QmpEndpoint_Tcp_RejectsBlankHost(string? host)
    {
        Assert.ThrowsAny<ArgumentException>(() => QmpEndpoint.Tcp(host!, 4444));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void QmpEndpoint_Tcp_RejectsOutOfRangePort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => QmpEndpoint.Tcp("127.0.0.1", port));
    }

    [Fact]
    public void QmpCommandException_CarriesClassAndDescription()
    {
        var ex = new QmpCommandException("GenericError", "boom");

        Assert.Equal("GenericError", ex.ErrorClass);
        Assert.Equal("boom", ex.Description);
        Assert.Contains("GenericError", ex.Message, StringComparison.Ordinal);
        Assert.Contains("boom", ex.Message, StringComparison.Ordinal);
    }
}
