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
    private readonly IQgaConnector _qgaConnector;
    private readonly int _guestAgentPort;
    private readonly Action? _onStopped;
    private int _onStoppedInvoked;

    internal RunningVm(
        QemuProcess process,
        IQmpClient client,
        Accelerator accelerator,
        int spicePort,
        string displayProtocol,
        IQgaConnector qgaConnector,
        int guestAgentPort,
        Action? onStopped = null)
    {
        _process = process;
        _client = client;
        Accelerator = accelerator;
        SpicePort = spicePort;
        DisplayProtocol = displayProtocol;
        _qgaConnector = qgaConnector;
        _guestAgentPort = guestAgentPort;
        _onStopped = onStopped;
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

    /// <summary>Reads a live resource sample: CPU/RSS from the QEMU host process + disk bytes via QMP (ADR-0019).</summary>
    public async Task<VmMetricsSample> GetMetricsSampleAsync(CancellationToken cancellationToken = default)
    {
        QmpBlockStats disk = await _client.QueryBlockStatsAsync(cancellationToken);
        (TimeSpan cpu, long workingSet) = ReadProcessMetrics();
        return new VmMetricsSample(cpu, workingSet, disk.ReadBytes, disk.WriteBytes);
    }

    // CPU time + working set of the qemu-system process. Returns zeros if the process is gone or
    // inaccessible (the caller polls and tolerates a dropped sample). Cross-platform (Win/mac/Linux).
    private (TimeSpan CpuTime, long WorkingSetBytes) ReadProcessMetrics()
    {
        if (_process.ProcessId is not { } pid)
        {
            return (TimeSpan.Zero, 0);
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return (process.TotalProcessorTime, process.WorkingSet64);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (TimeSpan.Zero, 0); // process exited or not accessible
        }
    }

    /// <summary>Presses/releases a single key in the guest (QMP <c>input-send-event</c>), e.g. to hold Enter across a boot-from-CD prompt.</summary>
    public Task SendKeyEventAsync(string qcode, bool down, CancellationToken cancellationToken = default) =>
        _client.SendKeyEventAsync(qcode, down, cancellationToken);

    /// <summary>Ejects the installer ISO from the running guest (QMP <c>eject</c>, forced past a guest lock).</summary>
    public Task EjectIsoAsync(CancellationToken cancellationToken = default) =>
        _client.ExecuteAsync("eject", new { device = CommandLineBuilder.CdromDriveId, force = true }, cancellationToken);

    /// <summary>Saves the VM state into a qcow2 internal snapshot (QMP-bridged <c>savevm</c>).</summary>
    public async Task SaveStateAsync(string tag, CancellationToken cancellationToken = default)
    {
        string output = await RunMonitorAsync($"savevm {tag}", cancellationToken);
        if (!string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException($"Saving VM state failed: {output.Trim()}");
        }
    }

    /// <summary>Restores the VM from a saved state (QMP-bridged <c>loadvm</c>).</summary>
    public async Task LoadStateAsync(string tag, CancellationToken cancellationToken = default)
    {
        string output = await RunMonitorAsync($"loadvm {tag}", cancellationToken);
        if (!string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException($"Resuming saved state failed: {output.Trim()}");
        }
    }

    /// <summary>Deletes a saved-state snapshot from within the running VM (QMP-bridged <c>delvm</c>) — best-effort.</summary>
    public Task DeleteStateAsync(string tag, CancellationToken cancellationToken = default) =>
        RunMonitorAsync($"delvm {tag}", cancellationToken);

    /// <summary>Returns the guest's IP addresses via the guest agent (empty when no agent is present).</summary>
    public async Task<IReadOnlyList<string>> GetGuestAddressesAsync(CancellationToken cancellationToken = default)
    {
        await using IQgaClient? agent = await _qgaConnector.TryConnectAsync(_guestAgentPort, cancellationToken);
        return agent is null ? [] : await agent.GetIpAddressesAsync(cancellationToken);
    }

    // Tries the guest agent's clean shutdown; false when no agent is present (caller falls back to ACPI).
    private async Task<bool> TryAgentShutdownAsync(CancellationToken cancellationToken)
    {
        await using IQgaClient? agent = await _qgaConnector.TryConnectAsync(_guestAgentPort, cancellationToken);
        if (agent is null)
        {
            return false;
        }

        await agent.ShutdownAsync(cancellationToken);
        return true;
    }

    // savevm/loadvm/delvm have no native QMP form, so they go through the human monitor.
    // The reply is the monitor's text output: empty on success, an error message otherwise.
    private Task<string> RunMonitorAsync(string commandLine, CancellationToken cancellationToken) =>
        _client.ExecuteAsync<string>(
            "human-monitor-command",
            new Dictionary<string, object> { ["command-line"] = commandLine },
            cancellationToken);

    /// <summary>Forcibly terminates the VM process (pulls the plug).</summary>
    public void ForceStop() => _process.Kill();

    /// <summary>
    /// Requests a graceful shutdown and, if the guest has not exited within
    /// <paramref name="gracePeriod"/>, forcibly terminates it.
    /// </summary>
    public async Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default)
    {
        // Prefer the guest agent's clean shutdown (it works even when the guest ignores the
        // ACPI power button); fall back to the ACPI power button when no agent is present.
        if (!await TryAgentShutdownAsync(cancellationToken))
        {
            await _client.ExecuteAsync("system_powerdown", arguments: null, cancellationToken);
        }

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

        // The session is gone — clear any persisted runtime state so a restart won't try to re-adopt
        // a dead process (reconnect-on-restart, ADR-0014). Runs once, after the process is down.
        if (Interlocked.Exchange(ref _onStoppedInvoked, 1) == 0)
        {
            _onStopped?.Invoke();
        }
    }
}
