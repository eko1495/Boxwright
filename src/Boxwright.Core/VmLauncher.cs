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
    private readonly AcceleratorDetector _acceleratorDetector;
    private readonly QemuLocator _locator;
    private readonly ILogger<VmLauncher> _logger;

    /// <summary>Creates a launcher from its collaborators.</summary>
    public VmLauncher(
        IProcessLauncher processLauncher,
        IEndpointAllocator endpointAllocator,
        IQmpConnector qmpConnector,
        AcceleratorDetector acceleratorDetector,
        QemuLocator locator,
        ILogger<VmLauncher> logger)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(endpointAllocator);
        ArgumentNullException.ThrowIfNull(qmpConnector);
        ArgumentNullException.ThrowIfNull(acceleratorDetector);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(logger);

        _processLauncher = processLauncher;
        _endpointAllocator = endpointAllocator;
        _qmpConnector = qmpConnector;
        _acceleratorDetector = acceleratorDetector;
        _locator = locator;
        _logger = logger;
    }

    /// <summary>Starts <paramref name="vm"/> and returns a handle for controlling it.</summary>
    public async Task<IRunningVm> StartAsync(Vm vm, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm);

        Accelerator accelerator = _acceleratorDetector.Detect();
        QmpEndpoint qmpEndpoint = _endpointAllocator.AllocateQmpEndpoint();
        int spicePort = _endpointAllocator.AllocateFreeTcpPort();

        var context = new QemuLaunchContext { QmpEndpoint = qmpEndpoint, SpicePort = spicePort };
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

            var runningVm = new RunningVm(process, client, accelerator, spicePort, vm.Config.Display.Protocol);
            launched = true;
            _logger.LogInformation("VM '{Name}' started; SPICE port {Port}.", vm.Config.Name, spicePort);
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

    // Logs the failure via the exception filter (returns false so the exception still
    // propagates) — keeps the launch log complete without swallowing the error.
    private bool LogLaunchFailure(string name, Exception ex)
    {
        _logger.LogError(ex, "Failed to start VM '{Name}'.", name);
        return false;
    }
}
