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
    public Vm Add(string name, string? id = null, IReadOnlyList<DiskConfig>? disks = null, bool isTemplate = false)
    {
        var config = new VmConfig
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Name = name,
            IsTemplate = isTemplate,
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

/// <summary>Records VM-level snapshot operations and returns a canned list.</summary>
internal sealed class FakeSnapshotService : IVmSnapshotService
{
    public List<VmSnapshot> Snapshots { get; } = [];

    public List<(Vm Vm, string Tag)> Created { get; } = [];

    public List<(Vm Vm, string Tag)> Restored { get; } = [];

    public List<(Vm Vm, string Tag)> Deleted { get; } = [];

    public Task<IReadOnlyList<VmSnapshot>> ListAsync(Vm vm, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<VmSnapshot>>(Snapshots);

    public Task CreateAsync(Vm vm, string tag, CancellationToken cancellationToken = default)
    {
        Created.Add((vm, tag));
        return Task.CompletedTask;
    }

    public Task RestoreAsync(Vm vm, string tag, CancellationToken cancellationToken = default)
    {
        Restored.Add((vm, tag));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Vm vm, string tag, CancellationToken cancellationToken = default)
    {
        Deleted.Add((vm, tag));
        return Task.CompletedTask;
    }
}

/// <summary>Records disk operations without invoking qemu-img.</summary>
internal sealed class FakeDiskService : IDiskService
{
    public List<(string Path, long SizeBytes, string Format)> Created { get; } = [];

    /// <summary>Maps a disk path to its qcow2 backing file (for linked-clone dependency checks). Default: no backing.</summary>
    public Dictionary<string, string> Backing { get; } = new(StringComparer.Ordinal);

    /// <summary>Maps a disk path to its (actual, virtual) size for disk-usage reporting. Default: 0/0.</summary>
    public Dictionary<string, (long Actual, long Virtual)> Sizes { get; } = new(StringComparer.Ordinal);

    public Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default)
    {
        Created.Add((path, sizeBytes, format));
        return Task.CompletedTask;
    }

    public Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        (long actual, long @virtual) = Sizes.TryGetValue(path, out (long Actual, long Virtual) s) ? s : (0, 0);
        return Task.FromResult(new DiskInfo
        {
            FullBackingFilename = Backing.TryGetValue(path, out string? b) ? b : null,
            ActualSize = actual,
            VirtualSize = @virtual,
        });
    }

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

/// <summary>An <see cref="IVmLauncher"/> whose <see cref="AdoptAsync"/> returns a preset running VM (or null).</summary>
internal sealed class FakeVmLauncher : IVmLauncher
{
    public IRunningVm? AdoptResult { get; init; }

    public Task<IRunningVm> StartAsync(Vm vm, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IRunningVm?> AdoptAsync(Vm vm, CancellationToken cancellationToken = default) =>
        Task.FromResult(AdoptResult);
}

/// <summary>A minimal running VM that records USB hot-plug calls (and can simulate QEMU rejecting them).</summary>
internal sealed class FakeRunningVm : IRunningVm
{
    public List<(string Vendor, string Product)> Attached { get; } = [];

    public List<(string Vendor, string Product)> Detached { get; } = [];

    /// <summary>When set, Attach/Detach throw it (e.g. a QmpCommandException) to exercise the error path.</summary>
    public Exception? UsbFailure { get; init; }

    public Accelerator Accelerator => Accelerator.Tcg;

    public int SpicePort => 5900;

    public string DisplayProtocol => "spice";

    public QemuProcessState State => QemuProcessState.Running;

    public event EventHandler? Exited { add { } remove { } }

    public Task AttachUsbAsync(string vendorId, string productId, CancellationToken cancellationToken = default)
    {
        if (UsbFailure is not null)
        {
            return Task.FromException(UsbFailure);
        }

        Attached.Add((vendorId, productId));
        return Task.CompletedTask;
    }

    public Task DetachUsbAsync(string vendorId, string productId, CancellationToken cancellationToken = default)
    {
        if (UsbFailure is not null)
        {
            return Task.FromException(UsbFailure);
        }

        Detached.Add((vendorId, productId));
        return Task.CompletedTask;
    }

    public Task RequestShutdownAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ResumeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SendKeyEventAsync(string qcode, bool down, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task EjectIsoAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SaveStateAsync(string tag, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task LoadStateAsync(string tag, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteStateAsync(string tag, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<IReadOnlyList<string>> GetGuestAddressesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<VmMetricsSample> GetMetricsSampleAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(default(VmMetricsSample));

    public Task TakeLiveSnapshotAsync(IReadOnlyList<LiveSnapshotDiskRequest> disks, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public void ForceStop()
    {
    }

    public Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
