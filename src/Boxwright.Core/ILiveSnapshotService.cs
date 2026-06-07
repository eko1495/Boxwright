namespace Boxwright.Core;

/// <summary>
/// Manages external/live VM snapshots (ADR-0021): take a consistent point-in-time of a <b>running</b> VM via
/// QMP <c>blockdev-snapshot-sync</c>, then list/revert/delete while the VM is <b>stopped</b>. A snapshot is the
/// frozen (read-only) image at the moment it was taken; the live disk is always the top overlay of the qcow2
/// backing chain. Implemented by <see cref="LiveSnapshotService"/>.
/// </summary>
public interface ILiveSnapshotService
{
    /// <summary>
    /// Takes a live snapshot of the running <paramref name="session"/> named <paramref name="name"/>, atomically
    /// across all qcow2 disks. Repoints the VM config to the new overlays and records the snapshot. Returns the
    /// updated <see cref="Vm"/> (its disks now point at the live overlays).
    /// </summary>
    /// <exception cref="DiskException">The VM has no qcow2 disk, or persisting the repointed config failed.</exception>
    Task<Vm> TakeAsync(Vm vm, IRunningVm session, string name, CancellationToken cancellationToken = default);

    /// <summary>Lists the VM's recorded live snapshots (names + timestamps), oldest first.</summary>
    Task<IReadOnlyList<LiveSnapshotEntry>> ListAsync(Vm vm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverts the (stopped) VM to the snapshot <paramref name="snapshotId"/>: layers a fresh overlay over each
    /// frozen file and repoints the config to it. Non-destructive — other snapshots are untouched and the
    /// pre-revert overlay is left in place. Returns the updated <see cref="Vm"/>.
    /// </summary>
    /// <exception cref="DiskException">No such snapshot, or a qemu-img operation failed.</exception>
    Task<Vm> RevertAsync(Vm vm, string snapshotId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the snapshot <paramref name="snapshotId"/> from the (stopped) VM, reclaiming space: rebases every
    /// dependent image off each frozen file (safe mode, preserving content) and then removes it. Returns the
    /// (config-unchanged) <see cref="Vm"/>.
    /// </summary>
    /// <exception cref="DiskException">No such snapshot, the snapshot is the base image (no parent to fold into), or a qemu-img operation failed.</exception>
    Task<Vm> DeleteAsync(Vm vm, string snapshotId, CancellationToken cancellationToken = default);
}
