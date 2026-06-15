namespace Boxwright.Core;

/// <summary>
/// Clones a VM into a fresh folder under the repository root: copies the config (new id +
/// name, installer ISO detached, disk-first boot) then either fully copies each disk
/// (independent) or creates a qcow2 overlay backed by the source disk (linked). Linked
/// clones are instant and space-efficient but couple to the source — the backing image must
/// not be modified or deleted while a linked clone exists.
/// </summary>
public sealed class VmCloneService : IVmCloneService
{
    private readonly VmRepository _repository;
    private readonly IDiskService _diskService;

    /// <summary>Creates a clone service over the given repository and disk service.</summary>
    public VmCloneService(VmRepository repository, IDiskService diskService)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(diskService);
        _repository = repository;
        _diskService = diskService;
    }

    /// <inheritdoc />
    public async Task<Vm> CloneAsync(Vm source, string newName, CloneMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        VmConfig newConfig = source.Config with
        {
            Id = string.Empty,           // VmRepository.CreateAsync stamps a fresh GUID + folder
            Name = newName,
            RemovableMedia = [],         // a clone shouldn't carry the source's installer ISO
            Boot = source.Config.Boot with { Order = "c" },
            // Clear the MAC so the clone gets its own (else it collides with the source on a bridge — ADR-0025).
            Network = source.Config.Network with { MacAddress = string.Empty },
        };

        Vm clone = await _repository.CreateAsync(newConfig, cancellationToken);

        try
        {
            foreach (DiskConfig disk in source.Config.Disks)
            {
                string sourceDisk = Path.Combine(source.FolderPath, disk.File);
                string cloneDisk = Path.Combine(clone.FolderPath, disk.File);
                bool isQcow2 = string.Equals(disk.Format, "qcow2", StringComparison.OrdinalIgnoreCase);

                if (mode == CloneMode.Linked && isQcow2)
                {
                    // Absolute backing path: the clone lives in a different folder. Linked clones
                    // are therefore not portable — moving the source breaks the clone.
                    await _diskService.CreateOverlayAsync(sourceDisk, cloneDisk, cancellationToken);
                }
                else
                {
                    await _diskService.CopyAsync(sourceDisk, cloneDisk, disk.Format, cancellationToken);
                }
            }

            return clone;
        }
        catch (Exception ex) when (ex is DiskException or IOException)
        {
            await _repository.DeleteAsync(clone.Config.Id, cancellationToken);
            throw;
        }
    }
}
