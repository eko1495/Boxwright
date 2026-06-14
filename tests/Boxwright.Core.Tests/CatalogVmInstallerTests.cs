using Xunit;

namespace Boxwright.Core.Tests;

// CatalogVmInstaller orchestrates download + folder/config + disk prep + seed, tested with fakes
// (no real network or qemu-img). Mirrors the GUI's New-VM flow (ADR-0022).
public sealed class CatalogVmInstallerTests : IDisposable
{
    private const long GiB = 1024L * 1024 * 1024;

    private readonly string _root;
    private readonly VmRepository _repository;
    private readonly RecordingDiskService _disks = new();
    private readonly FakeIsoDownloader _downloader = new("/cache/image.bin");
    private readonly RecordingSeedGenerator _seeds = new();

    public CatalogVmInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"boxwright-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _repository = new VmRepository(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private CatalogVmInstaller BuildInstaller(params IUnattendedInstaller[] installers) =>
        new(_downloader, _repository, _disks, _seeds, new UnattendedInstallerResolver(installers));

    private static OsCatalogEntry IsoEntry(string family = "ubuntu") => new()
    {
        Id = "distro-iso",
        Name = "Distro",
        Version = "1.0",
        ImageKind = OsCatalogEntry.ImageKindIso,
        IsoUrl = new Uri("https://example.invalid/distro.iso"),
        OsFamily = family,
        SupportsAutoinstall = true,
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
    };

    private static CatalogInstallOptions Options(bool unattended = false, UnattendedAnswers? answers = null) => new()
    {
        Name = "vm",
        MemoryMiB = 2048,
        CpuCores = 2,
        DiskSizeGiB = 20,
        Firmware = "uefi",
        Unattended = unattended,
        Answers = answers,
    };

    [Fact]
    public async Task Iso_attended_attaches_the_iso_cd_first_and_creates_a_blank_disk()
    {
        CatalogVmInstaller installer = BuildInstaller();

        Vm vm = await installer.CreateAsync(IsoEntry(), Options());

        RemovableMediaConfig media = Assert.Single(vm.Config.RemovableMedia);
        Assert.Equal("/cache/image.bin", media.File);
        Assert.Equal("dc", vm.Config.Boot.Order);
        Assert.Null(vm.Config.InstallBoot);
        (string path, long size) = Assert.Single(_disks.Created);
        Assert.Equal(20 * GiB, size);
        Assert.Equal(Path.Combine(vm.FolderPath, "disk.qcow2"), path);
        Assert.Empty(_seeds.Calls); // no unattended seed in attended mode
        Assert.True(_downloader.ReverifyRequested); // re-verifies the cached image at create time (PR #5)
    }

    [Fact]
    public async Task Iso_unattended_prepares_the_installer_and_sets_InstallBoot()
    {
        var fakeInstaller = new FakeUnattendedInstaller("ubuntu", new UnattendedInstallPlan
        {
            Boot = new InstallBootConfig { KernelFile = "vmlinuz", InitrdFile = "initrd", Append = "autoinstall" },
            SeedDisks = [new DiskConfig { File = "seed.img", Format = "raw", Interface = "virtio" }],
        });
        CatalogVmInstaller installer = BuildInstaller(fakeInstaller);
        var answers = new UnattendedAnswers { Username = "me", Password = "pw", Hostname = "host" };

        Vm vm = await installer.CreateAsync(IsoEntry(), Options(unattended: true, answers: answers));

        Assert.Equal(answers, fakeInstaller.ReceivedAnswers);
        Assert.Equal("/cache/image.bin", fakeInstaller.ReceivedIsoPath);
        Assert.NotNull(vm.Config.InstallBoot);
        Assert.Equal("vmlinuz", vm.Config.InstallBoot!.KernelFile);
        Assert.Contains(vm.Config.Disks, d => d.File == "seed.img");
        Assert.Single(_disks.Created);
    }

    [Fact]
    public async Task CloudImage_flattens_resizes_and_seeds()
    {
        _disks.InfoVirtualSize = 10 * GiB; // image is 10 GiB; requested 20 GiB → grows
        CatalogVmInstaller installer = BuildInstaller();
        var answers = new UnattendedAnswers { Username = "me", Password = "pw" };

        Vm vm = await installer.CreateAsync(CloudEntry(), Options(answers: answers));

        Assert.Equal("c", vm.Config.Boot.Order);
        Assert.Empty(vm.Config.RemovableMedia);
        (string source, string dest) = Assert.Single(_disks.Copies);
        Assert.Equal("/cache/image.bin", source);
        (string resizePath, long resizeBytes) = Assert.Single(_disks.Resizes);
        Assert.Equal(20 * GiB, resizeBytes);
        Assert.Equal(dest, resizePath);
        (UnattendedAnswers seedAnswers, SeedProfile profile) = Assert.Single(_seeds.Calls);
        Assert.Equal(SeedProfile.CloudImage, profile);
        Assert.Equal(answers, seedAnswers);
        Assert.Contains(vm.Config.Disks, d => d.File == CloudInitSeedGenerator.SeedFileName && d.Format == "raw");
    }

    [Fact]
    public async Task CloudImage_does_not_shrink_below_the_image_size()
    {
        _disks.InfoVirtualSize = 40 * GiB; // image bigger than the requested 20 GiB → no resize
        CatalogVmInstaller installer = BuildInstaller();

        await installer.CreateAsync(CloudEntry(), Options(answers: new UnattendedAnswers { Username = "u", Password = "p" }));

        Assert.Empty(_disks.Resizes);
    }

    [Fact]
    public async Task CloudImage_requires_answers()
    {
        CatalogVmInstaller installer = BuildInstaller();

        await Assert.ThrowsAsync<ArgumentException>(() => installer.CreateAsync(CloudEntry(), Options()));
    }

    [Fact]
    public async Task Rolls_back_the_vm_when_disk_creation_fails()
    {
        _disks.FailCreate = true;
        CatalogVmInstaller installer = BuildInstaller();

        await Assert.ThrowsAsync<DiskException>(() => installer.CreateAsync(IsoEntry(), Options()));

        Assert.Empty(await _repository.ListAsync()); // half-created VM cleaned up
    }

    [Fact]
    public async Task Unattended_on_an_unsupported_family_throws_and_rolls_back()
    {
        // No installer registered for the family → resolver throws InstallMediaException.
        CatalogVmInstaller installer = BuildInstaller();

        await Assert.ThrowsAsync<InstallMediaException>(() => installer.CreateAsync(
            IsoEntry(family: "plan9"),
            Options(unattended: true, answers: new UnattendedAnswers { Username = "u", Password = "p" })));

        Assert.Empty(await _repository.ListAsync());
    }

    private sealed class FakeIsoDownloader(string returnPath) : IIsoDownloader
    {
        public bool ReverifyRequested { get; private set; }

        public Task<string> EnsureAsync(OsCatalogEntry entry, IProgress<IsoDownloadProgress>? progress = null, bool reverifyCachedContent = false, CancellationToken cancellationToken = default)
        {
            ReverifyRequested = reverifyCachedContent;
            progress?.Report(new IsoDownloadProgress(100, 100));
            return Task.FromResult(returnPath);
        }
    }

    private sealed class RecordingSeedGenerator : ISeedGenerator
    {
        public List<(UnattendedAnswers Answers, SeedProfile Profile)> Calls { get; } = [];

        public string Generate(UnattendedAnswers answers, string vmFolderPath, SeedProfile profile = SeedProfile.InstallerAutoinstall)
        {
            Calls.Add((answers, profile));
            return Path.Combine(vmFolderPath, CloudInitSeedGenerator.SeedFileName);
        }
    }

    private sealed class FakeUnattendedInstaller(string osFamily, UnattendedInstallPlan plan) : IUnattendedInstaller
    {
        public string OsFamily => osFamily;

        public string? ReceivedIsoPath { get; private set; }

        public UnattendedAnswers? ReceivedAnswers { get; private set; }

        public UnattendedInstallPlan Prepare(string isoPath, string vmFolderPath, UnattendedAnswers answers)
        {
            ReceivedIsoPath = isoPath;
            ReceivedAnswers = answers;
            return plan;
        }
    }

    private sealed class RecordingDiskService : IDiskService
    {
        public List<(string Path, long SizeBytes)> Created { get; } = [];

        public List<(string Source, string Dest)> Copies { get; } = [];

        public List<(string Path, long SizeBytes)> Resizes { get; } = [];

        public bool FailCreate { get; set; }

        public long InfoVirtualSize { get; set; }

        public Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default)
        {
            if (FailCreate)
            {
                throw new DiskException("qemu-img create failed");
            }

            Created.Add((path, sizeBytes));
            return Task.CompletedTask;
        }

        public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DiskInfo { VirtualSize = InfoVirtualSize });

        public Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default)
        {
            Resizes.Add((path, sizeBytes));
            return Task.CompletedTask;
        }

        public Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default)
        {
            Copies.Add((sourcePath, destinationPath));
            return Task.CompletedTask;
        }

        public Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RebaseAsync(string imagePath, string newBackingPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
