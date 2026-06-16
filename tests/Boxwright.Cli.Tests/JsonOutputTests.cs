using System.Text.Json;
using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

/// <summary>Asserts that <c>--json</c> output is well-formed and carries the expected fields.</summary>
public sealed class JsonOutputTests
{
    [Fact]
    public async Task List_json_is_an_array_with_camelCase_fields()
    {
        using var store = new TempVmStore();
        Vm running = store.Add("up");
        store.Add("down");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(running.Config.Id);
        var output = new CapturingOutput();
        var command = new ListCommand(store.Repository, probe, new VmDiskUsageService(new FakeDiskService()), output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["--json"]), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output.Out);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());

        JsonElement[] items = [.. doc.RootElement.EnumerateArray()];
        Assert.Contains(items, e => e.GetProperty("name").GetString() == "up" && e.GetProperty("status").GetString() == "running");
        Assert.Contains(items, e => e.GetProperty("name").GetString() == "down" && e.GetProperty("status").GetString() == "stopped");
    }

    [Fact]
    public async Task List_json_on_empty_store_is_an_empty_array()
    {
        using var store = new TempVmStore();
        var output = new CapturingOutput();
        var command = new ListCommand(store.Repository, new FakeStatusProbe(), new VmDiskUsageService(new FakeDiskService()), output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["--json"]), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output.Out);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Info_json_carries_config_and_disks()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("alpha");
        var output = new CapturingOutput();
        var command = new InfoCommand(new VmResolver(store.Repository), new FakeStatusProbe(), new VmDiskUsageService(new FakeDiskService()), output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["alpha", "--json"]), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output.Out);
        JsonElement root = doc.RootElement;
        Assert.Equal(vm.Config.Id, root.GetProperty("id").GetString());
        Assert.Equal("alpha", root.GetProperty("name").GetString());
        Assert.Equal("stopped", root.GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("disks").GetArrayLength());
        Assert.Equal("disk.qcow2", root.GetProperty("disks")[0].GetProperty("file").GetString());
    }

    [Fact]
    public async Task Info_json_reports_disk_usage_when_measurable()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("alpha");
        var disks = new FakeDiskService();
        disks.Sizes[Path.Combine(vm.FolderPath, "disk.qcow2")] = (Actual: 2_000_000, Virtual: 20_000_000);
        var output = new CapturingOutput();
        var command = new InfoCommand(new VmResolver(store.Repository), new FakeStatusProbe(), new VmDiskUsageService(disks), output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["alpha", "--json"]), CancellationToken.None);

        JsonElement root = JsonDocument.Parse(output.Out).RootElement;
        Assert.Equal(2_000_000, root.GetProperty("diskActualBytes").GetInt64());
        Assert.Equal(20_000_000, root.GetProperty("diskVirtualBytes").GetInt64());
        Assert.Equal(2_000_000, root.GetProperty("disks")[0].GetProperty("actualBytes").GetInt64());
    }

    [Fact]
    public async Task List_json_reports_each_vms_on_disk_footprint()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("alpha");
        var disks = new FakeDiskService();
        disks.Sizes[Path.Combine(vm.FolderPath, "disk.qcow2")] = (Actual: 5_000_000, Virtual: 40_000_000);
        var output = new CapturingOutput();
        var command = new ListCommand(store.Repository, new FakeStatusProbe(), new VmDiskUsageService(disks), output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["--json"]), CancellationToken.None);

        JsonElement item = JsonDocument.Parse(output.Out).RootElement[0];
        Assert.Equal(5_000_000, item.GetProperty("diskActualBytes").GetInt64());
    }

    [Fact]
    public async Task Os_json_lists_entries()
    {
        var catalog = new FakeOsCatalogSource(new OsCatalogEntry
        {
            Id = "ubuntu-x",
            Name = "Ubuntu",
            Version = "24.04",
            Arch = "x86_64",
            IsoUrl = new Uri("https://example.invalid/os.iso"),
            SupportsAutoinstall = true,
        });
        var output = new CapturingOutput();
        var command = new OsCommand(catalog, output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["list", "--json"]), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output.Out);
        JsonElement entry = Assert.Single(doc.RootElement.EnumerateArray().ToArray());
        Assert.Equal("ubuntu-x", entry.GetProperty("id").GetString());
        Assert.True(entry.GetProperty("supportsAutoinstall").GetBoolean());
    }

    [Fact]
    public async Task Snapshot_list_json_carries_tags()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        var snapshots = new FakeSnapshotService();
        snapshots.Snapshots.Add(new VmSnapshot { Name = "base", DateSeconds = 1_700_000_000, VmStateSize = 4096 });
        var output = new CapturingOutput();
        var command = new SnapshotCommand(new VmResolver(store.Repository), new FakeStatusProbe(), snapshots, output.Cli);

        await command.RunAsync(ParsedArgs.Parse(["list", "vm", "--json"]), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(output.Out);
        JsonElement entry = Assert.Single(doc.RootElement.EnumerateArray().ToArray());
        Assert.Equal("base", entry.GetProperty("tag").GetString());
        Assert.True(entry.GetProperty("hasVmState").GetBoolean());
    }
}
