namespace Boxwright.Core;

/// <summary>
/// Starts a VM end-to-end and returns a control handle. Abstracted (implemented by
/// <see cref="VmLauncher"/>) so UI orchestration can be unit-tested with a fake
/// launcher — no real QEMU, no real socket.
/// </summary>
public interface IVmLauncher
{
    /// <summary>Starts <paramref name="vm"/> and returns a handle for controlling it.</summary>
    Task<IRunningVm> StartAsync(Vm vm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-adopts <paramref name="vm"/> if a QEMU process it launched earlier is still running
    /// (reconnect on app restart, ADR-0014): reconnects QMP and returns a control handle, or null if
    /// the VM has no live process to adopt.
    /// </summary>
    Task<IRunningVm?> AdoptAsync(Vm vm, CancellationToken cancellationToken = default);
}
