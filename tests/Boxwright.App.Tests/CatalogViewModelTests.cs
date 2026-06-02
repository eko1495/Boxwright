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

    private static (CatalogViewModel Vm, FakeIsoDownloader Downloader, FakeDiskService Disk) Build(
        VmRepository repository,
        Func<string, bool>? isNameTaken = null,
        Exception? downloadFails = null,
        DiskException? diskFails = null)
    {
        var source = new FakeOsCatalogSource();
        source.Entries.Add(SampleEntry());
        var downloader = new FakeIsoDownloader { FailWith = downloadFails };
        var disk = new FakeDiskService { FailWith = diskFails };
        var vm = new CatalogViewModel(source, downloader, repository, disk, new ImmediateUiDispatcher(), isNameTaken ?? (_ => false));
        return (vm, downloader, disk);
    }

    [Fact]
    public async Task LoadEntries_PopulatesGallery()
    {
        using var temp = new TempDir();
        (CatalogViewModel vm, _, _) = Build(new VmRepository(temp.Path));

        await vm.LoadEntriesCommand.ExecuteAsync(null);

        Assert.Single(vm.Entries);
    }

    [Fact]
    public void SelectingEntry_PrefillsRecommendedSpecs()
    {
        using var temp = new TempDir();
        (CatalogViewModel vm, _, _) = Build(new VmRepository(temp.Path));

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
        (CatalogViewModel vm, FakeIsoDownloader downloader, FakeDiskService disk) = Build(repository);
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
    }

    [Fact]
    public async Task GetIt_Cancelled_CreatesNoVmAndNoError()
    {
        using var temp = new TempDir();
        var repository = new VmRepository(temp.Path);
        (CatalogViewModel vm, _, _) = Build(repository, downloadFails: new OperationCanceledException());
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
        (CatalogViewModel vm, _, _) = Build(repository, downloadFails: new DownloadException("network down"));
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
        (CatalogViewModel vm, _, _) = Build(repository, diskFails: new DiskException("out of space"));
        vm.SelectedEntry = SampleEntry();
        Vm? created = null;
        vm.Created += (_, v) => created = v;

        await vm.GetItCommand.ExecuteAsync(null);

        Assert.Null(created);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Empty(await repository.ListAsync()); // the half-created VM was deleted
    }

    [Fact]
    public void NameCollision_BlocksGetIt()
    {
        using var temp = new TempDir();
        (CatalogViewModel vm, _, _) = Build(new VmRepository(temp.Path), isNameTaken: _ => true);

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
