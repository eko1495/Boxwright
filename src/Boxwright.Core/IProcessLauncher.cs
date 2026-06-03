namespace Boxwright.Core;

/// <summary>
/// Starts a long-lived child process and returns a handle to supervise it.
/// Separate from <see cref="IProcessRunner"/> (which runs a process to completion):
/// this models a process that keeps running — like a VM — so QEMU's lifecycle is
/// unit-testable with a fake.
/// </summary>
public interface IProcessLauncher
{
    /// <summary>Starts the process described by <paramref name="request"/>.</summary>
    IRunningProcess Start(ProcessLaunchRequest request);

    /// <summary>
    /// Adopts an already-running process by id — for re-managing a VM after the app restarts
    /// (reconnect-on-restart, ADR-0014). Returns null if no such process exists or it isn't a QEMU
    /// process (a PID-reuse guard). The handle has no stdout (the original pipe is gone) and detects
    /// exit by polling.
    /// </summary>
    IRunningProcess? Attach(int processId);
}

/// <summary>Describes a process to launch.</summary>
public sealed record ProcessLaunchRequest
{
    /// <summary>The executable to run.</summary>
    public required string Executable { get; init; }

    /// <summary>The arguments to pass.</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>The working directory, or null to inherit the current one.</summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>A handle to a running child process.</summary>
public interface IRunningProcess : IDisposable
{
    /// <summary>The OS process id.</summary>
    int Id { get; }

    /// <summary>True once the process has exited.</summary>
    bool HasExited { get; }

    /// <summary>The exit code (valid once exited).</summary>
    int ExitCode { get; }

    /// <summary>Raised for each line of combined stdout/stderr.</summary>
    event EventHandler<string>? OutputReceived;

    /// <summary>Raised when the process exits.</summary>
    event EventHandler? Exited;

    /// <summary>Waits for the process to exit.</summary>
    Task WaitForExitAsync(CancellationToken cancellationToken = default);

    /// <summary>Terminates the process and its child tree.</summary>
    void Kill();
}
