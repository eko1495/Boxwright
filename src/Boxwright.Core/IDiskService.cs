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

    /// <summary>Inspects a disk image, returning its parsed metadata.</summary>
    /// <exception cref="DiskException">The <c>qemu-img info</c> invocation failed or could not be parsed.</exception>
    Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default);
}
