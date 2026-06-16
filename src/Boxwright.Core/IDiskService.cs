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

    /// <summary>
    /// Runs a read-only consistency check (<c>qemu-img check</c>) on <paramref name="path"/>, returning the
    /// corruption/leak counts. A corrupted image is reported in the result (not thrown); only a failed or
    /// unsupported check throws. The image must not be open in a running QEMU (a live image reads as corrupt).
    /// </summary>
    /// <exception cref="DiskException">The check could not run, or the image format does not support checks (e.g. raw).</exception>
    Task<DiskCheckResult> CheckAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Copies a disk image to <paramref name="destinationPath"/> as a standalone image (full clone), preserving <paramref name="format"/>.</summary>
    /// <exception cref="DiskException">The <c>qemu-img convert</c> invocation failed.</exception>
    Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default);

    /// <summary>Creates a qcow2 overlay at <paramref name="overlayPath"/> backed by <paramref name="backingPath"/> (linked clone — shares the backing image).</summary>
    /// <exception cref="DiskException">The <c>qemu-img create</c> invocation failed.</exception>
    Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites <paramref name="imagePath"/>'s backing file to <paramref name="newBackingPath"/> in <b>safe mode</b>
    /// (qemu-img copies the clusters needed so the image's visible content is unchanged). Used to detach an
    /// intermediate snapshot from the chain before deleting it. The image must not be open in any QEMU process.
    /// </summary>
    /// <exception cref="DiskException">The <c>qemu-img rebase</c> invocation failed.</exception>
    Task RebaseAsync(string imagePath, string newBackingPath, CancellationToken cancellationToken = default);
}
