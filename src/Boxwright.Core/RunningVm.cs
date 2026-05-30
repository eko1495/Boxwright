using Boxwright.Qmp;

namespace Boxwright.Core;

/// <summary>
/// A running VM: its supervised QEMU process plus the connected QMP client.
/// Exposes power actions (graceful shutdown, force-stop, pause/resume, reset) and
/// the resolved accelerator + display port for the UI to surface.
/// </summary>
public sealed class RunningVm : IRunningVm
{
    private readonly QemuProcess _process;
    private readonly IQmpClient _client;

    internal RunningVm(QemuProcess process, IQmpClient client, Accelerator accelerator, int spicePort, string displayProtocol)
    {
        _process = process;
        _client = client;
        Accelerator = accelerator;
        SpicePort = spicePort;
        DisplayProtocol = displayProtocol;
    }

    /// <summary>The accelerator resolved for this VM (for the UI to surface — ADR-0003).</summary>
    public Accelerator Accelerator { get; }

    /// <summary>The SPICE display port (for the display launcher — CORE-10).</summary>
    public int SpicePort { get; }

    /// <summary>The display protocol the VM was launched with (<c>spice</c> or <c>vnc</c>).</summary>
    public string DisplayProtocol { get; }

    /// <summary>The process lifecycle state.</summary>
    public QemuProcessState State => _process.State;

    /// <summary>Raised when the VM's process exits.</summary>
    public event EventHandler? Exited
    {
        add => _process.Exited += value;
        remove => _process.Exited -= value;
    }

    /// <summary>Requests a graceful guest shutdown (ACPI power button) via QMP <c>system_powerdown</c>.</summary>
    public Task RequestShutdownAsync(CancellationToken cancellationToken = default) =>
        _client.ExecuteAsync("system_powerdown", arguments: null, cancellationToken);

    /// <summary>Pauses the guest (QMP <c>stop</c>).</summary>
    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        _client.ExecuteAsync("stop", arguments: null, cancellationToken);

    /// <summary>Resumes the guest (QMP <c>cont</c>).</summary>
    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        _client.ExecuteAsync("cont", arguments: null, cancellationToken);

    /// <summary>Resets the guest (QMP <c>system_reset</c>).</summary>
    public Task ResetAsync(CancellationToken cancellationToken = default) =>
        _client.ExecuteAsync("system_reset", arguments: null, cancellationToken);

    /// <summary>Forcibly terminates the VM process (pulls the plug).</summary>
    public void ForceStop() => _process.Kill();

    /// <summary>
    /// Requests a graceful shutdown and, if the guest has not exited within
    /// <paramref name="gracePeriod"/>, forcibly terminates it.
    /// </summary>
    public async Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        await _client.ExecuteAsync("system_powerdown", arguments: null, cancellationToken);

        using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        graceCts.CancelAfter(gracePeriod);
        try
        {
            await _process.WaitForExitAsync(graceCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Grace period elapsed without a clean shutdown — force it.
            _process.Kill();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        _process.Dispose();
    }
}
