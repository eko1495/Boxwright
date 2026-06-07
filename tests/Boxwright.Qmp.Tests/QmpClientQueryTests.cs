using System.Text.Json;
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

    [Fact]
    public async Task QueryBlockDevicesAsync_ParsesDeviceFileAndDriver()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("query-block",
            "[{\"device\":\"virtio0\",\"inserted\":{\"file\":\"C:\\\\VMs\\\\a\\\\disk.qcow2\",\"drv\":\"qcow2\",\"node-name\":\"#block123\"}}," +
            "{\"device\":\"boxwright-cd0\",\"inserted\":{\"file\":\"C:\\\\VMs\\\\a\\\\ubuntu.iso\",\"drv\":\"raw\"}}," +
            "{\"device\":\"floppy0\"}]"); // an empty drive with no inserted medium
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        IReadOnlyList<QmpBlockDevice> devices = await client.QueryBlockDevicesAsync();

        Assert.Equal(3, devices.Count);
        Assert.Equal(new QmpBlockDevice("virtio0", "C:\\VMs\\a\\disk.qcow2", "qcow2"), devices[0]);
        Assert.Equal(new QmpBlockDevice("boxwright-cd0", "C:\\VMs\\a\\ubuntu.iso", "raw"), devices[1]);
        Assert.Equal(new QmpBlockDevice("floppy0", null, null), devices[2]);
    }

    [Fact]
    public async Task BlockdevSnapshotTransactionAsync_SendsOneTransaction_TargetingDeviceNotNodeName()
    {
        await using var server = FakeQmpServer.Start();
        server.OnCommand("transaction", "{}");
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        await client.BlockdevSnapshotTransactionAsync(
        [
            ("virtio0", "C:\\VMs\\a\\snap-1-disk0.qcow2"),
            ("virtio1", "C:\\VMs\\a\\snap-1-disk1.qcow2"),
        ]);

        string request = Assert.Single(server.ReceivedCommandLines, r => r.Contains("\"transaction\""));
        using var doc = JsonDocument.Parse(request);
        JsonElement actions = doc.RootElement.GetProperty("arguments").GetProperty("actions");
        Assert.Equal(2, actions.GetArrayLength());

        JsonElement first = actions[0];
        Assert.Equal("blockdev-snapshot-sync", first.GetProperty("type").GetString());
        JsonElement data = first.GetProperty("data");
        Assert.Equal("virtio0", data.GetProperty("device").GetString());
        Assert.Equal("C:\\VMs\\a\\snap-1-disk0.qcow2", data.GetProperty("snapshot-file").GetString());
        Assert.Equal("qcow2", data.GetProperty("format").GetString());
        Assert.Equal("absolute-paths", data.GetProperty("mode").GetString());
        Assert.False(data.TryGetProperty("node-name", out _)); // targets the device, never a node-name
    }

    [Fact]
    public async Task BlockdevSnapshotTransactionAsync_NoActions_Throws()
    {
        await using var server = FakeQmpServer.Start();
        await using var client = new QmpClient();
        await client.ConnectAsync(server.Endpoint);

        await Assert.ThrowsAsync<ArgumentException>(() => client.BlockdevSnapshotTransactionAsync([]));
    }
}
