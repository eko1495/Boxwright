using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class ListCommandTests
{
    [Fact]
    public async Task Empty_store_prints_a_friendly_message()
    {
        using var store = new TempVmStore();
        var output = new CapturingOutput();
        var command = new ListCommand(store.Repository, new FakeStatusProbe(), new VmDiskUsageService(new FakeDiskService()), output.Cli);

        int code = await command.RunAsync(ParsedArgs.Parse([]), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("No VMs found", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lists_each_vm_with_run_status()
    {
        using var store = new TempVmStore();
        Vm running = store.Add("up");
        store.Add("down");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(running.Config.Id);
        var output = new CapturingOutput();
        var command = new ListCommand(store.Repository, probe, new VmDiskUsageService(new FakeDiskService()), output.Cli);

        int code = await command.RunAsync(ParsedArgs.Parse([]), CancellationToken.None);

        Assert.Equal(0, code);
        string text = output.Out;
        Assert.Contains("up", text, StringComparison.Ordinal);
        Assert.Contains("down", text, StringComparison.Ordinal);
        Assert.Contains("running", text, StringComparison.Ordinal);
        Assert.Contains("stopped", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("148b144b-d1e4-4ffb-9ccf-a7c04c1893f7", "148b144b")]
    [InlineData("nodashes", "nodashes")]
    [InlineData("", "(none)")]
    public void ShortId_takes_the_first_guid_segment(string id, string expected) =>
        Assert.Equal(expected, ListCommand.ShortId(id));
}
