namespace Boxwright.Core;

/// <summary>
/// The default <see cref="IVmDeletionService"/>. Detects linked-clone dependents by reading each other
/// VM's qcow2 backing pointers (<see cref="IDiskService"/>) and seeing whether any resolves into the
/// target VM's folder — the same backing-chain reasoning <see cref="LiveSnapshotService"/> uses, applied
/// across VMs instead of within one.
/// </summary>
public sealed class VmDeletionService : IVmDeletionService
{
    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;

    /// <summary>Creates a deletion service over the repository and disk service.</summary>
    public VmDeletionService(VmRepository repository, IDiskService diskService)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        _repository = repository;
        _diskService = diskService;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <inheritdoc />
    public async Task<IReadOnlyList<Vm>> FindDependentsAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);

        string targetFolder = Path.GetFullPath(vm.FolderPath);
        IReadOnlyList<Vm> all = await _repository.ListAsync(cancellationToken);

        var dependents = new List<Vm>();
        foreach (Vm other in all)
        {
            if (string.Equals(other.Config.Id, vm.Config.Id, StringComparison.Ordinal))
            {
                continue; // a VM never depends on itself
            }

            if (await IsBackedByAsync(other, targetFolder, cancellationToken))
            {
                dependents.Add(other);
            }
        }

        return dependents;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);

        IReadOnlyList<Vm> dependents = await FindDependentsAsync(vm, cancellationToken);
        if (dependents.Count > 0)
        {
            string names = string.Join(", ", dependents.Select(d => $"'{d.Config.Name}'"));
            throw new VmHasDependentsException(
                $"Can't delete '{vm.Config.Name}': {dependents.Count} linked clone(s) are backed by its disks ({names}). " +
                "Delete those first (or make them full clones).",
                [.. dependents.Select(d => d.Config.Name)]);
        }

        await _repository.DeleteAsync(vm.Config.Id, cancellationToken);
    }

    // True when any of other's qcow2 disks has an immediate backing file inside targetFolder.
    private async Task<bool> IsBackedByAsync(Vm other, string targetFolder, CancellationToken cancellationToken)
    {
        foreach (DiskConfig disk in other.Config.Disks)
        {
            if (!string.Equals(disk.Format, "qcow2", StringComparison.OrdinalIgnoreCase))
            {
                continue; // only qcow2 overlays carry a backing file
            }

            string diskPath = Path.Combine(other.FolderPath, disk.File);
            string? backing;
            try
            {
                backing = (await _diskService.GetInfoAsync(diskPath, cancellationToken)).FullBackingFilename;
            }
            catch (DiskException)
            {
                continue; // can't read this disk's metadata — a broken VM shouldn't block deletes
            }

            if (backing is not null && IsInside(Path.GetFullPath(backing), targetFolder))
            {
                return true;
            }
        }

        return false;
    }

    // Whether path lives inside folder (folder itself doesn't count — equal paths aren't "inside").
    private static bool IsInside(string path, string folder)
    {
        string prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison);
    }
}
