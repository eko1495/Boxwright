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

        JsonElement keys = doc.RootElement.GetProperty("arguments").GetProperty("keys");
        Assert.Equal(2, keys.GetArrayLength());
        Assert.Equal("qcode", keys[0].GetProperty("type").GetString());
        Assert.Equal("ret", keys[0].GetProperty("data").GetString());
        Assert.Equal("spc", keys[1].GetProperty("data").GetString());
    }

    [Fact]
    public void SendKeyAsync_NullClient_Throws() =>
        Assert.Throws<ArgumentNullException>(() => { _ = QmpClientExtensions.SendKeyAsync(null!, ["ret"]); });
}
