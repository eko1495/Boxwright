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
    /// Writes a config back into the VM's <em>actual</em> on-disk folder, creating it for a brand-new VM.
    /// The folder is found by the config's id (<see cref="FindFolderByIdAsync"/>), falling back to
    /// <c>root/&lt;id&gt;</c> when no folder exists yet — so a slug-renamed folder (ADR-0028, where folder
    /// != id) is honored and never orphaned, no matter which edit path calls this. The id stays the stable
    /// internal key; the folder name is cosmetic.
    /// </summary>
    public async Task SaveAsync(VmConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Id))
        {
            throw new ArgumentException("The VM config must have a non-empty Id to be saved.", nameof(config));
        }

        string folder = await FindFolderByIdAsync(config.Id, cancellationToken)
            ?? Path.Combine(_rootDirectory, config.Id);
        Directory.CreateDirectory(folder);
        await VmConfigJson.SaveAsync(Path.Combine(folder, ConfigFileName), config, cancellationToken);
    }

    /// <summary>
    /// Finds the on-disk folder holding the VM with <paramref name="id"/>, or null if none. Because a VM's
    /// folder may be a human-readable slug rather than its id (ADR-0028), callers that hold only an id must
    /// resolve the folder rather than assume <c>root/id</c>. The common case (folder == id) is an O(1)
    /// check; only a renamed VM forces a scan of the root.
    /// </summary>
    private async Task<string?> FindFolderByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            return null;
        }

        string direct = Path.Combine(_rootDirectory, id);
        if (await FolderHoldsIdAsync(direct, id, cancellationToken))
        {
            return direct;
        }

        foreach (string folder in Directory.EnumerateDirectories(_rootDirectory))
        {
            if (!string.Equals(folder, direct, StringComparison.Ordinal)
                && await FolderHoldsIdAsync(folder, id, cancellationToken))
            {
                return folder;
            }
        }

        return null;
    }

    private static async Task<bool> FolderHoldsIdAsync(string folder, string id, CancellationToken cancellationToken)
    {
        string configPath = Path.Combine(folder, ConfigFileName);
        if (!File.Exists(configPath))
        {
            return false;
        }

        try
        {
            VmConfig config = await VmConfigJson.LoadAsync(configPath, cancellationToken);
            return string.Equals(config.Id, id, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is VmConfigException or IOException or UnauthorizedAccessException)
        {
            return false; // an unreadable folder can't be the match we want
        }
    }

    /// <summary>
    /// Writes a VM's config into the exact folder it was loaded from (<see cref="Vm.FolderPath"/>) — an
    /// explicit-folder fast path for callers that already hold the <see cref="Vm"/>, avoiding the id→folder
    /// lookup that <see cref="SaveAsync(VmConfig, CancellationToken)"/> does. Both are folder-safe for a
    /// slug-renamed VM (ADR-0028); this one just skips the scan. The folder must be an immediate child of
    /// this repository's root.
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

        // Resolve the VM's actual folder by id — a slug-renamed VM (ADR-0028) is not at root/id, so a naive
        // root/id delete would silently leave it on disk.
        string folder = await FindFolderByIdAsync(id, cancellationToken) ?? Path.Combine(_rootDirectory, id);
        if (Directory.Exists(folder))
        {
            await Task.Run(() => Directory.Delete(folder, recursive: true), cancellationToken);
        }
    }
}
