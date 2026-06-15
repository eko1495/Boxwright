using Boxwright.Cli;
using Boxwright.Core;

namespace Boxwright.Cli.Tests;

/// <summary>Captures CLI output for assertions.</summary>
internal sealed class CapturingOutput : IDisposable
{
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public CliOutput Cli => new(_out, _error);

    public string Out => _out.ToString();

    public string Error => _error.ToString();

    public void Dispose()
    {
        _out.Dispose();
        _error.Dispose();
    }
}

/// <summary>A temp VMs directory plus its repository; deleted on dispose.</summary>
internal sealed class TempVmStore : IDisposable
{
    public TempVmStore()
    {
        Root = Path.Combine(Path.GetTempPath(), "boxwright-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Repository = new VmRepository(Root);
    }

    public string Root { get; }

    public VmRepository Repository { get; }

    /// <summary>Creates and persists a VM with the given name (and optional id), returning it.</summary>
    public Vm Add(string name, string? id = null, IReadOnlyList<DiskConfig>? disks = null)
    {
        var config = new VmConfig
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name,
            Disks = disks ?? [new DiskConfig { File = "disk.qcow2" }],
        };
        Repository.SaveAsync(config).GetAwaiter().GetResult();
        return new Vm(Path.Combine(Root, config.Id), config);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}

/// <summary>An <see cref="IVmStatusProbe"/> that reports a fixed set of VM ids as running.</summary>
internal sealed class FakeStatusProbe : IVmStatusProbe
{
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);

    public void MarkRunning(string vmId) => _running.Add(vmId);

    public bool IsRunning(Vm vm) => _running.Contains(vm.Config.Id);
}

/// <summary>Records snapshot operations and returns a canned list.</summary>
internal sealed class FakeSnapshotService : ISnapshotService
{
    public List<VmSnapshot> Snapshots { get; } = [];

    public List<(string Disk, string Tag)> Created { get; } = [];

    public List<(string Disk, string Tag)> Restored { get; } = [];

    public List<(string Disk, string Tag)> Deleted { get; } = [];

    public Task<IReadOnlyList<VmSnapshot>> ListAsync(string diskPath, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VmSnapshot>>(Snapshots);

    public Task CreateAsync(string diskPath, string tag, CancellationToken cancellationToken = default)
    {
        Created.Add((diskPath, tag));
        return Task.CompletedTask;
    }

    public Task RestoreAsync(string diskPath, string tag, CancellationToken cancellationToken = default)
    {
        Restored.Add((diskPath, tag));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string diskPath, string tag, CancellationToken cancellationToken = default)
    {
        Deleted.Add((diskPath, tag));
        return Task.CompletedTask;
    }
}

/// <summary>Records disk operations without invoking qemu-img.</summary>
internal sealed class FakeDiskService : IDiskService
{
    public List<(string Path, long SizeBytes, string Format)> Created { get; } = [];

    public Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default)
    {
        Created.Add((path, sizeBytes, format));
        return Task.CompletedTask;
    }

    public Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RebaseAsync(string imagePath, string newBackingPath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>Records clone requests and produces a clone VM under the given store.</summary>
internal sealed class FakeVmCloneService : IVmCloneService
{
    private readonly TempVmStore _store;

    public FakeVmCloneService(TempVmStore store) => _store = store;

    public List<(string SourceId, string NewName, CloneMode Mode)> Clones { get; } = [];

    public Task<Vm> CloneAsync(Vm source, string newName, CloneMode mode, CancellationToken cancellationToken = default)
    {
        Clones.Add((source.Config.Id, newName, mode));
        return Task.FromResult(_store.Add(newName));
    }
}

/// <summary>Records catalog-install requests and returns a VM created under the given store.</summary>
internal sealed class FakeCatalogVmInstaller : ICatalogVmInstaller
{
    private readonly TempVmStore _store;

    public FakeCatalogVmInstaller(TempVmStore store) => _store = store;

    public OsCatalogEntry? Entry { get; private set; }

    public CatalogInstallOptions? Options { get; private set; }

    public Task<Vm> CreateAsync(
        OsCatalogEntry entry,
        CatalogInstallOptions options,
        IProgress<IsoDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Entry = entry;
        Options = options;
        progress?.Report(new IsoDownloadProgress(50, 100));
        return Task.FromResult(_store.Add(options.Name));
    }
}

/// <summary>A fake host USB enumerator returning preset devices (or reporting unsupported).</summary>
internal sealed class FakeUsbDeviceEnumerator : IUsbDeviceEnumerator
{
    public bool IsSupported { get; init; } = true;

    public List<HostUsbDevice> Devices { get; } = [];

    public Task<IReadOnlyList<HostUsbDevice>> ListAsync(CancellationToken cancellationToken = default) =>
        IsSupported
            ? Task.FromResult<IReadOnlyList<HostUsbDevice>>(Devices.ToList())
            : throw new NotSupportedException();
}

/// <summary>Returns a fixed catalog.</summary>
internal sealed class FakeOsCatalogSource : IOsCatalogSource
{
    private readonly IReadOnlyList<OsCatalogEntry> _entries;

    public FakeOsCatalogSource(params OsCatalogEntry[] entries) => _entries = entries;

    public Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries);
}
