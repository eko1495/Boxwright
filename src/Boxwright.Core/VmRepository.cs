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

        string id = string.IsNullOrWhiteSpace(config.Id) ? Guid.NewGuid().ToString() : config.Id;
        VmConfig stamped = string.Equals(config.Id, id, StringComparison.Ordinal) ? config : config with { Id = id };
        await SaveAsync(stamped, cancellationToken);
        return new Vm(Path.Combine(_rootDirectory, id), stamped);
    }

    /// <summary>
    /// Writes a config to its folder under the root (<c>root/&lt;id&gt;/vm.json</c>),
    /// creating the folder if needed. The config must have a non-empty Id.
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
