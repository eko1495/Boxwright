namespace Boxwright.Core.Tests;

// Fake long-lived process launcher for QemuProcess tests: lets a test emit output
// lines and simulate exit on demand.
internal sealed class FakeProcessLauncher : IProcessLauncher
{
    public ProcessLaunchRequest? LastRequest { get; private set; }

    public FakeRunningProcess? Last { get; private set; }

    /// <summary>The process <see cref="Attach"/> returns (null → "no such process"); set by adopt tests.</summary>
    public FakeRunningProcess? AttachResult { get; set; }

    public int? LastAttachedId { get; private set; }

    public IRunningProcess Start(ProcessLaunchRequest request)
    {
        LastRequest = request;
        Last = new FakeRunningProcess();
        return Last;
    }

    public IRunningProcess? Attach(int processId)
    {
        LastAttachedId = processId;
        return AttachResult;
    }
}

internal sealed class FakeRunningProcess : IRunningProcess
{
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Id => 1234;

    public bool HasExited { get; private set; }

    public int ExitCode { get; private set; }

    public event EventHandler<string>? OutputReceived;

    public event EventHandler? Exited;

    public void EmitOutput(string line) => OutputReceived?.Invoke(this, line);

    public void SimulateExit(int exitCode)
    {
        ExitCode = exitCode;
        HasExited = true;
        _exited.TrySetResult();
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken = default) => _exited.Task.WaitAsync(cancellationToken);

    public void Kill() => SimulateExit(-1);

    public void Dispose()
    {
        // Nothing to release.
    }
}
