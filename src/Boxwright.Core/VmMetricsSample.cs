namespace Boxwright.Core;

/// <summary>
/// A point-in-time snapshot of a running VM's resource counters (ADR-0019), read from the QEMU host
/// process (<see cref="CpuTime"/>, <see cref="WorkingSetBytes"/>) and QMP <c>query-blockstats</c>
/// (<see cref="DiskReadBytes"/>, <see cref="DiskWriteBytes"/>). <see cref="CpuTime"/> and the disk byte
/// counters are <b>cumulative</b> — a caller differences successive samples to derive a CPU % and a disk
/// throughput rate; <see cref="WorkingSetBytes"/> is already an instantaneous value.
/// </summary>
public readonly record struct VmMetricsSample(
    TimeSpan CpuTime,
    long WorkingSetBytes,
    long DiskReadBytes,
    long DiskWriteBytes);

/// <summary>Derived live rates for the performance graphs: CPU %, RAM (MB), and disk throughput (MB/s).</summary>
public readonly record struct VmMetricsRate(double CpuPercent, double MemoryMegabytes, double DiskMegabytesPerSecond);

/// <summary>Turns two <see cref="VmMetricsSample"/>s into displayable rates (ADR-0019). Pure + unit-tested.</summary>
public static class VmMetrics
{
    /// <summary>
    /// Derives the live rates from <paramref name="previous"/> → <paramref name="current"/> over
    /// <paramref name="wallSeconds"/> elapsed. CPU % is the process CPU-time delta as a fraction of wall
    /// time across <paramref name="vCpuCount"/> vCPUs (so 100 % = the VM saturating its vCPUs), clamped to
    /// 0–100; RAM is the current working set; disk is the read+write byte delta per second.
    /// </summary>
    public static VmMetricsRate Derive(VmMetricsSample previous, VmMetricsSample current, double wallSeconds, int vCpuCount)
    {
        int vcpus = Math.Max(1, vCpuCount);
        double cpu = wallSeconds > 0
            ? Math.Clamp((current.CpuTime - previous.CpuTime).TotalSeconds / wallSeconds / vcpus * 100.0, 0, 100)
            : 0;
        double memory = current.WorkingSetBytes / 1_000_000.0;
        long diskDelta = (current.DiskReadBytes - previous.DiskReadBytes) + (current.DiskWriteBytes - previous.DiskWriteBytes);
        double disk = wallSeconds > 0 ? Math.Max(0, diskDelta / 1_000_000.0 / wallSeconds) : 0;
        return new VmMetricsRate(cpu, memory, disk);
    }
}
