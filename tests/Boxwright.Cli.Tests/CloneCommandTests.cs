using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class CloneCommandTests
{
    private static CloneCommand Build(TempVmStore store, IVmStatusProbe probe, FakeVmCloneService clone, CapturingOutput output) =>
        new(new VmResolver(store.Repository), probe, clone, output.Cli);

    [Fact]
    public async Task Full_clone_is_the_default()
    {
        using var store = new TempVmStore();
        Vm source = store.Add("base");
        var clone = new FakeVmCloneService(store);
        var output = new CapturingOutput();
        CloneCommand command = Build(store, new FakeStatusProbe(), clone, output);

        int code = await command.RunAsync(ParsedArgs.Parse(["base", "copy"]), CancellationToken.None);

        Assert.Equal(0, code);
        (string sourceId, string newName, CloneMode mode) = Assert.Single(clone.Clones);
        Assert.Equal(source.Config.Id, sourceId);
        Assert.Equal("copy", newName);
        Assert.Equal(CloneMode.Full, mode);
    }

    [Fact]
    public async Task Linked_flag_selects_linked_mode()
    {
        using var store = new TempVmStore();
        store.Add("base");
        var clone = new FakeVmCloneService(store);
        CloneCommand command = Build(store, new FakeStatusProbe(), clone, new CapturingOutput());

        await command.RunAsync(ParsedArgs.Parse(["base", "copy", "--linked"]), CancellationToken.None);

        Assert.Equal(CloneMode.Linked, clone.Clones.Single().Mode);
    }

    [Fact]
    public async Task Refuses_to_clone_a_running_source()
    {
        using var store = new TempVmStore();
        Vm source = store.Add("base");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(source.Config.Id);
        var clone = new FakeVmCloneService(store);
        CloneCommand command = Build(store, probe, clone, new CapturingOutput());

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["base", "copy"]), CancellationToken.None));

        Assert.Contains("running", ex.Message, StringComparison.Ordinal);
        Assert.Empty(clone.Clones);
    }

    [Fact]
    public async Task Missing_new_name_is_a_usage_error()
    {
        using var store = new TempVmStore();
        store.Add("base");
        CloneCommand command = Build(store, new FakeStatusProbe(), new FakeVmCloneService(store), new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["base"]), CancellationToken.None));
    }
}
