namespace Boxwright.Core;

/// <summary>
/// Orchestrates a VM's qcow2 internal snapshots across <b>all</b> of its qcow2 disks (stopped-only),
/// so a multi-disk VM snapshots and reverts consistently instead of capturing only its first disk.
/// Wraps the per-disk <see cref="ISnapshotService"/> (a thin <c>qemu-img snapshot</c> wrapper) and adds
/// the cross-disk semantics: a snapshot is only "complete" — listed and restorable — when it exists on
/// every qcow2 disk. Implemented by <see cref="VmSnapshotService"/>.
/// </summary>
/// <remarks>
/// This is the cold/offline analogue of <see cref="ILiveSnapshotService"/>, which already spans disks
/// atomically for a running VM (ADR-0021). The VM must be stopped — internal snapshots need exclusive
/// access to each image.
/// </remarks>
public interface IVmSnapshotService
{
    /// <summary>
    /// Lists the VM's complete internal snapshots — those present on every qcow2 disk (the primary disk
    /// carries the VM-state size and timestamp shown). Snapshots missing from some disk are omitted.
    /// </summary>
    /// <exception cref="DiskException">The VM has no qcow2 disk, or a <c>qemu-img info</c> invocation failed.</exception>
    Task<IReadOnlyList<VmSnapshot>> ListAsync(Vm vm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates snapshot <paramref name="tag"/> on every qcow2 disk. All-or-nothing: if a later disk fails,
    /// the snapshot is rolled back off the disks already done so a half-created tag is never left behind.
    /// </summary>
    /// <exception cref="DiskException">The VM has no qcow2 disk, or a <c>qemu-img snapshot</c> invocation failed.</exception>
    Task CreateAsync(Vm vm, string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores every qcow2 disk to snapshot <paramref name="tag"/>. The tag is validated to exist on all
    /// disks before any disk is touched, so the VM is never left with its disks at different points in time.
    /// </summary>
    /// <exception cref="DiskException">The VM has no qcow2 disk, the tag is missing on some disk, or a <c>qemu-img</c> invocation failed.</exception>
    Task RestoreAsync(Vm vm, string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes snapshot <paramref name="tag"/> from every qcow2 disk that has it (disks lacking the tag are
    /// skipped, so an incomplete snapshot can still be cleaned up). The current disk state is unaffected.
    /// </summary>
    /// <exception cref="DiskException">The VM has no qcow2 disk, or a <c>qemu-img</c> invocation failed.</exception>
    Task DeleteAsync(Vm vm, string tag, CancellationToken cancellationToken = default);
}
