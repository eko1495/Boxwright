using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class RenameCommandTests
{
    private static RenameCommand Build(TempVmStore store, IVmStatusProbe probe, CapturingOutput output, FakeDiskService? disks = null)
    {
        FakeDiskService disk = disks ?? new FakeDiskService();
        var rename = new VmRenameService(store.Repository, new VmDeletionService(store.Repository, disk), new VmRuntimeStore());
        return new RenameCommand(new VmResolver(store.Repository), rename, probe, output.Cli);
    }

    [Fact]
    public async Task Renames_a_stopped_vm_and_reports_the_new_folder()
    {
        using var store = new TempVmStore();
        store.Add("old-name");
        var output = new CapturingOutput();
        RenameCommand command = Build(store, new FakeStatusProbe(), output);

        int code = await command.RunAsync(ParsedArgs.Parse(["old-name", "Shiny New Name"]), CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Contains("Shiny New Name", output.Out, StringComparison.Ordinal);
        Assert.Contains("shiny-new-name-", output.Out, StringComparison.Ordinal); // folder slug reported
        Vm reloaded = Assert.Single(await store.Repository.ListAsync());
        Assert.Equal("Shiny New Name", reloaded.Config.Name);
        Assert.StartsWith("shiny-new-name-", Path.GetFileName(reloaded.FolderPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_while_running()
    {
        using var store = new TempVmStore();
        Vm vm = store.Add("busy");
        var probe = new FakeStatusProbe();
        probe.MarkRunning(vm.Config.Id);
        RenameCommand command = Build(store, probe, new CapturingOutput());

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["busy", "renamed"]), CancellationToken.None));

        Assert.Contains("running", ex.Message, StringComparison.Ordinal);
        Assert.Equal("busy", (await store.Repository.ListAsync())[0].Config.Name); // unchanged
    }

    [Fact]
    public async Task Refuses_when_a_linked_clone_depends_on_it()
    {
        using var store = new TempVmStore();
        Vm template = store.Add("template");
        Vm clone = store.Add("instance");
        var disks = new FakeDiskService();
        disks.Backing[Path.Combine(clone.FolderPath, "disk.qcow2")] = Path.Combine(template.FolderPath, "disk.qcow2");
        RenameCommand command = Build(store, new FakeStatusProbe(), new CapturingOutput(), disks);

        CliException ex = await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["template", "renamed-template"]), CancellationToken.None));

        Assert.Contains("instance", ex.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(template.FolderPath)); // folder not moved
    }

    [Fact]
    public async Task Requires_a_new_name_argument()
    {
        using var store = new TempVmStore();
        store.Add("vm");
        RenameCommand command = Build(store, new FakeStatusProbe(), new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["vm"]), CancellationToken.None));
    }
}
