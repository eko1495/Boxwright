namespace Boxwright.Core;

/// <summary>
/// Creates and inspects disk images via <c>qemu-img</c> (implemented by
/// <see cref="DiskService"/>). Abstracted so callers — e.g. the New-VM flow — can be
/// unit-tested without invoking qemu-img.
/// </summary>
public interface IDiskService
{
    /// <summary>Creates a disk image of <paramref name="sizeBytes"/> bytes at <paramref name="path"/>.</summary>
    /// <exception cref="DiskException">The <c>qemu-img create</c> invocation failed.</exception>
    Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default);

    /// <summary>
    /// Grows the disk image at <paramref name="path"/> to <paramref name="sizeBytes"/> bytes (used to
    /// expand a flattened cloud image to the requested size — the guest's growpart/cloud-init then
    /// extends the filesystem on first boot). Growing only; callers must not request a smaller size.
    /// </summary>
    /// <exception cref="DiskException">The <c>qemu-img resize</c> invocation failed.</exception>
    Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default);

    /// <summary>Inspects a disk image, returning its parsed metadata.</summary>
    /// <exception cref="DiskException">The <c>qemu-img info</c> invocation failed or could not be parsed.</exception>
    Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Copies a disk image to <paramref name="destinationPath"/> as a standalone image (full clone), preserving <paramref name="format"/>.</summary>
    /// <exception cref="DiskException">The <c>qemu-img convert</c> invocation failed.</exception>
    Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default);

    /// <summary>Creates a qcow2 overlay at <paramref name="overlayPath"/> backed by <paramref name="backingPath"/> (linked clone — shares the backing image).</summary>
    /// <exception cref="DiskException">The <c>qemu-img create</c> invocation failed.</exception>
    Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default);
}
