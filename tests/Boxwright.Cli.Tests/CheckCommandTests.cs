using System.Text.Json;
using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class CheckCommandTests
{
    private static CheckCommand Build(TempVmStore store, IVmStatusProbe probe, FakeDiskService disks, CapturingOutput output) =>
        new(new VmResolver(store.Repository), probe, new VmIntegrityService(disks), output.Cli);

    [Fact]
    public async Task Healthy_disk_reports_ok_and_exits_zero()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var disks = new FakeDiskService();
        disks.Checks[Path.Combine(vm.FolderPath, "disk.qcow2")] = new DiskCheckResult();
        var output = new CapturingOutput();
        CheckCommand command = Build(store, new FakeStatusProbe(), disks, output);

        int code = await command.RunAsync(ParsedArgs.Parse(["vm"]), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("OK", output.Out, StringComparison.Ordinal);
        Assert.Contains("consistent", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Corrupted_disk_reports_and_exits_nonzero()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var disks = new FakeDiskService();
        disks.Checks[Path.Combine(vm.FolderPath, "disk.qcow2")] = new DiskCheckResult { Corruptions = 4 };
        var output = new CapturingOutput();
        CheckCommand command = Build(store, new FakeStatusProbe(), disks, output);

        int code = await command.RunAsync(ParsedArgs.Parse(["vm"]), CancellationToken.None);

        Assert.Equal(1, code);
        Assert.Contains("CORRUPTED", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repair_flag_requests_repair_and_reports_fixed_counts()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var disks = new FakeDiskService();
        disks.Checks[Path.Combine(vm.FolderPath, "disk.qcow2")] =
            new DiskCheckResult { Corruptions = 2, CorruptionsFixed = 2, Leaks = 1, LeaksFixed = 1 };
        var output = new CapturingOutput();
        CheckCommand command = Build(store, new FakeStatusProbe(), disks, output);

        int code = await command.RunAsync(ParsedArgs.Parse(["vm", "--repair"]), CancellationToken.None);

        Assert.Equal(DiskRepairMode.All, disks.LastRepair); // opted into -r all
        Assert.Equal(0, code); // healthy after repair (no remaining corruptions/check-errors)
        Assert.Contains("fixed 2 corruptions / 1 leaks", output.Out, StringComparison.Ordinal);
        Assert.Contains("repaired", output.Out, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Without_repair_flag_no_repair_is_requested()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var disks = new FakeDiskService();
        CheckCommand command = Build(store, new FakeStatusProbe(), disks, new CapturingOutput());

        await command.RunAsync(ParsedArgs.Parse(["vm"]), CancellationToken.None);

        Assert.Equal(DiskRepairMode.None, disks.LastRepair);
    }

    [Fact]
    public async Task Refused_while_running()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);
        CheckCommand command = Build(store, probe, new FakeDiskService(), new CapturingOutput());

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["vm"]), CancellationToken.None));

        Assert.Contains("running", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_carries_the_verdict_and_counts()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var disks = new FakeDiskService();
        disks.Checks[Path.Combine(vm.FolderPath, "disk.qcow2")] = new DiskCheckResult { Leaks = 2 };
        var output = new CapturingOutput();
        CheckCommand command = Build(store, new FakeStatusProbe(), disks, output);

        await command.RunAsync(ParsedArgs.Parse(["vm", "--json"]), CancellationToken.None);

        JsonElement root = JsonDocument.Parse(output.Out).RootElement;
        Assert.True(root.GetProperty("healthy").GetBoolean()); // leaks aren't corruption
        Assert.True(root.GetProperty("checked").GetBoolean());
        Assert.Equal(2, root.GetProperty("disks")[0].GetProperty("leaks").GetInt64());
    }
}
