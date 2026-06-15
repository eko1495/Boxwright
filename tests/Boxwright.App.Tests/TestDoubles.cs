using Boxwright.App.Services;
using Boxwright.Core;

namespace Boxwright.App.Tests;

/// <summary>Runs posted callbacks synchronously, so background-style callbacks are deterministic in tests.</summary>
internal sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

/// <summary>A fake running VM that records the power commands issued to it.</summary>
internal sealed class FakeRunningVm : IRunningVm
{
    public Accelerator Accelerator { get; init; } = Accelerator.Tcg;

    public int SpicePort { get; init; } = 5900;

    public string DisplayProtocol { get; init; } = "spice";

    public QemuProcessState State { get; private set; } = QemuProcessState.Running;

    public List<string> Calls { get; } = [];

    public event EventHandler? Exited;

    public Task RequestShutdownAsync(CancellationToken cancellationToken = default) => Record("shutdown");

    public Task PauseAsync(CancellationToken cancellationToken = default) => Record("pause");

    public Task ResumeAsync(CancellationToken cancellationToken = default) => Record("resume");

    public Task ResetAsync(CancellationToken cancellationToken = default) => Record("reset");

    /// <summary>Records each key press/release event (qcode, down) sent via QMP input-send-event (newest last).</summary>
    public List<(string Qcode, bool Down)> KeyEvents { get; } = [];

    public Task SendKeyEventAsync(string qcode, bool down, CancellationToken cancellationToken = default)
    {
        KeyEvents.Add((qcode, down));
        return Record("keyevent");
    }

    public Task EjectIsoAsync(CancellationToken cancellationToken = default) => Record("eject");

    public Task SaveStateAsync(string tag, CancellationToken cancellationToken = default) => Record("savestate");

    public Task LoadStateAsync(string tag, CancellationToken cancellationToken = default) => Record("loadstate");

    public Task DeleteStateAsync(string tag, CancellationToken cancellationToken = default) => Record("deletestate");

    public Task TakeLiveSnapshotAsync(IReadOnlyList<LiveSnapshotDiskRequest> disks, CancellationToken cancellationToken = default) =>
        Record("take-live-snapshot");

    public List<string> GuestAddresses { get; } = [];

    public Task<IReadOnlyList<string>> GetGuestAddressesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(GuestAddresses.ToList());

    /// <summary>Scripted metric samples returned in order by GetMetricsSampleAsync; the last one repeats.</summary>
    public List<VmMetricsSample> MetricSamples { get; } = [];
    private int _metricIndex;

    public Task<VmMetricsSample> GetMetricsSampleAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("metrics");
        if (MetricSamples.Count == 0)
        {
            return Task.FromResult(default(VmMetricsSample));
        }

        VmMetricsSample sample = MetricSamples[Math.Min(_metricIndex, MetricSamples.Count - 1)];
        _metricIndex++;
        return Task.FromResult(sample);
    }

    public void ForceStop()
    {
        Calls.Add("forcestop");
        State = QemuProcessState.Exited;
    }

    public Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        State = QemuProcessState.Exited;
        return Record("stop");
    }

    public ValueTask DisposeAsync()
    {
        Calls.Add("dispose");
        return ValueTask.CompletedTask;
    }

    /// <summary>Simulates the QEMU process exiting on its own (guest power-off / crash).</summary>
    public void RaiseExited() => Exited?.Invoke(this, EventArgs.Empty);

    private Task Record(string call)
    {
        Calls.Add(call);
        return Task.CompletedTask;
    }
}

/// <summary>An <see cref="IVmLauncher"/> that hands back a pre-built session.</summary>
internal sealed class FakeVmLauncher : IVmLauncher
{
    private readonly IRunningVm _session;

    public FakeVmLauncher(IRunningVm session) => _session = session;

    public Vm? LastVm { get; private set; }

    public Task<IRunningVm> StartAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        LastVm = vm;
        return Task.FromResult(_session);
    }

    /// <summary>The session returned by <see cref="AdoptAsync"/> (null → nothing to adopt); set by reconnect tests.</summary>
    public IRunningVm? AdoptResult { get; set; }

    public Task<IRunningVm?> AdoptAsync(Vm vm, CancellationToken cancellationToken = default) => Task.FromResult(AdoptResult);
}

/// <summary>An <see cref="IVmLauncher"/> that always fails to start (e.g. accelerator unavailable).</summary>
internal sealed class ThrowingVmLauncher : IVmLauncher
{
    private readonly Exception _exception;

    public ThrowingVmLauncher(Exception exception) => _exception = exception;

    public Task<IRunningVm> StartAsync(Vm vm, CancellationToken cancellationToken = default) =>
        Task.FromException<IRunningVm>(_exception);

    public Task<IRunningVm?> AdoptAsync(Vm vm, CancellationToken cancellationToken = default) => Task.FromResult<IRunningVm?>(null);
}

/// <summary>A fake disk service that records operations (or fails on demand) instead of invoking qemu-img.</summary>
internal sealed class FakeDiskService : IDiskService
{
    public List<(string Path, long SizeBytes, string Format)> Created { get; } = [];

    public List<(string Source, string Destination, string Format)> Copied { get; } = [];

    public List<(string Path, long SizeBytes)> Resized { get; } = [];

    /// <summary>Makes <see cref="CreateAsync"/> fail (the ISO-path disk-create step).</summary>
    public DiskException? FailWith { get; init; }

    /// <summary>Makes <see cref="CopyAsync"/> fail (the cloud-image flatten step).</summary>
    public DiskException? CopyFailWith { get; init; }

    /// <summary>The virtual size <see cref="GetInfoAsync"/> reports (a small cloud-image-ish default).</summary>
    public long VirtualSizeBytes { get; set; } = 3L * 1024 * 1024 * 1024;

    public Task CreateAsync(string path, long sizeBytes, string format = "qcow2", CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            return Task.FromException(FailWith);
        }

        Created.Add((path, sizeBytes, format));
        return Task.CompletedTask;
    }

    public Task<DiskInfo> GetInfoAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DiskInfo { VirtualSize = VirtualSizeBytes });

    public Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default)
    {
        if (CopyFailWith is not null)
        {
            return Task.FromException(CopyFailWith);
        }

        Copied.Add((sourcePath, destinationPath, format));
        return Task.CompletedTask;
    }

    public Task ResizeAsync(string path, long sizeBytes, CancellationToken cancellationToken = default)
    {
        Resized.Add((path, sizeBytes));
        return Task.CompletedTask;
    }

    public Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RebaseAsync(string imagePath, string newBackingPath, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>A fake file picker returning a preset path (or null to simulate cancellation).</summary>
internal sealed class FakeFilePicker : IFilePicker
{
    public string? IsoToReturn { get; set; }

    public Task<string?> PickIsoAsync() => Task.FromResult(IsoToReturn);
}

/// <summary>A fake display launcher that records SPICE ports (or fails to simulate a missing viewer).</summary>
internal sealed class FakeDisplayLauncher : IDisplayLauncher
{
    public List<(int Port, string Protocol)> Launches { get; } = [];

    public DisplayException? FailWith { get; set; }

    public void Launch(int port, string protocol = "spice", string host = "127.0.0.1")
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Launches.Add((port, protocol));
    }
}

/// <summary>A fake embedded VNC display that records the windows it was asked to open.</summary>
internal sealed class FakeEmbeddedVncDisplay : IEmbeddedVncDisplay
{
    public List<(string Title, string Host, int Port)> Opens { get; } = [];

    public void Open(string title, string host, int port) => Opens.Add((title, host, port));
}

/// <summary>A fake folder opener that records the path it was asked to reveal, instead of shelling out.</summary>
internal sealed class FakeFolderOpener : IFolderOpener
{
    public string? LastPath { get; private set; }

    public void OpenFolder(string path) => LastPath = path;
}

/// <summary>A fake snapshot service: records operations and keeps an in-memory list, instead of invoking qemu-img.</summary>
internal sealed class FakeSnapshotService : ISnapshotService
{
    public List<string> Calls { get; } = [];

    public List<VmSnapshot> Snapshots { get; } = [];

    public Task<IReadOnlyList<VmSnapshot>> ListAsync(string diskPath, CancellationToken cancellationToken = default)
    {
        Calls.Add($"list:{diskPath}");
        return Task.FromResult<IReadOnlyList<VmSnapshot>>(Snapshots.ToList());
    }

    public Task CreateAsync(string diskPath, string tag, CancellationToken cancellationToken = default)
    {
        Calls.Add($"create:{tag}");
        Snapshots.Add(new VmSnapshot { Name = tag });
        return Task.CompletedTask;
    }

    public Task RestoreAsync(string diskPath, string tag, CancellationToken cancellationToken = default)
    {
        Calls.Add($"restore:{tag}");
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string diskPath, string tag, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete:{tag}");
        Snapshots.RemoveAll(s => s.Name == tag);
        return Task.CompletedTask;
    }
}

/// <summary>A fake clone service: records the request and returns a synthesized clone VM (or fails on demand).</summary>
internal sealed class FakeVmCloneService : IVmCloneService
{
    public List<(string Name, CloneMode Mode)> Clones { get; } = [];

    public DiskException? FailWith { get; init; }

    public Task<Vm> CloneAsync(Vm source, string newName, CloneMode mode, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            return Task.FromException<Vm>(FailWith);
        }

        Clones.Add((newName, mode));
        VmConfig config = source.Config with { Id = $"clone-{newName}", Name = newName };
        return Task.FromResult(new Vm(Path.Combine(Path.GetTempPath(), config.Id), config));
    }
}

/// <summary>A fake live-snapshot service: records calls and serves a scriptable snapshot list (or fails on demand).</summary>
internal sealed class FakeLiveSnapshotService : ILiveSnapshotService
{
    public List<string> Calls { get; } = [];

    public List<LiveSnapshotEntry> Snapshots { get; } = [];

    public DiskException? FailWith { get; set; }

    public Task<Vm> TakeAsync(Vm vm, IRunningVm session, string name, CancellationToken cancellationToken = default)
    {
        Calls.Add($"take:{name}");
        if (FailWith is not null)
        {
            return Task.FromException<Vm>(FailWith);
        }

        Snapshots.Add(new LiveSnapshotEntry { Id = name, Name = name });
        return Task.FromResult(vm);
    }

    public Task<IReadOnlyList<LiveSnapshotEntry>> ListAsync(Vm vm, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LiveSnapshotEntry>>(Snapshots.ToList());

    public Task<Vm> RevertAsync(Vm vm, string snapshotId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"revert:{snapshotId}");
        return FailWith is not null ? Task.FromException<Vm>(FailWith) : Task.FromResult(vm);
    }

    public Task<Vm> DeleteAsync(Vm vm, string snapshotId, CancellationToken cancellationToken = default)
    {
        Calls.Add($"delete:{snapshotId}");
        if (FailWith is not null)
        {
            return Task.FromException<Vm>(FailWith);
        }

        Snapshots.RemoveAll(s => s.Id == snapshotId);
        return Task.FromResult(vm);
    }
}

/// <summary>A fake log reader returning preset content and recording the path it was asked for.</summary>
internal sealed class FakeLogReader : ILogReader
{
    public string? Content { get; set; }

    public string? LastPath { get; private set; }

    public Task<string?> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        LastPath = path;
        return Task.FromResult(Content);
    }
}

/// <summary>A fake OS catalog source returning preset entries (or failing on demand).</summary>
internal sealed class FakeOsCatalogSource : IOsCatalogSource
{
    public List<OsCatalogEntry> Entries { get; } = [];

    public OsCatalogException? FailWith { get; init; }

    public Task<IReadOnlyList<OsCatalogEntry>> GetEntriesAsync(CancellationToken cancellationToken = default) =>
        FailWith is not null
            ? Task.FromException<IReadOnlyList<OsCatalogEntry>>(FailWith)
            : Task.FromResult<IReadOnlyList<OsCatalogEntry>>(Entries.ToList());
}

/// <summary>A fake ISO downloader: records requests and returns a preset path (or fails on demand).</summary>
internal sealed class FakeIsoDownloader : IIsoDownloader
{
    public string ReturnPath { get; init; } = Path.Combine(Path.GetTempPath(), "boxwright-fake.iso");

    public Exception? FailWith { get; init; }

    public List<OsCatalogEntry> Requested { get; } = [];

    public Task<string> EnsureAsync(OsCatalogEntry entry, IProgress<IsoDownloadProgress>? progress = null, bool reverifyCachedContent = false, CancellationToken cancellationToken = default)
    {
        Requested.Add(entry);
        if (FailWith is not null)
        {
            return Task.FromException<string>(FailWith);
        }

        progress?.Report(new IsoDownloadProgress(entry.SizeBytes, entry.SizeBytes));
        return Task.FromResult(ReturnPath);
    }
}

/// <summary>A fake catalog-VM installer: records the (entry, options) it was called with and returns a synthesized VM (or fails on demand).</summary>
internal sealed class FakeCatalogVmInstaller : ICatalogVmInstaller
{
    public OsCatalogEntry? Entry { get; private set; }

    public CatalogInstallOptions? Options { get; private set; }

    public Exception? FailWith { get; init; }

    public Task<Vm> CreateAsync(OsCatalogEntry entry, CatalogInstallOptions options, IProgress<IsoDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Entry = entry;
        Options = options;
        if (FailWith is not null)
        {
            return Task.FromException<Vm>(FailWith);
        }

        progress?.Report(new IsoDownloadProgress(entry.SizeBytes, entry.SizeBytes));
        VmConfig config = new() { Id = $"created-{options.Name}", Name = options.Name };
        return Task.FromResult(new Vm(Path.Combine(Path.GetTempPath(), config.Id), config));
    }
}

/// <summary>A fake Windows autounattend seed generator: records requests and returns a synthetic ISO path.</summary>
internal sealed class FakeAutounattendSeedGenerator : IAutounattendSeedGenerator
{
    public List<(UnattendedAnswers Answers, WindowsInstallOptions Options, bool Uefi, string VmFolder)> Calls { get; } = [];

    public Exception? FailWith { get; init; }

    public string Generate(UnattendedAnswers answers, WindowsInstallOptions options, bool uefi, string vmFolderPath)
    {
        Calls.Add((answers, options, uefi, vmFolderPath));
        if (FailWith is not null)
        {
            throw FailWith;
        }

        return Path.Combine(vmFolderPath, AutounattendSeedGenerator.SeedFileName);
    }
}
