using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class DeleteCommandTests
{
    private static DeleteCommand Build(TempVmStore store, IVmStatusProbe probe, CapturingOutput output) =>
        new(new VmResolver(store.Repository), store.Repository, probe, output.Cli);

    [Fact]
    public async Task Deletes_with_yes()
    {
        using var store = new TempVmStore();
        store.Add("doomed");
        DeleteCommand command = Build(store, new FakeStatusProbe(), new CapturingOutput());

        int code = await command.RunAsync(ParsedArgs.Parse(["doomed", "--yes"]), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Empty(await store.Repository.ListAsync());
    }

    [Fact]
    public async Task Refuses_without_yes_and_keeps_the_vm()
    {
        using var store = new TempVmStore();
        store.Add("keep");
        DeleteCommand command = Build(store, new FakeStatusProbe(), new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["keep"]), CancellationToken.None));

        Assert.Single(await store.Repository.ListAsync());
    }

    [Fact]
    public async Task Refuses_while_running()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("busy");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);
        DeleteCommand command = Build(store, probe, new CapturingOutput());

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["busy", "--yes"]), CancellationToken.None));

        Assert.Contains("running", ex.Message, StringComparison.Ordinal);
        Assert.Single(await store.Repository.ListAsync());
    }
}
