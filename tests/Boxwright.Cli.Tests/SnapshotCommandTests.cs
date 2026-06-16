using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class SnapshotCommandTests
{
    private static SnapshotCommand Build(TempVmStore store, IVmStatusProbe probe, FakeSnapshotService snapshots, CapturingOutput output) =>
        new(new VmResolver(store.Repository), probe, snapshots, output.Cli);

    [Fact]
    public async Task List_renders_snapshots()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        var snapshots = new FakeSnapshotService();
        snapshots.Snapshots.Add(new VmSnapshot { Name = "base", DateSeconds = 1_700_000_000 });
        var output = new CapturingOutput();
        SnapshotCommand command = Build(store, new FakeStatusProbe(), snapshots, output);

        int code = await command.RunAsync(ParsedArgs.Parse(["list", "vm"]), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("base", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_with_none_says_so()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        var output = new CapturingOutput();
        SnapshotCommand command = Build(store, new FakeStatusProbe(), new FakeSnapshotService(), output);

        await command.RunAsync(ParsedArgs.Parse(["list", "vm"]), CancellationToken.None);

        Assert.Contains("No snapshots", output.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_passes_the_vm_and_tag()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var snapshots = new FakeSnapshotService();
        SnapshotCommand command = Build(store, new FakeStatusProbe(), snapshots, new CapturingOutput());

        await command.RunAsync(ParsedArgs.Parse(["create", "vm", "v1"]), CancellationToken.None);

        (Vm received, string tag) = Assert.Single(snapshots.Created);
        Assert.Equal("v1", tag);
        Assert.Equal(vm.FolderPath, received.FolderPath);
    }

    [Fact]
    public async Task Create_requires_a_tag()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        SnapshotCommand command = Build(store, new FakeStatusProbe(), new FakeSnapshotService(), new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["create", "vm"]), CancellationToken.None));
    }

    [Fact]
    public async Task Create_is_refused_while_running()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);
        var snapshots = new FakeSnapshotService();
        SnapshotCommand command = Build(store, probe, snapshots, new CapturingOutput());

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["create", "vm", "v1"]), CancellationToken.None));

        Assert.Contains("running", ex.Message, StringComparison.Ordinal);
        Assert.Empty(snapshots.Created);
    }

    [Fact]
    public async Task Restore_passes_tag_through_when_stopped()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var snapshots = new FakeSnapshotService();
        SnapshotCommand command = Build(store, new FakeStatusProbe(), snapshots, new CapturingOutput());

        await command.RunAsync(ParsedArgs.Parse(["restore", "vm", "s1"]), CancellationToken.None);

        (Vm received, string tag) = Assert.Single(snapshots.Restored);
        Assert.Equal("s1", tag);
        Assert.Equal(vm.FolderPath, received.FolderPath);
    }

    [Fact]
    public async Task Restore_is_refused_while_running()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("vm");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);
        var snapshots = new FakeSnapshotService();
        SnapshotCommand command = Build(store, probe, snapshots, new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["restore", "vm", "s1"]), CancellationToken.None));

        Assert.Empty(snapshots.Restored);
    }

    [Fact]
    public async Task Delete_passes_tag_through()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        var snapshots = new FakeSnapshotService();
        SnapshotCommand command = Build(store, new FakeStatusProbe(), snapshots, new CapturingOutput());

        await command.RunAsync(ParsedArgs.Parse(["delete", "vm", "old"]), CancellationToken.None);

        Assert.Equal("old", snapshots.Deleted.Single().Tag);
    }

    [Fact]
    public async Task Unknown_subcommand_is_an_error()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        SnapshotCommand command = Build(store, new FakeStatusProbe(), new FakeSnapshotService(), new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["frobnicate", "vm"]), CancellationToken.None));
    }
}
