namespace Boxwright.Core;

/// <summary>
/// Control surface for a running VM (implemented by <see cref="RunningVm"/>): power
/// actions, lifecycle state, and the resolved accelerator / display port. Abstracted
/// so UI orchestration can be unit-tested without launching a real QEMU process.
/// </summary>
public interface IRunningVm : IAsyncDisposable
{
    /// <summary>The accelerator resolved for this VM (for the UI to surface — ADR-0003).</summary>
    Accelerator Accelerator { get; }

    /// <summary>The SPICE display port (for the display launcher — CORE-10).</summary>
    int SpicePort { get; }

    /// <summary>The display protocol the VM was launched with (<c>spice</c> or <c>vnc</c>).</summary>
    string DisplayProtocol { get; }

    /// <summary>The process lifecycle state.</summary>
    QemuProcessState State { get; }

    /// <summary>Raised when the VM's process exits.</summary>
    event EventHandler? Exited;

    /// <summary>Requests a graceful guest shutdown (ACPI power button) via QMP <c>system_powerdown</c>.</summary>
    Task RequestShutdownAsync(CancellationToken cancellationToken = default);

    /// <summary>Pauses the guest (QMP <c>stop</c>).</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Resumes the guest (QMP <c>cont</c>).</summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);

    /// <summary>Resets the guest (QMP <c>system_reset</c>).</summary>
    Task ResetAsync(CancellationToken cancellationToken = default);

    /// <summary>Forcibly terminates the VM process (pulls the plug).</summary>
    void ForceStop();

    /// <summary>
    /// Requests a graceful shutdown and, if the guest has not exited within
    /// <paramref name="gracePeriod"/>, forcibly terminates it.
    /// </summary>
    Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default);
}
