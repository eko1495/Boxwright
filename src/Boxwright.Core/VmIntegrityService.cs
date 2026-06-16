namespace Boxwright.Core;

/// <summary>
/// Checks a VM's qcow2 disks for corruption via <c>qemu-img check</c> (<see cref="IDiskService.CheckAsync"/>),
/// across every checkable disk. Shared by the CLI <c>check</c> command and the GUI (ADR-0022). The VM must
/// be stopped — a check on a disk open in a running QEMU reads as corrupt. Implemented by
/// <see cref="VmIntegrityService"/>.
/// </summary>
public interface IVmIntegrityService
{
    /// <summary>
    /// Checks each qcow2 disk of <paramref name="vm"/>. Raw disks are skipped. A disk whose check can't run
    /// (e.g. qemu-img missing) is recorded with an <see cref="DiskIntegrity.Error"/> rather than throwing,
    /// so one unreadable disk doesn't hide the others' results. <paramref name="repair"/> opts into
    /// rewriting the images to fix problems (<c>-r leaks|all</c>) — it can discard unrecoverable data.
    /// </summary>
    Task<VmIntegrityReport> CheckAsync(Vm vm, DiskRepairMode repair = DiskRepairMode.None, CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IVmIntegrityService"/>, over <see cref="IDiskService.CheckAsync"/>.</summary>
public sealed class VmIntegrityService : IVmIntegrityService
{
    private readonly IDiskService _diskService;

    /// <summary>Creates an integrity service over the given disk service.</summary>
    public VmIntegrityService(IDiskService diskService)
    {
        ArgumentNullException.ThrowIfNull(diskService);
        _diskService = diskService;
    }

    /// <inheritdoc />
    public async Task<VmIntegrityReport> CheckAsync(Vm vm, DiskRepairMode repair = DiskRepairMode.None, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);

        var disks = new List<DiskIntegrity>();
        foreach (DiskConfig disk in vm.Config.Disks)
        {
            if (!string.Equals(disk.Format, "qcow2", StringComparison.OrdinalIgnoreCase))
            {
                continue; // qemu-img check only supports formats like qcow2 — skip raw seed/data disks
            }

            string path = Path.Combine(vm.FolderPath, disk.File);
            try
            {
                DiskCheckResult result = await _diskService.CheckAsync(path, repair, cancellationToken);
                disks.Add(new DiskIntegrity { File = disk.File, Result = result });
            }
            catch (Exception ex) when (ex is DiskException or QemuNotFoundException)
            {
                disks.Add(new DiskIntegrity { File = disk.File, Error = ex.Message });
            }
        }

        return new VmIntegrityReport { Disks = disks };
    }
}
