namespace Boxwright.Core;

/// <summary>
/// A VM's on-disk footprint: the actual (allocated) and virtual (logical) bytes of each configured
/// disk, summed. Actual bytes are what the VM occupies on the host drive right now; a thin qcow2 grows
/// toward its virtual size as the guest writes. Produced by <see cref="IVmDiskUsageService"/>.
/// </summary>
public sealed record VmDiskUsage
{
    /// <summary>Total bytes the VM's disks occupy on the host drive (sum of measured disks' actual size).</summary>
    public long ActualBytes { get; init; }

    /// <summary>Total logical capacity of the VM's disks (sum of measured disks' virtual size).</summary>
    public long VirtualBytes { get; init; }

    /// <summary>Per-disk breakdown, in config order.</summary>
    public IReadOnlyList<DiskUsage> Disks { get; init; } = [];

    /// <summary>
    /// True when every configured disk was measured. False when a disk couldn't be read (missing file,
    /// or <c>qemu-img</c> unavailable) — the totals then cover only the disks that were measured.
    /// </summary>
    public bool Complete { get; init; } = true;
}

/// <summary>One disk's footprint within a <see cref="VmDiskUsage"/>.</summary>
public sealed record DiskUsage
{
    /// <summary>The disk file name (relative to the VM folder).</summary>
    public string File { get; init; } = string.Empty;

    /// <summary>Bytes allocated on the host drive (0 when <see cref="Measured"/> is false).</summary>
    public long ActualBytes { get; init; }

    /// <summary>Logical capacity in bytes (0 when <see cref="Measured"/> is false).</summary>
    public long VirtualBytes { get; init; }

    /// <summary>True when <c>qemu-img info</c> read this disk; false when it couldn't (skipped from the totals).</summary>
    public bool Measured { get; init; }
}
