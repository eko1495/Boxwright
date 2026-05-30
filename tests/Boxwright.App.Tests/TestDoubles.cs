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

    public QemuProcessState State { get; private set; } = QemuProcessState.Running;

    public List<string> Calls { get; } = [];

    public event EventHandler? Exited;

    public Task RequestShutdownAsync(CancellationToken cancellationToken = default) => Record("shutdown");

    public Task PauseAsync(CancellationToken cancellationToken = default) => Record("pause");

    public Task ResumeAsync(CancellationToken cancellationToken = default) => Record("resume");

    public Task ResetAsync(CancellationToken cancellationToken = default) => Record("reset");

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
    public List<int> LaunchedPorts { get; } = [];

    public DisplayException? FailWith { get; set; }

    public void Launch(int spicePort, string host = "127.0.0.1")
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        LaunchedPorts.Add(spicePort);
    }
}
