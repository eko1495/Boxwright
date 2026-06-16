using Microsoft.Extensions.Logging;

namespace Boxwright.Core;

/// <summary>
/// Discovers, loads, creates, and saves VMs as self-contained folders under a
/// root directory — no central registry or database (ADR-0006). The repository
/// owns the layout: each VM lives in <c>root/&lt;id&gt;/</c> with one
/// <c>vm.json</c> plus its disks and logs.
/// </summary>
public sealed class VmRepository
{
    /// <summary>The config file name inside each VM folder.</summary>
    public const string ConfigFileName = "vm.json";

    private readonly string _rootDirectory;
    private readonly ILogger? _logger;

    /// <summary>
    /// Creates a repository over the given VMs root directory. An optional <paramref name="logger"/>
    /// surfaces folders skipped during <see cref="ListAsync"/> (a broken/unreadable <c>vm.json</c>),
    /// which would otherwise vanish from the list with no diagnostic trail.
    /// </summary>
    public VmRepository(string rootDirectory, ILogger<VmRepository>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = rootDirectory;
        _logger = logger;
    }

    /// <summary>The root directory containing per-VM folders.</summary>
    public string RootDirectory => _rootDirectory;

    /// <summary>
    /// The default per-OS VMs root (e.g. <c>%LOCALAPPDATA%\Boxwright\VMs</c> on
    /// Windows, <c>~/.local/share/Boxwright/VMs</c> on Linux). User-overridable by
    /// constructing the repository with a different path.
    /// </summary>
    public static string DefaultRootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Boxwright", "VMs");

    /// <summary>
    /// Scans the root for VM folders (those containing a <c>vm.json</c>) and loads
    /// their configs. Folders with a missing or unreadable config are skipped.
    /// </summary>
    public async Task<IReadOnlyList<Vm>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return [];
        }

        var vms = new List<Vm>();
        foreach (string folder in Directory.EnumerateDirectories(_rootDirectory))
        {
            string configPath = Path.Combine(folder, ConfigFileName);
            if (!File.Exists(configPath))
            {
                continue;
            }

            try
            {
                VmConfig config = await VmConfigJson.LoadAsync(configPath, cancellationToken);
                vms.Add(new Vm(folder, config));
            }
            catch (Exception ex) when (ex is VmConfigException or IOException or UnauthorizedAccessException)
            {
                // Skip folders whose config is invalid or unreadable — but say so, or a broken VM
                // silently disappears from the list with no clue why (disk rot, a bad edit, permissions).
                _logger?.LogWarning(ex, "Skipping VM folder '{Folder}': its {ConfigFile} is missing or unreadable.", folder, ConfigFileName);
            }
        }

        return vms;
    }

    /// <summary>
    /// Creates a new VM under the root and writes its config. Generates a GUID id
    /// when the config has none. Returns the created <see cref="Vm"/>.
    /// </summary>
    public async Task<Vm> CreateAsync(VmConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Stamp identity the config is missing: a fresh GUID id, and a unique NIC MAC so the VM doesn't
        // share QEMU's default MAC with every other VM (a bridge collision — ADR-0025). A caller-supplied
        // id/MAC (e.g. a restored config) is kept as-is.
        string id = string.IsNullOrWhiteSpace(config.Id) ? Guid.NewGuid().ToString() : config.Id;
        string mac = string.IsNullOrWhiteSpace(config.Network.MacAddress)
            ? MacAddress.Generate()
            : config.Network.MacAddress;
        VmConfig stamped = config with { Id = id, Network = config.Network with { MacAddress = mac } };
        await SaveAsync(stamped, cancellationToken);
        return new Vm(Path.Combine(_rootDirectory, id), stamped);
    }

    /// <summary>
    /// Writes a config to <c>root/&lt;id&gt;/vm.json</c>, creating the folder if needed. The config must
    /// have a non-empty Id. Use this only for brand-new VMs (where folder == id by construction, as in
    /// <see cref="CreateAsync"/>); to edit an existing VM use <see cref="SaveAsync(Vm, CancellationToken)"/>,
    /// which writes to the VM's actual on-disk folder — a slugged folder (ADR-0028) is not <c>root/id</c>,
    /// and recomputing the path from the id here would silently orphan it on the next save.
    /// </summary>
    public async Task SaveAsync(VmConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Id))
        {
            throw new ArgumentException("The VM config must have a non-empty Id to be saved.", nameof(config));
        }

        string folder = Path.Combine(_rootDirectory, config.Id);
        Directory.CreateDirectory(folder);
        await VmConfigJson.SaveAsync(Path.Combine(folder, ConfigFileName), config, cancellationToken);
    }

    /// <summary>
    /// Writes a VM's config back into its <em>actual</em> folder (<see cref="Vm.FolderPath"/>), not a path
    /// recomputed from the id. This is the folder-aware save the edit paths must use once a folder can be a
    /// human-readable slug rather than the GUID id (ADR-0028): the id stays the stable internal key inside
    /// <c>vm.json</c>, while the folder name is cosmetic, so the writer must honor the on-disk location it
    /// was handed instead of fabricating <c>root/id</c> and orphaning the slug folder (with its disks and
    /// <c>runtime.json</c>). The read path (<see cref="ListAsync"/>) already tolerates folder != id.
    /// </summary>
    public async Task SaveAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);
        if (string.IsNullOrWhiteSpace(vm.Config.Id))
        {
            throw new ArgumentException("The VM config must have a non-empty Id to be saved.", nameof(vm));
        }

        // The folder is honored as-is (it may be a slug, not root/id), but it must still be an immediate
        // child of this repository's root — writing outside it would scatter VM state and break the
        // "one VM = one folder under the root" model (ADR-0006). This also guards against a Vm handed in
        // from a different root.
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_rootDirectory));
        string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(vm.FolderPath, "..")));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(parent, root, comparison))
        {
            throw new ArgumentException(
                $"The VM folder '{vm.FolderPath}' is not under this repository's root '{_rootDirectory}'.", nameof(vm));
        }

        Directory.CreateDirectory(vm.FolderPath);
        await VmConfigJson.SaveAsync(vm.ConfigPath, vm.Config, cancellationToken);
    }

    /// <summary>
    /// Renames a VM's on-disk folder to <paramref name="newFolderName"/> (a sibling under the same root),
    /// returning the relocated <see cref="Vm"/> with its config unchanged. The move primitive lives here
    /// because the repository owns the layout (ADR-0006); the caller (<see cref="VmRenameService"/>) owns the
    /// safety guards. <see cref="Directory.Move"/> is atomic within the volume (old and new both sit under
    /// the root, so it is always same-volume), which avoids a half-copied folder if the process dies
    /// mid-rename. Throws if a folder of that name already exists.
    /// </summary>
    public Task<Vm> MoveFolderAsync(Vm vm, string newFolderName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFolderName);

        string destination = Path.Combine(_rootDirectory, newFolderName);
        if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(vm.FolderPath), StringComparison.Ordinal))
        {
            return Task.FromResult(vm); // already there — nothing to move
        }

        return Task.Run(
            () =>
            {
                Directory.Move(vm.FolderPath, destination);
                return new Vm(destination, vm.Config);
            },
            cancellationToken);
    }

    /// <summary>
    /// Deletes a VM's folder (config, disks, and logs) under the root. Does nothing
    /// if the folder does not exist. The (potentially disk-heavy) delete runs off the
    /// caller's thread.
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        string folder = Path.Combine(_rootDirectory, id);
        if (Directory.Exists(folder))
        {
            await Task.Run(() => Directory.Delete(folder, recursive: true), cancellationToken);
        }
    }
}
