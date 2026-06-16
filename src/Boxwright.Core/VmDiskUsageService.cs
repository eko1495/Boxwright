namespace Boxwright.Core;

/// <summary>
/// Measures a VM's on-disk footprint by reading each configured disk's actual/virtual size via
/// <c>qemu-img info</c> (<see cref="IDiskService"/>). Shared by the CLI (<c>list</c>/<c>info</c>) and the
/// GUI detail panel (ADR-0022). Implemented by <see cref="VmDiskUsageService"/>.
/// </summary>
public interface IVmDiskUsageService
{
    /// <summary>
    /// Sums the actual and virtual size of <paramref name="vm"/>'s configured disks. Best-effort: a disk
    /// that can't be read (missing, or <c>qemu-img</c> unavailable) is reported with
    /// <see cref="DiskUsage.Measured"/> = false and excluded from the totals, with
    /// <see cref="VmDiskUsage.Complete"/> set to false — so a missing tool never fails the caller.
    /// </summary>
    Task<VmDiskUsage> MeasureAsync(Vm vm, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IVmDiskUsageService"/>, over <see cref="IDiskService.GetInfoAsync"/>.</summary>
public sealed class VmDiskUsageService : IVmDiskUsageService
{
    private readonly IDiskService _diskService;

    /// <summary>Creates a disk-usage service over the given disk service.</summary>
    public VmDiskUsageService(IDiskService diskService)
    {
        ArgumentNullException.ThrowIfNull(diskService);
        _diskService = diskService;
    }

    /// <inheritdoc />
    public async Task<VmDiskUsage> MeasureAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);

        var disks = new List<DiskUsage>(vm.Config.Disks.Count);
        long actual = 0;
        long virtual_ = 0;
        bool complete = true;

        foreach (DiskConfig disk in vm.Config.Disks)
        {
            string path = Path.Combine(vm.FolderPath, disk.File);
            try
            {
                DiskInfo info = await _diskService.GetInfoAsync(path, cancellationToken);
                disks.Add(new DiskUsage
                {
                    File = disk.File,
                    ActualBytes = info.ActualSize,
                    VirtualBytes = info.VirtualSize,
                    Measured = true,
                });
                actual += info.ActualSize;
                virtual_ += info.VirtualSize;
            }
            catch (Exception ex) when (ex is DiskException or QemuNotFoundException)
            {
                // Missing file, unreadable image, or no qemu-img — count it as unmeasured rather than failing.
                disks.Add(new DiskUsage { File = disk.File, Measured = false });
                complete = false;
            }
        }

        return new VmDiskUsage
        {
            ActualBytes = actual,
            VirtualBytes = virtual_,
            Disks = disks,
            Complete = complete,
        };
    }
}
