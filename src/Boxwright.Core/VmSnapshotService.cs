namespace Boxwright.Core;

/// <summary>
/// The default <see cref="IVmSnapshotService"/>: applies the per-disk <see cref="ISnapshotService"/>
/// (qemu-img) across all of a VM's qcow2 disks so internal snapshots stay consistent on a multi-disk VM.
/// The single-disk case (the overwhelming majority) behaves exactly as the underlying per-disk service.
/// </summary>
public sealed class VmSnapshotService : IVmSnapshotService
{
    private readonly ISnapshotService _snapshots;

    /// <summary>Creates a VM snapshot orchestrator over the given per-disk snapshot service.</summary>
    public VmSnapshotService(ISnapshotService snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        _snapshots = snapshots;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VmSnapshot>> ListAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        List<string> disks = Qcow2DiskPaths(vm);

        // The primary disk defines the user-facing list (it holds the VM-state blob and timestamp).
        IReadOnlyList<VmSnapshot> primary = await _snapshots.ListAsync(disks[0], cancellationToken);
        if (disks.Count == 1)
        {
            return primary;
        }

        // A snapshot is restorable only if it exists on every qcow2 disk — intersect by tag.
        var otherDiskTags = new List<HashSet<string>>(disks.Count - 1);
        for (int i = 1; i < disks.Count; i++)
        {
            IReadOnlyList<VmSnapshot> snaps = await _snapshots.ListAsync(disks[i], cancellationToken);
            otherDiskTags.Add(snaps.Select(s => s.Name).ToHashSet(StringComparer.Ordinal));
        }

        return [.. primary.Where(s => otherDiskTags.All(tags => tags.Contains(s.Name)))];
    }

    /// <inheritdoc />
    public async Task CreateAsync(Vm vm, string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        List<string> disks = Qcow2DiskPaths(vm);

        var done = new List<string>(disks.Count);
        try
        {
            foreach (string disk in disks)
            {
                await _snapshots.CreateAsync(disk, tag, cancellationToken);
                done.Add(disk);
            }
        }
        catch
        {
            // Partial failure: undo the disks we did snapshot so the tag is all-or-nothing across the VM.
            foreach (string disk in done)
            {
                try
                {
                    await _snapshots.DeleteAsync(disk, tag, cancellationToken);
                }
                catch (DiskException)
                {
                    // Best-effort rollback; surface the original failure below.
                }
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task RestoreAsync(Vm vm, string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        List<string> disks = Qcow2DiskPaths(vm);

        // Validate the tag exists on EVERY disk before mutating any — a half-restore would leave the VM's
        // disks at different points in time, which is worse than refusing.
        foreach (string disk in disks)
        {
            if (!await HasTagAsync(disk, tag, cancellationToken))
            {
                throw new DiskException(
                    $"Snapshot '{tag}' is incomplete: it is missing on '{Path.GetFileName(disk)}', so the VM can't be restored to it consistently.");
            }
        }

        foreach (string disk in disks)
        {
            await _snapshots.RestoreAsync(disk, tag, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Vm vm, string tag, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        List<string> disks = Qcow2DiskPaths(vm);

        // Delete from each disk that has it; tolerate disks lacking the tag so an incomplete snapshot
        // (e.g. left by a partial create on an older build) can still be cleaned up.
        foreach (string disk in disks)
        {
            if (await HasTagAsync(disk, tag, cancellationToken))
            {
                await _snapshots.DeleteAsync(disk, tag, cancellationToken);
            }
        }
    }

    private async Task<bool> HasTagAsync(string disk, string tag, CancellationToken cancellationToken)
    {
        IReadOnlyList<VmSnapshot> snaps = await _snapshots.ListAsync(disk, cancellationToken);
        return snaps.Any(s => string.Equals(s.Name, tag, StringComparison.Ordinal));
    }

    private static List<string> Qcow2DiskPaths(Vm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        List<string> paths = vm.Config.Disks
            .Where(d => string.Equals(d.Format, "qcow2", StringComparison.OrdinalIgnoreCase))
            .Select(d => Path.Combine(vm.FolderPath, d.File))
            .ToList();

        if (paths.Count == 0)
        {
            throw new DiskException($"VM '{vm.Config.Name}' has no qcow2 disk to snapshot.");
        }

        return paths;
    }
}
