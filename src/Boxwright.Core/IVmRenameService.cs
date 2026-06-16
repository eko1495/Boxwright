namespace Boxwright.Core;

/// <summary>
/// Renames a VM: it updates the display <see cref="VmConfig.Name"/> <b>and</b> re-slugs the VM's on-disk
/// folder to a browsable, human-readable name (ADR-0028). The VM <see cref="VmConfig.Id"/> is the stable
/// internal key and never changes — linked-clone backing chains and every repository lookup key off the id,
/// not the folder name. Shared by the CLI and GUI rename paths (ADR-0022); implemented by
/// <see cref="VmRenameService"/>.
/// <para>
/// The folder move is <b>guarded</b>. It refuses when the VM backs a <b>linked clone</b> (a clone embeds an
/// <em>absolute</em> qcow2 backing path into the source folder, so moving the folder corrupts the clone —
/// the same hazard <see cref="IVmDeletionService"/> guards, and this reuses its detection), and it refuses
/// when the VM appears to be running (an open file would make <see cref="Directory.Move"/> fail or
/// half-complete). The running check is the conservative one Core can make alone — see
/// <see cref="VmRenameService"/> for its limits.
/// </para>
/// </summary>
public interface IVmRenameService
{
    /// <summary>
    /// Computes the collision-free, cross-platform-safe folder name for a VM named <paramref name="name"/>
    /// with id <paramref name="id"/>, avoiding any name in <paramref name="takenFolderNames"/> (matched
    /// case-insensitively on Windows, where the file system is). The result is a kebab-case slug of the name
    /// plus a short id suffix (e.g. <c>ubuntu-dev-1a2b3c4d</c>) so it stays unique even when two VMs share a
    /// display name; Windows-invalid characters and reserved device names are sanitized away.
    /// </summary>
    string ComputeSlug(string name, IEnumerable<string> takenFolderNames, string id);

    /// <summary>
    /// Sets <paramref name="vm"/>'s display name to <paramref name="newName"/> and moves its folder to the
    /// matching slug, returning the relocated <see cref="Vm"/>. The id is unchanged.
    /// </summary>
    /// <exception cref="VmHasDependentsException">A linked clone is backed by this VM's folder (moving it would corrupt the clone).</exception>
    /// <exception cref="InvalidOperationException">The VM appears to be running.</exception>
    Task<Vm> RenameAsync(Vm vm, string newName, CancellationToken cancellationToken = default);
}
