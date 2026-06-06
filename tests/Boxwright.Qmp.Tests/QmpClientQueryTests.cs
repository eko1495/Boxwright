using Xunit;

namespace Boxwright.Qmp.Tests;

// QMP-6: typed query-status / query-name convenience wrappers.
public class QmpClientQueryTests
{
    [Fact]
    public async Task QueryStatusAsync_ReturnsRunningState()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-status", "{\"status\":\"running\",\"running\":true}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        QmpVmStatus status = await client.QueryStatusAsync();

        Assert.Equal("running", status.Status);
        Assert.True(status.Running);
    }

    [Fact]
    public async Task QueryStatusAsync_ReturnsPrelaunchState()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-status", "{\"status\":\"prelaunch\",\"running\":false}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        QmpVmStatus status = await client.QueryStatusAsync();

        Assert.Equal("prelaunch", status.Status);
        Assert.False(status.Running);
    }

    [Fact]
    public async Task QueryNameAsync_ReturnsGuestName()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-name", "{\"name\":\"Ubuntu 24.04\"}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        string? name = await client.QueryNameAsync();

        Assert.Equal("Ubuntu 24.04", name);
    }

    [Fact]
    public async Task QueryNameAsync_WhenNoNameSet_ReturnsNull()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-name", "{}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        string? name = await client.QueryNameAsync();

        Assert.Null(name);
    }

    [Fact]
    public void QueryStatusAsync_NullClient_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = QmpClientExtensions.QueryStatusAsync(null!);
        });
    }

    [Fact]
    public async Task QueryBlockStatsAsync_SumsReadAndWriteAcrossDevices()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-blockstats",
            "[{\"device\":\"d0\",\"stats\":{\"rd_bytes\":1000,\"wr_bytes\":2000}}," +
            "{\"device\":\"d1\",\"stats\":{\"rd_bytes\":500,\"wr_bytes\":0}}]");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        QmpBlockStats stats = await client.QueryBlockStatsAsync();

        Assert.Equal(1500, stats.ReadBytes);
        Assert.Equal(2000, stats.WriteBytes);
    }

    [Fact]
    public async Task QueryBlockStatsAsync_ToleratesAMissingStatsObject()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-blockstats", "[{\"device\":\"cd0\"}]"); // an optical drive with no stats
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        QmpBlockStats stats = await client.QueryBlockStatsAsync();

        Assert.Equal(0, stats.ReadBytes);
        Assert.Equal(0, stats.WriteBytes);
    }
}
