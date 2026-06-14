using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class CreateCommandTests
{
    [Fact]
    public async Task Creates_a_vm_with_a_disk_and_default_specs()
    {
        using var store = new TempVmStore();
        var disks = new FakeDiskService();
        var output = new CapturingOutput();
        var command = new CreateCommand(store.Repository, disks, output.Cli);

        int code = await command.RunAsync(ParsedArgs.Parse(["devbox"]), CancellationToken.None);

        Assert.Equal(0, code);
        IReadOnlyList<Vm> vms = await store.Repository.ListAsync();
        Vm vm = Assert.Single(vms);
        Assert.Equal("devbox", vm.Config.Name);
        Assert.Equal(2048, vm.Config.MemoryMiB);
        DiskConfig disk = Assert.Single(vm.Config.Disks);
        Assert.Equal("disk.qcow2", disk.File);

        (string path, long sizeBytes, string format) = Assert.Single(disks.Created);
        Assert.Equal(20L * 1024 * 1024 * 1024, sizeBytes);
        Assert.Equal("qcow2", format);
        Assert.Equal(Path.Combine(vm.FolderPath, "disk.qcow2"), path);
    }

    [Fact]
    public async Task Honors_resource_options()
    {
        using var store = new TempVmStore();
        var disks = new FakeDiskService();
        var command = new CreateCommand(store.Repository, disks, new CapturingOutput().Cli);

        await command.RunAsync(
            ParsedArgs.Parse(["big", "--memory=8192", "--cpus=6", "--disk=40", "--firmware=uefi", "--os-type=windows"]),
            CancellationToken.None);

        Vm vm = Assert.Single(await store.Repository.ListAsync());
        Assert.Equal(8192, vm.Config.MemoryMiB);
        Assert.Equal(6, vm.Config.Cpu.Cores);
        Assert.Equal("uefi", vm.Config.Firmware);
        Assert.Equal("windows", vm.Config.OsType);
        Assert.Equal(40L * 1024 * 1024 * 1024, disks.Created.Single().SizeBytes);
    }

    [Fact]
    public async Task Attaches_an_iso_and_boots_cd_first()
    {
        using var store = new TempVmStore();
        string isoPath = Path.Combine(store.Root, "installer.iso");
        await File.WriteAllTextAsync(isoPath, "not really an iso");
        var command = new CreateCommand(store.Repository, new FakeDiskService(), new CapturingOutput().Cli);

        await command.RunAsync(ParsedArgs.Parse(["withiso", $"--iso={isoPath}"]), CancellationToken.None);

        Vm vm = Assert.Single(await store.Repository.ListAsync());
        RemovableMediaConfig media = Assert.Single(vm.Config.RemovableMedia);
        Assert.Equal("cdrom", media.Type);
        Assert.True(media.Attached);
        Assert.Equal(Path.GetFullPath(isoPath), media.File);
        Assert.Equal("dc", vm.Config.Boot.Order);
    }

    [Fact]
    public async Task Missing_iso_is_a_clean_error()
    {
        using var store = new TempVmStore();
        var command = new CreateCommand(store.Repository, new FakeDiskService(), new CapturingOutput().Cli);

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["x", "--iso=/no/such/file.iso"]), CancellationToken.None));
    }

    [Fact]
    public async Task Missing_name_is_a_usage_error()
    {
        using var store = new TempVmStore();
        var command = new CreateCommand(store.Repository, new FakeDiskService(), new CapturingOutput().Cli);

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse([]), CancellationToken.None));
    }

    [Fact]
    public async Task Non_positive_resources_are_rejected()
    {
        using var store = new TempVmStore();
        var command = new CreateCommand(store.Repository, new FakeDiskService(), new CapturingOutput().Cli);

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["x", "--memory=0"]), CancellationToken.None));
    }
}
