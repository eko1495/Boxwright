using Boxwright.Qmp;

namespace Boxwright.Core;

/// <summary>
/// Starts a VM end-to-end: detects the accelerator (CORE-4), allocates endpoints
/// (CORE-8), builds the command line (CORE-5), spawns the QEMU process (CORE-7),
/// and connects the QMP client with retry (CORE-8). Returns a <see cref="RunningVm"/>.
/// </summary>
public sealed class VmLauncher : IVmLauncher
{
    private const string LogFileName = "qemu.log";

    private readonly IProcessLauncher _processLauncher;
    private readonly IEndpointAllocator _endpointAllocator;
    private readonly IQmpConnector _qmpConnector;
    private readonly AcceleratorDetector _acceleratorDetector;
    private readonly QemuLocator _locator;

    /// <summary>Creates a launcher from its collaborators.</summary>
    public VmLauncher(
        IProcessLauncher processLauncher,
        IEndpointAllocator endpointAllocator,
        IQmpConnector qmpConnector,
        AcceleratorDetector acceleratorDetector,
        QemuLocator locator)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(endpointAllocator);
        ArgumentNullException.ThrowIfNull(qmpConnector);
        ArgumentNullException.ThrowIfNull(acceleratorDetector);
        ArgumentNullException.ThrowIfNull(locator);

        _processLauncher = processLauncher;
        _endpointAllocator = endpointAllocator;
        _qmpConnector = qmpConnector;
        _acceleratorDetector = acceleratorDetector;
        _locator = locator;
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
        string logPath = Path.Combine(vm.FolderPath, LogFileName);

        var process = new QemuProcess(_processLauncher, executable, arguments, vm.FolderPath, logPath);
        bool launched = false;
        try
        {
            process.Start();
            IQmpClient client = await _qmpConnector.ConnectAsync(
                qmpEndpoint,
                () => process.State == QemuProcessState.Running,
                cancellationToken);

            var runningVm = new RunningVm(process, client, accelerator, spicePort);
            launched = true;
            return runningVm;
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
}
