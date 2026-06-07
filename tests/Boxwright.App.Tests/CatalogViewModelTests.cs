using Boxwright.App.ViewModels;
using Boxwright.Core;
using Xunit;

namespace Boxwright.App.Tests;

public sealed class CatalogViewModelTests
{
    private static OsCatalogEntry SampleEntry() => new()
    {
        Id = "test-os",
        Name = "Test OS",
        Version = "1.0",
        Arch = "x86_64",
        IsoUrl = new Uri("https://example.com/test.iso"),
        Sha256 = new string('a', 64),
        SizeBytes = 1000,
        SourceName = "Example",
        Recommended = new OsRecommendedSpec { MemoryMiB = 4096, CpuCores = 4, DiskGiB = 30, Firmware = "uefi" },
    };

    private static OsCatalogEntry CloudImageEntry() => new()
    {
        Id = "ubuntu-cloud",
        Name = "Ubuntu (cloud)",
        Version = "24.04",
        Arch = "x86_64",
        ImageKind = OsCatalogEntry.ImageKindCloudImage,
        OsFamily = "ubuntu",
        SupportsAutoinstall = true,
        IsoUrl = new Uri("https://example.com/ubuntu-cloud.img"),
        Sha256 = new string('b', 64),
        SizeBytes = 1000,
        SourceName = "Canonical",
        Recommended = new OsRecommendedSpec { MemoryMiB = 2048, CpuCores = 2, DiskGiB = 20, Firmware = "uefi" },
    };

    private static (CatalogViewModel Vm, FakeIsoDownloader Downloader, FakeDiskService Disk, FakeSeedGenerator Seed) Build(
        VmRepository repository,
        Func<string, bool>? isNameTaken = null,
        Exception? downloadFails = null,
        DiskException? diskFails = null,
        DiskException? copyFails = null,
        FakeUnattendedInstaller? installer = null)
    {
        var source = new FakeOsCatalogSource();
        source.Entries.Add(SampleEntry());
        var downloader = new FakeIsoDownloader { FailWith = downloadFails };
        var disk = new FakeDiskService { FailWith = diskFails, CopyFailWith = copyFails };
        var seed = new FakeSeedGenerator();
        var resolver = new FakeUnattendedInstallerResolver(installer ?? new FakeUnattendedInstaller());
        var vm = new CatalogViewModel(source, downloader, repository, disk, seed, resolver,
            new ImmediateUiDispatcher(), isNameTaken ?? (_ => false));
        return (vm, downloader, disk, seed);
    }

    [Fact]
    public async Task LoadEntries_PopulatesGallery()
    {
        using var temp = new TempDir();
        (CatalogViewModel vm, _, _, _) = Build(new VmRepository(temp.Path));

        await vm.LoadEntriesCommand.ExecuteAsync(null);

        Assert.Single(vm.Entries);
    }

    [Fact]
    public void SelectingEntry_PrefillsRecommendedSpecs()
    {
        using var temp = new TempDir();
        (CatalogViewModel vm, _, _, _) = Build(new VmRepository(temp.Path));

        vm.SelectedEntry = SampleEntry();

        Assert.Equal("Test OS", vm.Name);
        Assert.Equal(4096, vm.MemoryMiB);
        Assert.Equal(4, vm.CpuCores);
        Assert.Equal(30, vm.DiskSizeGiB);
        Assert.Equal("uefi", vm.Firmware);
        Assert.True(vm.HasSelection);
        Assert.True(vm.GetItCommand.CanExecute(null));
    }

    [Fact]
    public async Task GetIt_HappyPath_CreatesVmWithAttachedIsoAndCdBoot()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        (CatalogViewModel vm, FakeIsoDownloader downloader, FakeDiskService disk, FakeSeedGenerator seed) = Build(repository);
        await vm.LoadEntriesCommand.ExecuteAsync(null);
        vm.SelectedEntry = vm.Entries[0];
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.False(vm.IsDownloading);
        Assert.Null(vm.ErrorMessage);

        DiskConfig disk0 = Assert.Single(created!.Config.Disks);
        Assert.Equal("disk.qcow2", disk0.File);
        RemovableMediaConfig media = Assert.Single(created.Config.RemovableMedia);
        Assert.Equal("cdrom", media.Type);
        Assert.Equal(downloader.ReturnPath, media.File);
        Assert.True(media.Attached);
        Assert.Equal("dc", created.Config.Boot.Order);

        (string Path, long SizeBytes, string Format) diskCreate = Assert.Single(disk.Created);
        Assert.Equal(30L * 1024 * 1024 * 1024, diskCreate.SizeBytes); // 30 GiB recommended

        // The VM is persisted, not just raised.
        Assert.Single(await repository.ListAsync());

        // SampleEntry doesn't support autoinstall, so no seed is generated and only the primary disk exists.
        Assert.Empty(seed.Calls);
        Assert.Single(created.Config.Disks);
        Assert.Equal("linux", created.Config.OsType); // catalog guests are Linux
    }

    [Fact]
    public async Task GetIt_UnattendedOptInOffByDefault_PreparesNoInstall()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        var installer = new FakeUnattendedInstaller();
        (CatalogViewModel vm, _, _, _) = Build(repository, installer: installer);
        vm.SelectedEntry = SampleEntry() with { SupportsAutoinstall = true, OsFamily = "ubuntu" };

        Assert.False(vm.UnattendedEnabled); // opt-in: off until the user ticks it

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Empty(installer.Calls);
    }

    [Fact]
    public async Task GetIt_Unattended_PreparesInstallerAndAttachesItsSeedDisk()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        var installer = new FakeUnattendedInstaller();
        (CatalogViewModel vm, FakeIsoDownloader downloader, _, _) = Build(repository, installer: installer);
        vm.SelectedEntry = SampleEntry() with { SupportsAutoinstall = true, OsFamily = "ubuntu" };
        vm.UnattendedEnabled = true;
        vm.UnattendedUsername = "alice";
        vm.UnattendedPassword = "secret";
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.True(vm.SelectedSupportsUnattended);

        // The per-family installer was prepared against the downloaded ISO, carrying the entered answers.
        (string iso, string folder, UnattendedAnswers answers) = Assert.Single(installer.Calls);
        Assert.Equal(downloader.ReturnPath, iso);
        Assert.Equal(created!.FolderPath, folder);
        Assert.Equal("alice", answers.Username);

        // The plan's seed disk (Ubuntu-style) is attached alongside the primary disk, and InstallBoot is set.
        Assert.Equal(2, created.Config.Disks.Count);
        Assert.Contains(created.Config.Disks, d => d.File == "seed.img" && d.Format == "raw");
        Assert.NotNull(created.Config.InstallBoot);
    }

    [Fact]
    public async Task GetIt_Unattended_ResolvesInstallerByOsFamily()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        var source = new FakeOsCatalogSource();
        source.Entries.Add(SampleEntry());
        var resolver = new FakeUnattendedInstallerResolver(new FakeUnattendedInstaller());
        var vm = new CatalogViewModel(source, new FakeIsoDownloader(), repository, new FakeDiskService(),
            new FakeSeedGenerator(), resolver, new ImmediateUiDispatcher(), _ => false);
        vm.SelectedEntry = SampleEntry() with { SupportsAutoinstall = true, OsFamily = "debian" };
        vm.UnattendedEnabled = true;
        vm.UnattendedUsername = "alice";
        vm.UnattendedPassword = "secret";

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Contains("debian", resolver.ResolvedFamilies);
    }

    [Fact]
    public async Task GetIt_Unattended_NoSeedDisks_AttachesOnlyPrimaryDisk_AndSetsInstallBoot()
    {
        // A Debian-style installer keeps its preseed inside the initrd, so the plan returns no seed disks.
        var installer = new FakeUnattendedInstaller
        {
            OsFamily = "debian",
            Result = new UnattendedInstallPlan
            {
                Boot = new InstallBootConfig { KernelFile = "vmlinuz", InitrdFile = "initrd", Append = "auto=true priority=critical" },
                SeedDisks = [],
            },
        };
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        (CatalogViewModel vm, _, _, _) = Build(repository, installer: installer);
        vm.SelectedEntry = SampleEntry() with { SupportsAutoinstall = true, OsFamily = "debian" };
        vm.UnattendedEnabled = true;
        vm.UnattendedUsername = "alice";
        vm.UnattendedPassword = "secret";
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Single(created!.Config.Disks); // only the primary disk — the preseed lives in the initrd
        Assert.NotNull(created.Config.InstallBoot);
        Assert.Equal("vmlinuz", created.Config.InstallBoot!.KernelFile);
        Assert.Equal("auto=true priority=critical", created.Config.InstallBoot.Append);
    }

    [Fact]
    public async Task GetIt_CloudImage_DoesNotPrepareInstaller_AndLeavesInstallBootNull()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        var installer = new FakeUnattendedInstaller();
        (CatalogViewModel vm, _, _, _) = Build(repository, installer: installer);
        vm.SelectedEntry = CloudImageEntry();
        vm.UnattendedPassword = "secret";
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Empty(installer.Calls);            // a cloud image is pre-installed — no installer prepares it
        Assert.Null(created!.Config.InstallBoot);
    }

    [Fact]
    public async Task GetIt_Cancelled_CreatesNoVmAndNoError()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        (CatalogViewModel vm, _, _, _) = Build(repository, downloadFails: new OperationCanceledException());
        vm.SelectedEntry = SampleEntry();
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Null(created);
        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsDownloading);
        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task GetIt_DownloadFailure_ShowsErrorAndCreatesNoVm()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        (CatalogViewModel vm, _, _, _) = Build(repository, downloadFails: new DownloadException("network down"));
        vm.SelectedEntry = SampleEntry();
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Null(created);
        Assert.Equal("network down", vm.ErrorMessage);
        Assert.False(vm.IsDownloading);
        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task GetIt_DiskFailure_RollsBackTheVm()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        (CatalogViewModel vm, _, _, _) = Build(repository, diskFails: new DiskException("out of space"));
        vm.SelectedEntry = SampleEntry();
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Null(created);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(await repository.ListAsync()); // the half-created VM was deleted
    }

    [Fact]
    public void CloudImage_RequiresCredentials_AndHidesAutoinstallOptIn()
    {
        using var temp = new TempDir();
        (CatalogViewModel vm, _, _, _) = Build(new VmRepository(temp.Path));

        vm.SelectedEntry = CloudImageEntry();

        Assert.True(vm.IsCloudImage);
        Assert.False(vm.ShowAutoinstallOptIn); // no experimental opt-in for a pre-installed image
        Assert.True(vm.HasValidationError);    // password is required (the image has no default login)
        Assert.False(vm.GetItCommand.CanExecute(null));

        vm.UnattendedPassword = "secret";

        Assert.False(vm.HasValidationError);
        Assert.True(vm.GetItCommand.CanExecute(null));
    }

    [Fact]
    public async Task GetIt_CloudImage_FlattensResizesSeedsAndAttaches()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        (CatalogViewModel vm, FakeIsoDownloader downloader, FakeDiskService disk, FakeSeedGenerator seed) = Build(repository);
        vm.SelectedEntry = CloudImageEntry();
        vm.UnattendedUsername = "alice";
        vm.UnattendedPassword = "secret";
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.NotNull(created);
        Assert.Null(vm.ErrorMessage);

        // The downloaded cloud image is flattened into the VM folder as the disk (not freshly created).
        Assert.Empty(disk.Created);
        (string Source, string Destination, string Format) copy = Assert.Single(disk.Copied);
        Assert.Equal(downloader.ReturnPath, copy.Source);
        Assert.Equal(Path.Combine(created!.FolderPath, "disk.qcow2"), copy.Destination);

        // Grown to the requested 20 GiB (the fake image's virtual size is smaller).
        (string Path, long SizeBytes) resize = Assert.Single(disk.Resized);
        Assert.Equal(20L * 1024 * 1024 * 1024, resize.SizeBytes);

        // A CLOUD-IMAGE seed (plain cloud-init, not autoinstall) carrying the login, attached as a raw disk.
        (UnattendedAnswers Answers, string VmFolder, SeedProfile Profile) seedCall = Assert.Single(seed.Calls);
        Assert.Equal(SeedProfile.CloudImage, seedCall.Profile);
        Assert.Equal("alice", seedCall.Answers.Username);

        // No installer media; boot straight from the disk; primary disk + seed disk.
        Assert.Empty(created.Config.RemovableMedia);
        Assert.Equal("c", created.Config.Boot.Order);
        Assert.Equal(2, created.Config.Disks.Count);
        Assert.Contains(created.Config.Disks, d => d.File == "seed.img" && d.Format == "raw");

        Assert.Single(await repository.ListAsync());
    }

    [Fact]
    public async Task GetIt_CloudImage_SkipsResizeWhenRequestedFitsInImage()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        (CatalogViewModel vm, _, FakeDiskService disk, _) = Build(repository);
        disk.VirtualSizeBytes = 50L * 1024 * 1024 * 1024; // image is already larger than the request
        vm.SelectedEntry = CloudImageEntry();
        vm.DiskSizeGiB = 20;
        vm.UnattendedPassword = "secret";

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Empty(disk.Resized); // never shrink below the image's own virtual size
    }

    [Fact]
    public async Task GetIt_CloudImagePrepFailure_RollsBackTheVm()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        (CatalogViewModel vm, _, _, _) = Build(repository, copyFails: new DiskException("copy failed"));
        vm.SelectedEntry = CloudImageEntry();
        vm.UnattendedPassword = "secret";
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Null(created);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(await repository.ListAsync()); // the half-prepared VM was deleted
    }

    [Fact]
    public void NameCollision_BlocksGetIt()
    {
        using var temp = new TempDir();
        (CatalogViewModel vm, _, _, _) = Build(new VmRepository(temp.Path), isNameTaken: _ => true);

        vm.SelectedEntry = SampleEntry();

        Assert.True(vm.HasValidationError);
        Assert.False(vm.GetItCommand.CanExecute(null));
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bw-catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
