using Xunit;

namespace Boxwright.Core.Tests;

// VmDiskUsageService sums each configured disk's actual/virtual size via IDiskService, best-effort.
public sealed class VmDiskUsageServiceTests
{
    private static Vm VmWith(params DiskConfig[] disks) =>
        new("/vms/vm1", new VmConfig { Name = "vm1", Disks = disks });

    private static DiskConfig Disk(string file, string format = "qcow2") => new() { File = file, Format = format };

    [Fact]
    public async Task Sums_actual_and_virtual_across_disks()
    {
        var fake = new FakeDiskService();
        fake.Info[Path.Combine("/vms/vm1", "os.qcow2")] = new DiskInfo { ActualSize = 3_000, VirtualSize = 20_000 };
        fake.Info[Path.Combine("/vms/vm1", "data.qcow2")] = new DiskInfo { ActualSize = 1_500, VirtualSize = 10_000 };
        var service = new VmDiskUsageService(fake);

        VmDiskUsage usage = await service.MeasureAsync(VmWith(Disk("os.qcow2"), Disk("data.qcow2")));

        Assert.Equal(4_500, usage.ActualBytes);
        Assert.Equal(30_000, usage.VirtualBytes);
        Assert.True(usage.Complete);
        Assert.Equal(2, usage.Disks.Count);
        Assert.All(usage.Disks, d => Assert.True(d.Measured));
    }

    [Fact]
    public async Task An_unreadable_disk_is_excluded_and_flagged_incomplete()
    {
        var fake = new FakeDiskService();
        fake.Info[Path.Combine("/vms/vm1", "os.qcow2")] = new DiskInfo { ActualSize = 3_000, VirtualSize = 20_000 };
        fake.Throw.Add(Path.Combine("/vms/vm1", "data.qcow2")); // qemu-img can't read it
        var service = new VmDiskUsageService(fake);

        VmDiskUsage usage = await service.MeasureAsync(VmWith(Disk("os.qcow2"), Disk("data.qcow2")));

        Assert.Equal(3_000, usage.ActualBytes); // only the readable disk counts
        Assert.False(usage.Complete);
        DiskUsage unreadable = usage.Disks.Single(d => d.File == "data.qcow2");
        Assert.False(unreadable.Measured);
        Assert.Equal(0, unreadable.ActualBytes);
    }

    [Fact]
    public async Task Missing_qemu_img_degrades_to_an_empty_incomplete_report()
    {
        var fake = new FakeDiskService { ThrowQemuNotFound = true };
        var service = new VmDiskUsageService(fake);

        VmDiskUsage usage = await service.MeasureAsync(VmWith(Disk("os.qcow2")));

        Assert.Equal(0, usage.ActualBytes);
        Assert.False(usage.Complete);
        Assert.False(usage.Disks.Single().Measured);
    }

    [Fact]
    public async Task No_disks_is_a_complete_zero_report()
    {
        VmDiskUsage usage = await new VmDiskUsageService(new FakeDiskService()).MeasureAsync(VmWith());

        Assert.Equal(0, usage.ActualBytes);
        Assert.Empty(usage.Disks);
        Assert.True(usage.Complete);
    }

    private sealed class FakeDiskService : IDiskService
    {
        public Dictionary<string, DiskInfo> Info { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Throw { get; } = new(StringComparer.Ordinal);

        public bool ThrowQemuNotFound { get; init; }

        public Task<DiskCheckResult> CheckAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default)
        {
            if (ThrowQemuNotFound)
            {
                throw new QemuNotFoundException("qemu-img not found");
            }

            if (Throw.Contains(path))
            {
                throw new DiskException($"qemu-img info failed for {path}");
            }

            return Task.FromResult(Info.TryGetValue(path, out DiskInfo? info) ? info : new DiskInfo());
        }

        public Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RebaseAsync(string imagePath, string newBackingPath, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
