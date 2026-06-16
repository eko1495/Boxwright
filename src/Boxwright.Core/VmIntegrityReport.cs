namespace Boxwright.Core;

/// <summary>
/// The result of checking a VM's qcow2 disks for corruption (<see cref="IVmIntegrityService"/>).
/// Raw disks are skipped (their format can't be checked).
/// </summary>
public sealed record VmIntegrityReport
{
    /// <summary>One entry per checkable (qcow2) disk, in config order.</summary>
    public IReadOnlyList<DiskIntegrity> Disks { get; init; } = [];

    /// <summary>True when at least one disk could be checked (a VM with only raw disks has nothing to check).</summary>
    public bool Checked => Disks.Count > 0;

    /// <summary>True when every checked disk is consistent (no corruptions, no check errors, no check failures).</summary>
    public bool Healthy => Checked && Disks.All(d => d is { Error: null, Result.Healthy: true });
}

/// <summary>One disk's integrity result: either a <see cref="Result"/> or an <see cref="Error"/> explaining why the check couldn't run.</summary>
public sealed record DiskIntegrity
{
    /// <summary>The disk file name (relative to the VM folder).</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>The check result, or null when the check couldn't run (see <see cref="Error"/>).</summary>
    public DiskCheckResult? Result { get; init; }

    /// <summary>Why the check couldn't run (e.g. qemu-img missing), or null on success.</summary>
    public string? Error { get; init; }
}
