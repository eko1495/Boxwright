using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class InfoCommandTests
{
    [Fact]
    public async Task Prints_core_fields_and_running_status()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("alpha");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);
        var output = new CapturingOutput();
        var command = new InfoCommand(new VmResolver(store.Repository), probe, new VmDiskUsageService(new FakeDiskService()), output.Cli);

        int code = await command.RunAsync(ParsedArgs.Parse(["alpha"]), CancellationToken.None);

        Assert.Equal(0, code);
        string text = output.Out;
        Assert.Contains("Name:        alpha", text, StringComparison.Ordinal);
        Assert.Contains(vm.Config.Id, text, StringComparison.Ordinal);
        Assert.Contains("running", text, StringComparison.Ordinal);
        Assert.Contains("disk.qcow2", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_reference_is_a_usage_error()
    {
        using var store = new TempVmStore();
        var command = new InfoCommand(new VmResolver(store.Repository), new FakeStatusProbe(), new VmDiskUsageService(new FakeDiskService()), new CapturingOutput().Cli);

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse([]), CancellationToken.None));
    }
}
