namespace Boxwright.Core;

/// <summary>
/// Deletes a VM, but first guards against orphaning <b>linked clones</b> (ADR-0025): a linked clone is a
/// qcow2 overlay whose backing file lives in the source VM's folder, so deleting the source would corrupt
/// every clone built on it. Templates are the headline case (you stamp instances off them), but the guard
/// applies to any VM that backs a linked clone. Shared by the CLI and GUI delete paths (ADR-0022);
/// implemented by <see cref="VmDeletionService"/>.
/// </summary>
public interface IVmDeletionService
{
    /// <summary>
    /// Finds the VMs that are linked clones backed by <paramref name="vm"/>'s folder (deleting it would
    /// corrupt them). Returns an empty list when nothing depends on it.
    /// </summary>
    Task<IReadOnlyList<Vm>> FindDependentsAsync(Vm vm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes <paramref name="vm"/>'s folder (config, disks, logs). Refuses with a
    /// <see cref="VmHasDependentsException"/> when linked clones depend on it.
    /// </summary>
    /// <exception cref="VmHasDependentsException">Linked clones are backed by this VM's disks.</exception>
    Task DeleteAsync(Vm vm, CancellationToken cancellationToken = default);
}
