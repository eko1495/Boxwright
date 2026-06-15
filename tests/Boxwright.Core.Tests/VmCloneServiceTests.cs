using Xunit;

namespace Boxwright.Core.Tests;

// VmCloneService orchestrates config + folder + disk copy/overlay, tested with a fake disk service.
public sealed class VmCloneServiceTests : IDisposable
{
    private readonly string _root;
    private readonly VmRepository _repository;
    private readonly RecordingDiskService _disks = new();

    public VmCloneServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"boxwright-clone-{Guid.NewGuid():N}");
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

    [Fact]
    public async Task FullClone_CopiesDisk_AndCreatesAnIndependentVm()
    {
        Vm source = await NewSourceAsync();
        var service = new VmCloneService(_repository, _disks);

        Vm clone = await service.CloneAsync(source, "copy", CloneMode.Full);

        Assert.NotEqual(source.Config.Id, clone.Config.Id);
        Assert.Equal("copy", clone.Config.Name);
        Assert.Single(_disks.Copies);
        Assert.Empty(_disks.Overlays);
        Assert.Contains(await _repository.ListAsync(), v => v.Config.Id == clone.Config.Id);
    }

    [Fact]
    public async Task LinkedClone_CreatesAnOverlayBackedBySourceDisk()
    {
        Vm source = await NewSourceAsync();
        var service = new VmCloneService(_repository, _disks);

        Vm clone = await service.CloneAsync(source, "linked", CloneMode.Linked);

        (string Backing, string Overlay) overlay = Assert.Single(_disks.Overlays);
        Assert.Equal(Path.Combine(source.FolderPath, "disk.qcow2"), overlay.Backing);
        Assert.Equal(Path.Combine(clone.FolderPath, "disk.qcow2"), overlay.Overlay);
        Assert.Empty(_disks.Copies);
    }

    [Fact]
    public async Task Clone_GetsItsOwnMac_NotTheSourcesDefault()
    {
        Vm source = await NewSourceAsync();
        var service = new VmCloneService(_repository, _disks);

        Vm clone = await service.CloneAsync(source, "copy", CloneMode.Full);

        Assert.True(MacAddress.IsValid(clone.Config.Network.MacAddress));
        Assert.NotEqual(source.Config.Network.MacAddress, clone.Config.Network.MacAddress);
    }

    [Fact]
    public async Task Clone_DetachesTheInstallerIso()
    {
        Vm source = await _repository.CreateAsync(new VmConfig
        {
            Name = "base",
            Disks = [new DiskConfig { File = "disk.qcow2", Format = "qcow2", Interface = "virtio" }],
            RemovableMedia = [new RemovableMediaConfig { Type = "cdrom", File = "ubuntu.iso", Attached = true }],
        });
        var service = new VmCloneService(_repository, _disks);

        Vm clone = await service.CloneAsync(source, "copy", CloneMode.Full);

        Assert.Empty(clone.Config.RemovableMedia);
    }

    [Fact]
    public async Task Clone_RollsBackTheNewVm_WhenADiskOperationFails()
    {
        Vm source = await NewSourceAsync();
        var service = new VmCloneService(_repository, new RecordingDiskService { FailCopy = true });

        await Assert.ThrowsAsync<DiskException>(() => service.CloneAsync(source, "doomed", CloneMode.Full));

        Vm remaining = Assert.Single(await _repository.ListAsync());
        Assert.Equal(source.Config.Id, remaining.Config.Id);
    }

    private Task<Vm> NewSourceAsync() => _repository.CreateAsync(new VmConfig
    {
        Name = "base",
        Disks = [new DiskConfig { File = "disk.qcow2", Format = "qcow2", Interface = "virtio" }],
    });

    private sealed class RecordingDiskService : IDiskService
    {
        public List<(string Source, string Dest)> Copies { get; } = [];

        public List<(string Backing, string Overlay)> Overlays { get; } = [];

        public bool FailCopy { get; init; }

        public Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default)
        {
            if (FailCopy)
            {
                throw new DiskException("qemu-img convert failed");
            }

            Copies.Add((sourcePath, destinationPath));
            return Task.CompletedTask;
        }

        public Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default)
        {
            Overlays.Add((backingPath, overlayPath));
            return Task.CompletedTask;
        }

        public Task RebaseAsync(string imagePath, string newBackingPath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
