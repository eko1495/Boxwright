using Boxwright.Core;

namespace Boxwright.Cli;

/// <summary>Reports whether a VM is currently running. Abstracted so commands are unit-testable.</summary>
internal interface IVmStatusProbe
{
    /// <summary>True if <paramref name="vm"/> has a live recorded QEMU process.</summary>
    bool IsRunning(Vm vm);
}

/// <summary>
/// A lightweight "is this VM running?" check that doesn't open a QMP connection: a VM is
/// running when it has a <c>runtime.json</c> (ADR-0014) whose recorded process is still a
/// live QEMU process. <see cref="IProcessLauncher.Attach"/> already PID-reuse-guards (it only
/// returns a handle for an actual <c>qemu-system</c> process), so a stale record reads as stopped.
/// </summary>
internal sealed class VmStatusProbe : IVmStatusProbe
{
    private readonly IVmRuntimeStore _runtimeStore;
    private readonly IProcessLauncher _processLauncher;

    public VmStatusProbe(IVmRuntimeStore runtimeStore, IProcessLauncher processLauncher)
    {
        ArgumentNullException.ThrowIfNull(runtimeStore);
        ArgumentNullException.ThrowIfNull(processLauncher);
        _runtimeStore = runtimeStore;
        _processLauncher = processLauncher;
    }

    /// <summary>True if <paramref name="vm"/> has a live recorded QEMU process.</summary>
    public bool IsRunning(Vm vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        VmRuntimeState? state = _runtimeStore.TryLoad(vm);
        if (state is null)
        {
            return false;
        }

        IRunningProcess? process = _processLauncher.Attach(state.ProcessId);
        if (process is null)
        {
            return false;
        }

        process.Dispose(); // we only needed liveness; don't keep supervising it
        return true;
    }
}
