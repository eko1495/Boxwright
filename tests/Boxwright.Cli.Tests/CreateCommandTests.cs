using Boxwright.Cli.Commands;
using Boxwright.Core;
using Xunit;

namespace Boxwright.Cli.Tests;

public sealed class CreateCommandTests
{
    private static CreateCommand Build(
        TempVmStore store,
        FakeDiskService disks,
        CapturingOutput output,
        IOsCatalogSource? catalog = null,
        ICatalogVmInstaller? installer = null) =>
        new(store.Repository, disks, catalog ?? new FakeOsCatalogSource(), installer ?? new FakeCatalogVmInstaller(store), output.Cli);

    private static OsCatalogEntry IsoEntry(bool autoinstall = true) => new()
    {
        Id = "distro-iso",
        Name = "Distro",
        Version = "1.0",
        Arch = "x86_64",
        ImageKind = OsCatalogEntry.ImageKindIso,
        IsoUrl = new Uri("https://example.invalid/distro.iso"),
        OsFamily = "ubuntu",
        SupportsAutoinstall = autoinstall,
        Recommended = new OsRecommendedSpec { MemoryMiB = 4096, CpuCores = 4, DiskGiB = 30, Firmware = "uefi" },
    };

    private static OsCatalogEntry CloudEntry() => new()
    {
        Id = "distro-cloud",
        Name = "Distro Cloud",
        Version = "1.0",
        ImageKind = OsCatalogEntry.ImageKindCloudImage,
        IsoUrl = new Uri("https://example.invalid/distro.qcow2"),
        OsFamily = "ubuntu",
        Recommended = new OsRecommendedSpec { MemoryMiB = 2048, CpuCores = 2, DiskGiB = 25, Firmware = "uefi" },
    };

    // ---- Blank-VM path ----

    [Fact]
    public async Task Creates_a_vm_with_a_disk_and_default_specs()
    {
        using var store = new TempVmStore();
        var disks = new FakeDiskService();
        CreateCommand command = Build(store, disks, new CapturingOutput());

        int code = await command.RunAsync(ParsedArgs.Parse(["devbox"]), CancellationToken.None);

        Assert.Equal(0, code);
        Vm vm = Assert.Single(await store.Repository.ListAsync());
        Assert.Equal("devbox", vm.Config.Name);
        Assert.Equal(2048, vm.Config.MemoryMiB);
        Assert.Equal("disk.qcow2", Assert.Single(vm.Config.Disks).File);

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
        CreateCommand command = Build(store, disks, new CapturingOutput());

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
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput());

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
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["x", "--iso=/no/such/file.iso"]), CancellationToken.None));
    }

    [Fact]
    public async Task Missing_name_is_a_usage_error()
    {
        using var store = new TempVmStore();
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse([]), CancellationToken.None));
    }

    [Fact]
    public async Task Non_positive_resources_are_rejected()
    {
        using var store = new TempVmStore();
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput());

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["x", "--memory=0"]), CancellationToken.None));
    }

    // ---- Catalog (--os) path ----

    [Fact]
    public async Task Os_defaults_resources_from_the_recommended_spec()
    {
        using var store = new TempVmStore();
        var installer = new FakeCatalogVmInstaller(store);
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput(),
            new FakeOsCatalogSource(IsoEntry()), installer);

        await command.RunAsync(ParsedArgs.Parse(["vm", "--os", "distro-iso"]), CancellationToken.None);

        Assert.Equal("distro-iso", installer.Entry!.Id);
        Assert.Equal(4096, installer.Options!.MemoryMiB);
        Assert.Equal(4, installer.Options.CpuCores);
        Assert.Equal(30, installer.Options.DiskSizeGiB);
        Assert.Equal("uefi", installer.Options.Firmware);
        Assert.False(installer.Options.Unattended); // no --unattended
        Assert.Null(installer.Options.Answers);
    }

    [Fact]
    public async Task Os_overrides_beat_the_recommended_spec()
    {
        using var store = new TempVmStore();
        var installer = new FakeCatalogVmInstaller(store);
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput(),
            new FakeOsCatalogSource(IsoEntry()), installer);

        await command.RunAsync(ParsedArgs.Parse(["vm", "--os", "distro-iso", "--memory", "1024", "--disk", "8"]), CancellationToken.None);

        Assert.Equal(1024, installer.Options!.MemoryMiB);
        Assert.Equal(8, installer.Options.DiskSizeGiB);
        Assert.Equal(4, installer.Options.CpuCores); // not overridden → recommended
    }

    [Fact]
    public async Task Os_unattended_iso_builds_answers()
    {
        using var store = new TempVmStore();
        var installer = new FakeCatalogVmInstaller(store);
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput(),
            new FakeOsCatalogSource(IsoEntry()), installer);

        await command.RunAsync(
            ParsedArgs.Parse(["my-vm", "--os", "distro-iso", "--unattended", "--user", "alice", "--password", "secret"]),
            CancellationToken.None);

        Assert.True(installer.Options!.Unattended);
        Assert.Equal("alice", installer.Options.Answers!.Username);
        Assert.Equal("secret", installer.Options.Answers.Password);
        Assert.Equal("my-vm", installer.Options.Answers.Hostname); // sanitized from the VM name
    }

    [Fact]
    public async Task Os_cloud_image_requires_credentials_and_is_always_seeded()
    {
        using var store = new TempVmStore();
        var installer = new FakeCatalogVmInstaller(store);
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput(),
            new FakeOsCatalogSource(CloudEntry()), installer);

        // Without creds → error, installer never called.
        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["vm", "--os", "distro-cloud"]), CancellationToken.None));
        Assert.Null(installer.Options);

        // With creds → installer called; cloud image is seeded but Unattended (installer flag) stays false.
        await command.RunAsync(
            ParsedArgs.Parse(["vm", "--os", "distro-cloud", "--user", "u", "--password", "p"]), CancellationToken.None);
        Assert.False(installer.Options!.Unattended);
        Assert.Equal("u", installer.Options.Answers!.Username);
    }

    [Fact]
    public async Task Os_unattended_on_unsupported_entry_errors_before_calling_the_installer()
    {
        using var store = new TempVmStore();
        var installer = new FakeCatalogVmInstaller(store);
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput(),
            new FakeOsCatalogSource(IsoEntry(autoinstall: false)), installer);

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["vm", "--os", "distro-iso", "--unattended", "--user", "u", "--password", "p"]),
                CancellationToken.None));
        Assert.Null(installer.Options);
    }

    [Fact]
    public async Task Unknown_os_id_is_a_clean_error()
    {
        using var store = new TempVmStore();
        CreateCommand command = Build(store, new FakeDiskService(), new CapturingOutput(),
            new FakeOsCatalogSource(IsoEntry()), new FakeCatalogVmInstaller(store));

        await Assert.ThrowsAsync<CliException>(() =>
            command.RunAsync(ParsedArgs.Parse(["vm", "--os", "nope"]), CancellationToken.None));
    }
}
