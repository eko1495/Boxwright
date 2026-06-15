using Boxwright.Qmp;
using Microsoft.Extensions.Logging;

namespace Boxwright.Core;

/// <summary>
/// Starts a VM end-to-end: detects the accelerator (CORE-4), allocates endpoints
/// (CORE-8), builds the command line (CORE-5), spawns the QEMU process (CORE-7),
/// and connects the QMP client with retry (CORE-8). Returns a <see cref="RunningVm"/>.
/// </summary>
public sealed class VmLauncher : IVmLauncher
{
    private readonly IProcessLauncher _processLauncher;
    private readonly IEndpointAllocator _endpointAllocator;
    private readonly IQmpConnector _qmpConnector;
    private readonly IQgaConnector _qgaConnector;
    private readonly IVmRuntimeStore _runtimeStore;
    private readonly AcceleratorDetector _acceleratorDetector;
    private readonly QemuLocator _locator;
    private readonly ILogger<VmLauncher> _logger;

    /// <summary>Creates a launcher from its collaborators.</summary>
    public VmLauncher(
        IProcessLauncher processLauncher,
        IEndpointAllocator endpointAllocator,
        IQmpConnector qmpConnector,
        IQgaConnector qgaConnector,
        IVmRuntimeStore runtimeStore,
        AcceleratorDetector acceleratorDetector,
        QemuLocator locator,
        ILogger<VmLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(endpointAllocator);
        ArgumentNullException.ThrowIfNull(qmpConnector);
        ArgumentNullException.ThrowIfNull(qgaConnector);
        ArgumentNullException.ThrowIfNull(runtimeStore);
        ArgumentNullException.ThrowIfNull(acceleratorDetector);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(logger);

        _processLauncher = processLauncher;
        _endpointAllocator = endpointAllocator;
        _qmpConnector = qmpConnector;
        _qgaConnector = qgaConnector;
        _runtimeStore = runtimeStore;
        _acceleratorDetector = acceleratorDetector;
        _locator = locator;
        _logger = logger;
    }

    /// <summary>Starts <paramref name="vm"/> and returns a handle for controlling it.</summary>
    public async Task<IRunningVm> StartAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);

        // A template is a clone base, not a bootable VM — booting it would mutate the disk its linked
        // instances overlay (ADR-0025). Refuse before doing any work.
        if (vm.Config.IsTemplate)
        {
            throw new VmConfigException(
                $"'{vm.Config.Name}' is a template; create an instance from it instead of booting it.");
        }

        // Fail fast before spawning QEMU if the config asks for a host-unsupported network mode (ADR-0024).
        NetworkValidation.EnsureSupportedOnHost(vm.Config.Network);

        Accelerator accelerator = _acceleratorDetector.Detect();
        QmpEndpoint qmpEndpoint = _endpointAllocator.AllocateQmpEndpoint();

        // VNC's display number is (port - 5900), so its display port must be ≥ 5900; SPICE takes any free port.
        bool isVnc = string.Equals(vm.Config.Display.Protocol, "vnc", StringComparison.OrdinalIgnoreCase);
        int spicePort = _endpointAllocator.AllocateFreeTcpPort(isVnc ? 5900 : 0);
        int guestAgentPort = _endpointAllocator.AllocateFreeTcpPort();
        (string? uefiCode, string? uefiVars) = PrepareUefiFirmware(vm);

        var context = new QemuLaunchContext
        {
            QmpEndpoint = qmpEndpoint,
            SpicePort = spicePort,
            GuestAgentPort = guestAgentPort,
            UefiCodePath = uefiCode,
            UefiVarsPath = uefiVars,
        };
        IReadOnlyList<string> arguments = CommandLineBuilder.Build(vm.Config, accelerator, context);
        string executable = _locator.ResolveSystemEmulator(vm.Config.Arch);
        _logger.LogInformation(
            "Starting VM '{Name}' (arch {Arch}) with accelerator {Accelerator}.", vm.Config.Name, vm.Config.Arch, accelerator);

        var process = new QemuProcess(_processLauncher, executable, arguments, vm.FolderPath, vm.LogPath, accelerator);
        bool launched = false;
        try
        {
            process.Start();
            IQmpClient client = await _qmpConnector.ConnectAsync(
                qmpEndpoint,
                () => process.State == QemuProcessState.Running,
                cancellationToken);

            int pid = process.ProcessId ?? 0;
            _runtimeStore.Save(vm, VmRuntimeState.From(pid, qmpEndpoint, spicePort, vm.Config.Display.Protocol, guestAgentPort, accelerator));
            var runningVm = new RunningVm(
                process, client, accelerator, spicePort, vm.Config.Display.Protocol,
                _qgaConnector, guestAgentPort, onStopped: () => _runtimeStore.Clear(vm));
            launched = true;
            _logger.LogInformation("VM '{Name}' started; pid {Pid}, SPICE port {Port}.", vm.Config.Name, pid, spicePort);
            return runningVm;
        }
        catch (Exception ex) when (LogLaunchFailure(vm.Config.Name, ex))
        {
            throw; // Not reached: the filter logs and returns false, so the exception propagates (and the finally runs).
        }
        finally
        {
            if (!launched)
            {
                process.Kill();
                process.Dispose();
            }
        }
    }

    /// <summary>
    /// Re-adopts a VM whose QEMU process is still running after an app restart: reads the persisted
    /// runtime state, attaches to the process by id, and reconnects the QMP client. Returns null when
    /// there is nothing to adopt (no record, or the recorded process is gone / not QEMU), clearing a
    /// stale record. Leaves the process running if QMP cannot be reconnected.
    /// </summary>
    public async Task<IRunningVm?> AdoptAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);

        VmRuntimeState? state = _runtimeStore.TryLoad(vm);
        if (state is null)
        {
            return null; // never launched, or cleanly stopped
        }

        IRunningProcess? attached = _processLauncher.Attach(state.ProcessId);
        if (attached is null)
        {
            _runtimeStore.Clear(vm); // the process is gone (or its id was reused) — forget it
            return null;
        }

        QemuProcess process = QemuProcess.Attach(attached, vm.LogPath, state.Accelerator);
        bool adopted = false;
        try
        {
            IQmpClient client = await _qmpConnector.ConnectAsync(
                state.ToQmpEndpoint(),
                () => !attached.HasExited,
                cancellationToken);

            var runningVm = new RunningVm(
                process, client, state.Accelerator, state.SpicePort, state.DisplayProtocol,
                _qgaConnector, state.GuestAgentPort, onStopped: () => _runtimeStore.Clear(vm));
            adopted = true;
            _logger.LogInformation("Reconnected to VM '{Name}' (pid {Pid}).", vm.Config.Name, state.ProcessId);
            return runningVm;
        }
        catch (QmpProtocolException ex)
        {
            // The process is alive but its QMP socket won't answer — leave it running (don't kill the
            // user's VM); just don't adopt it. A Windows Job Object backstop would avoid this orphan.
            _logger.LogWarning(ex, "Could not reconnect QMP to VM '{Name}' (pid {Pid}); leaving it running.", vm.Config.Name, state.ProcessId);
            return null;
        }
        finally
        {
            if (!adopted)
            {
                process.Dispose(); // drop our wrapper/handle; the QEMU process keeps running
            }
        }
    }

    // For UEFI VMs, resolve the OVMF firmware and ensure a writable per-VM VARS (NVRAM) copy
    // exists in the VM folder (Directive 6: VM state stays per-VM). BIOS VMs need nothing.
    private (string? Code, string? Vars) PrepareUefiFirmware(Vm vm)
    {
        if (!string.Equals(vm.Config.Firmware, "uefi", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        UefiFirmware firmware = _locator.ResolveUefiFirmware(vm.Config.Arch);
        string varsPath = Path.Combine(vm.FolderPath, "uefi-vars.fd");
        if (!File.Exists(varsPath))
        {
            File.Copy(firmware.VarsTemplatePath, varsPath);
        }

        return (firmware.CodePath, varsPath);
    }

    // Logs the failure via the exception filter (returns false so the exception still
    // propagates) — keeps the launch log complete without swallowing the error.
    private bool LogLaunchFailure(string name, Exception ex)
    {
        _logger.LogError(ex, "Failed to start VM '{Name}'.", name);
        return false;
    }
}
