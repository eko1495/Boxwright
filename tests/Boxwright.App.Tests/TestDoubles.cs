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

    public Task EjectIsoAsync(CancellationToken cancellationToken = default) => Record("eject");

    public Task SaveStateAsync(string tag, CancellationToken cancellationToken = default) => Record("savestate");

    public Task LoadStateAsync(string tag, CancellationToken cancellationToken = default) => Record("loadstate");

    public Task DeleteStateAsync(string tag, CancellationToken cancellationToken = default) => Record("deletestate");

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
}

/// <summary>An <see cref="IVmLauncher"/> that always fails to start (e.g. accelerator unavailable).</summary>
internal sealed class ThrowingVmLauncher : IVmLauncher
{
    private readonly Exception _exception;

    public ThrowingVmLauncher(Exception exception) => _exception = exception;

    public Task<IRunningVm> StartAsync(Vm vm, CancellationToken cancellationToken = default) =>
        Task.FromException<IRunningVm>(_exception);
}

/// <summary>A fake disk service that records creates (or fails on demand) instead of invoking qemu-img.</summary>
internal sealed class FakeDiskService : IDiskService
{
    public List<(string Path, long SizeBytes, string Format)> Created { get; } = [];

    public DiskException? FailWith { get; init; }

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
        throw new NotSupportedException();

    public Task CopyAsync(string sourcePath, string destinationPath, string format = "qcow2", CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CreateOverlayAsync(string backingPath, string overlayPath, CancellationToken cancellationToken = default) =>
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
