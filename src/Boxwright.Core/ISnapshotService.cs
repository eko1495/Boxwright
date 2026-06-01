namespace Boxwright.Core;

/// <summary>
/// Manages qcow2 internal snapshots of a disk image via <c>qemu-img snapshot</c>
/// (implemented by <see cref="SnapshotService"/>). The VM must be stopped — internal
/// snapshots need exclusive access to the image. Abstracted so callers can be
/// unit-tested without invoking qemu-img.
/// </summary>
public interface ISnapshotService
{
    /// <summary>Lists the snapshots stored in <paramref name="diskPath"/>.</summary>
    /// <exception cref="DiskException">The <c>qemu-img info</c> invocation failed or could not be parsed.</exception>
    Task<IReadOnlyList<VmSnapshot>> ListAsync(string diskPath, CancellationToken cancellationToken = default);

    /// <summary>Creates snapshot <paramref name="tag"/> capturing the current disk state.</summary>
    /// <exception cref="DiskException">The <c>qemu-img snapshot</c> invocation failed.</exception>
    Task CreateAsync(string diskPath, string tag, CancellationToken cancellationToken = default);

    /// <summary>Restores the disk to snapshot <paramref name="tag"/>, discarding newer state.</summary>
    /// <exception cref="DiskException">The <c>qemu-img snapshot</c> invocation failed.</exception>
    Task RestoreAsync(string diskPath, string tag, CancellationToken cancellationToken = default);

    /// <summary>Deletes snapshot <paramref name="tag"/> (the current disk state is unaffected).</summary>
    /// <exception cref="DiskException">The <c>qemu-img snapshot</c> invocation failed.</exception>
    Task DeleteAsync(string diskPath, string tag, CancellationToken cancellationToken = default);
}
