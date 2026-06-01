namespace Boxwright.Core;

/// <summary>How to clone a VM's disk(s).</summary>
public enum CloneMode
{
    /// <summary>An independent copy of each disk (full clone — uses full space, survives changes to the source).</summary>
    Full,

    /// <summary>A qcow2 overlay backed by the source disk (linked clone — instant and small, but the source disk must not change).</summary>
    Linked,
}

/// <summary>
/// Clones a VM into a new self-contained VM folder (implemented by <see cref="VmCloneService"/>).
/// Abstracted so UI orchestration can be unit-tested without invoking qemu-img.
/// </summary>
public interface IVmCloneService
{
    /// <summary>
    /// Creates a new VM named <paramref name="newName"/> from <paramref name="source"/>, copying
    /// (full) or overlaying (linked) its disks. The source must be stopped. Rolls back the new VM
    /// folder if a disk operation fails.
    /// </summary>
    Task<Vm> CloneAsync(Vm source, string newName, CloneMode mode, CancellationToken cancellationToken = default);
}
