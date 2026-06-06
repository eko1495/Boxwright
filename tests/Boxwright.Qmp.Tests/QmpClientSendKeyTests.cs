using System.Text.Json;
using Xunit;

namespace Boxwright.Qmp.Tests;

// send-key convenience wrapper: drives a guest key chord (e.g. to dismiss a boot-from-CD prompt).
public class QmpClientSendKeyTests
{
    [Fact]
    public async Task SendKeyAsync_EmitsSendKeyWithQcodeKeys()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("send-key", "{}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        await client.SendKeyAsync(["ret", "spc"]);

        string line = Assert.Single(server.ReceivedCommandLines, l => l.Contains("send-key", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("send-key", doc.RootElement.GetProperty("execute").GetString());

        JsonElement args = doc.RootElement.GetProperty("arguments");
        JsonElement keys = args.GetProperty("keys");
        Assert.Equal(2, keys.GetArrayLength());
        Assert.Equal("qcode", keys[0].GetProperty("type").GetString());
        Assert.Equal("ret", keys[0].GetProperty("data").GetString());
        Assert.Equal("spc", keys[1].GetProperty("data").GetString());
        Assert.False(args.TryGetProperty("hold-time", out _)); // omitted when not given
    }

    [Fact]
    public async Task SendKeyAsync_WithHoldTime_IncludesHoldTime()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("send-key", "{}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        await client.SendKeyAsync(["ret"], holdTimeMs: 500);

        string line = Assert.Single(server.ReceivedCommandLines, l => l.Contains("send-key", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(line);
        Assert.Equal(500, doc.RootElement.GetProperty("arguments").GetProperty("hold-time").GetInt32());
    }

    [Fact]
    public void SendKeyAsync_NullClient_Throws() =>
        Assert.Throws<ArgumentNullException>(() => { _ = QmpClientExtensions.SendKeyAsync(null!, ["ret"]); });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SendKeyEventAsync_EmitsInputSendEvent_WithDownState(bool down)
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("input-send-event", "{}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        await client.SendKeyEventAsync("ret", down);

        string line = Assert.Single(server.ReceivedCommandLines, l => l.Contains("input-send-event", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("input-send-event", doc.RootElement.GetProperty("execute").GetString());
        JsonElement data = doc.RootElement.GetProperty("arguments").GetProperty("events")[0].GetProperty("data");
        Assert.Equal(down, data.GetProperty("down").GetBoolean());
        Assert.Equal("ret", data.GetProperty("key").GetProperty("data").GetString());
    }

    [Fact]
    public void SendKeyEventAsync_NullClient_Throws() =>
        Assert.Throws<ArgumentNullException>(() => { _ = QmpClientExtensions.SendKeyEventAsync(null!, "ret", true); });
}
