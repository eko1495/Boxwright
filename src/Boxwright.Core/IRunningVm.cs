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

    /// <summary>
    /// Presses (<paramref name="down"/> = <see langword="true"/>) or releases a single key in the guest
    /// (QMP <c>input-send-event</c>, qcode e.g. <c>ret</c>). Holding a key <i>down</i> across a boot-time
    /// firmware prompt — such as Windows Setup's "Press any key to boot from CD" — is reliably registered,
    /// where discrete presses race the firmware's brief keyboard poll and can be missed.
    /// </summary>
    Task SendKeyEventAsync(string qcode, bool down, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ejects the optical medium (installer ISO) from the running guest via QMP — a
    /// VirtualBox-style live eject, e.g. for the post-install "remove the installation
    /// medium" prompt. No-op-safe targets the drive launched as <see cref="CommandLineBuilder.CdromDriveId"/>.
    /// </summary>
    Task EjectIsoAsync(CancellationToken cancellationToken = default);

    /// <summary>Saves the full VM state (RAM + devices + disk) into a qcow2 internal snapshot named <paramref name="tag"/> (QMP <c>savevm</c>) — the suspend half of save/resume.</summary>
    Task SaveStateAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>Restores the VM from the <paramref name="tag"/> saved state (QMP <c>loadvm</c>). Runs on a freshly-launched process with the same config.</summary>
    Task LoadStateAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>Deletes the <paramref name="tag"/> saved-state snapshot from within the running VM (QMP <c>delvm</c>) — best-effort, to consume a resumed state.</summary>
    Task DeleteStateAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>Returns the guest's IP addresses via the guest agent, or an empty list when no agent is present.</summary>
    Task<IReadOnlyList<string>> GetGuestAddressesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a live resource sample (ADR-0019): CPU time + working set from the QEMU host process and
    /// cumulative disk byte counters via QMP <c>query-blockstats</c>. The caller polls and differences
    /// successive samples to drive the performance graphs.
    /// </summary>
    Task<VmMetricsSample> GetMetricsSampleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a live external snapshot of the running disks: resolves each request's active image to its live
    /// block device via QMP <c>query-block</c> (matched by file path), then issues one
    /// <c>blockdev-snapshot-sync</c> <c>transaction</c> so all disks are snapshotted atomically. On return,
    /// the guest is writing into the new overlays and the previously-active images are frozen read-only.
    /// </summary>
    /// <exception cref="InvalidOperationException">A request's active image has no matching running qcow2 block device.</exception>
    Task TakeLiveSnapshotAsync(IReadOnlyList<LiveSnapshotDiskRequest> disks, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hot-plugs a host USB device into the running guest by vendor:product (QMP <c>device_add</c> with
    /// driver <c>usb-host</c>, ADR-0023), using the stable <see cref="UsbId.DeviceId"/> handle. The
    /// change is live and not persisted; it lasts until the device is unplugged or the VM stops.
    /// </summary>
    /// <exception cref="Boxwright.Qmp.QmpCommandException">QEMU rejected the device (e.g. no such host device, or already attached).</exception>
    Task AttachUsbAsync(string vendorId, string productId, CancellationToken cancellationToken = default);

    /// <summary>Hot-unplugs the USB device with the given vendor:product from the running guest (QMP <c>device_del</c>).</summary>
    /// <exception cref="Boxwright.Qmp.QmpCommandException">No device with that id is attached.</exception>
    Task DetachUsbAsync(string vendorId, string productId, CancellationToken cancellationToken = default);

    /// <summary>Forcibly terminates the VM process (pulls the plug).</summary>
    void ForceStop();

    /// <summary>
    /// Requests a graceful shutdown and, if the guest has not exited within
    /// <paramref name="gracePeriod"/>, forcibly terminates it.
    /// </summary>
    Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken = default);
}
